using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.SubmissionService.Configuration;
using ResearchTrack.SubmissionService.Contracts;
using ResearchTrack.SubmissionService.Domain;
using ResearchTrack.SubmissionService.Infrastructure;
using ResearchTrack.SubmissionService.Persistence;

namespace ResearchTrack.SubmissionService.Features;

public sealed class ResearchSubmissionService : IResearchSubmissionService
{
    private readonly IDbContextFactory<SubmissionDbContext> _dbContextFactory;
    private readonly IProjectAuthorizationClient _projectAuthorization;
    private readonly IUserProfileClient _userProfileClient;
    private readonly IObjectStorageService _storage;
    private readonly SubmissionOptions _submissionOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ResearchSubmissionService> _logger;

    public ResearchSubmissionService(
        IDbContextFactory<SubmissionDbContext> dbContextFactory,
        IProjectAuthorizationClient projectAuthorization,
        IUserProfileClient userProfileClient,
        IObjectStorageService storage,
        SubmissionOptions submissionOptions,
        TimeProvider timeProvider,
        ILogger<ResearchSubmissionService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _projectAuthorization = projectAuthorization;
        _userProfileClient = userProfileClient;
        _storage = storage;
        _submissionOptions = submissionOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ResearchSubmissionResponse>> ListAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanAccessAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var submissions = await db.ResearchSubmissions.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.LastSubmittedAt).ToListAsync(cancellationToken);
        if (submissions.Count == 0) return Array.Empty<ResearchSubmissionResponse>();
        var ids = submissions.Select(x => x.Id).ToArray();
        var requirementIds = submissions.Select(x => x.RequirementId).Distinct().ToArray();
        var requirements = await db.SubmissionRequirements.AsNoTracking().Where(x => requirementIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var versions = await db.SubmissionVersions.AsNoTracking().Where(x => ids.Contains(x.SubmissionId)).OrderByDescending(x => x.VersionNumber).ToListAsync(cancellationToken);
        var versionsBySubmission = versions.GroupBy(x => x.SubmissionId).ToDictionary(x => x.Key, x => (IReadOnlyList<SubmissionVersion>)x.ToArray());
        return submissions.Select(x => BuildResponse(x, requirements[x.RequirementId], versionsBySubmission.GetValueOrDefault(x.Id) ?? Array.Empty<SubmissionVersion>())).ToArray();
    }

    public async Task<ResearchSubmissionResponse> GetAsync(Guid projectId, Guid submissionId, CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanAccessAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await LoadResponseAsync(db, projectId, submissionId, cancellationToken);
    }

    public async Task<SubmissionUploadSessionResponse> CreateInitialUploadSessionAsync(Guid projectId, Guid requirementId, Guid userId, CreateUploadSessionRequest request, CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanAccessAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var requirement = await db.SubmissionRequirements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == requirementId && x.ProjectId == projectId, cancellationToken)
            ?? throw NotFound("Submission requirement not found.");
        if (requirement.Status != SubmissionConstants.RequirementStatus.Open)
            throw Conflict("This submission requirement is not open for uploads.");
        if (await db.ResearchSubmissions.AnyAsync(x => x.ProjectId == projectId && x.RequirementId == requirementId, cancellationToken))
            throw Conflict("A submission already exists for this requirement. A later resubmission story controls additional versions.");
        if (await db.SubmissionUploadSessions.AnyAsync(x => x.ProjectId == projectId && x.RequirementId == requirementId && x.ActiveSlot == "ACTIVE", cancellationToken))
            throw Conflict("An upload is already in progress for this requirement.");

        var validated = ValidateUploadRequest(requirement, request);
        var displayName = await _userProfileClient.GetCurrentUserDisplayNameAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var sessionId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var session = new SubmissionUploadSession
        {
            Id = sessionId,
            ProjectId = projectId,
            RequirementId = requirementId,
            SubmissionId = submissionId,
            VersionId = versionId,
            ExpectedVersionNumber = 1,
            TemporaryObjectKey = $"pending/{sessionId:N}",
            FinalObjectKey = $"projects/{projectId:N}/requirements/{requirementId:N}/submissions/{submissionId:N}/versions/{versionId:N}",
            OriginalFileName = validated.FileName,
            FileExtension = validated.Extension,
            ExpectedContentType = validated.ContentType,
            DeclaredFileSizeBytes = request.FileSizeBytes,
            ExpectedMaxFileSizeBytes = requirement.MaxFileSizeBytes,
            SubmissionNote = validated.Note,
            CreatedBy = userId,
            CreatedByName = displayName,
            Status = SubmissionConstants.UploadSessionStatus.Pending,
            ActiveSlot = "ACTIVE",
            ExpiresAt = now.AddMinutes(_submissionOptions.UploadSessionLifetimeMinutes),
            CreatedAt = now
        };

        db.SubmissionUploadSessions.Add(session);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.Conflict, "Another upload session was created at the same time. Refresh and try again.", innerException: exception);
        }

        try
        {
            var grant = await _storage.CreateUploadGrantAsync(session.TemporaryObjectKey, session.ExpectedContentType, cancellationToken);
            var expiresAt = grant.ExpiresAt < session.ExpiresAt ? grant.ExpiresAt : session.ExpiresAt;
            return new SubmissionUploadSessionResponse(session.Id, grant.Url, expiresAt, 1, requirement.MaxFileSizeBytes, grant.RequiredHeaders);
        }
        catch
        {
            session.Status = SubmissionConstants.UploadSessionStatus.Failed;
            session.ActiveSlot = null;
            session.FailureReason = "Unable to create storage upload authorization.";
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<ResearchSubmissionResponse> CompleteUploadSessionAsync(Guid projectId, Guid uploadSessionId, Guid userId, CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanAccessAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.SubmissionUploadSessions.SingleOrDefaultAsync(x => x.Id == uploadSessionId && x.ProjectId == projectId, cancellationToken)
            ?? throw NotFound("Upload session not found.");
        if (session.CreatedBy != userId) throw new ApiException(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, "Only the user who started this upload can finalize it.");
        if (session.Status == SubmissionConstants.UploadSessionStatus.Completed)
            return await LoadResponseAsync(db, projectId, session.SubmissionId, cancellationToken);
        if (session.Status != SubmissionConstants.UploadSessionStatus.Pending) throw Conflict("This upload session is no longer active.");

        var now = _timeProvider.GetUtcNow();
        if (session.ExpiresAt <= now)
        {
            session.Status = SubmissionConstants.UploadSessionStatus.Expired;
            session.ActiveSlot = null;
            session.FailureReason = "Upload session expired before completion.";
            await db.SaveChangesAsync(cancellationToken);
            await TryDeleteTemporaryAsync(session.TemporaryObjectKey);
            throw Conflict("This upload session has expired. Start a new upload.");
        }

        var requirement = await db.SubmissionRequirements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == session.RequirementId && x.ProjectId == projectId, cancellationToken)
            ?? throw NotFound("Submission requirement not found.");
        if (requirement.Status != SubmissionConstants.RequirementStatus.Open) throw Conflict("The requirement was closed or archived before the upload was finalized.");
        if (await db.ResearchSubmissions.AnyAsync(x => x.ProjectId == projectId && x.RequirementId == session.RequirementId, cancellationToken))
            throw Conflict("A submission has already been recorded for this requirement.");

        var metadata = await _storage.GetMetadataAsync(session.TemporaryObjectKey, cancellationToken)
            ?? throw new ApiValidationException([new ApiFieldError("file", ["The uploaded object could not be found in storage."])]);
        ValidateStoredObject(session, metadata);

        await _storage.PromoteAsync(session.TemporaryObjectKey, session.FinalObjectKey, cancellationToken);
        var finalMetadata = await _storage.GetMetadataAsync(session.FinalObjectKey, cancellationToken) ?? metadata;
        var isLate = requirement.DueAt.HasValue && now > requirement.DueAt.Value;
        var submission = new ResearchSubmission
        {
            Id = session.SubmissionId,
            ProjectId = projectId,
            RequirementId = session.RequirementId,
            Status = SubmissionConstants.SubmissionStatus.PendingReview,
            CurrentVersionId = session.VersionId,
            VersionCount = 1,
            LastSubmittedAt = now,
            CreatedAt = now
        };
        var version = new SubmissionVersion
        {
            Id = session.VersionId,
            SubmissionId = session.SubmissionId,
            VersionNumber = 1,
            ObjectKey = session.FinalObjectKey,
            OriginalFileName = session.OriginalFileName,
            FileExtension = session.FileExtension,
            ContentType = session.ExpectedContentType,
            FileSizeBytes = finalMetadata.ContentLength,
            ObjectETag = finalMetadata.ETag,
            UploadedBy = session.CreatedBy,
            UploadedByName = session.CreatedByName,
            SubmissionNote = session.SubmissionNote,
            SubmittedAt = now,
            IsLate = isLate
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.ResearchSubmissions.Add(submission);
        db.SubmissionVersions.Add(version);
        session.Status = SubmissionConstants.UploadSessionStatus.Completed;
        session.ActiveSlot = null;
        session.CompletedAt = now;
        session.FailureReason = null;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            await using var retryDb = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None);
            var currentSession = await retryDb.SubmissionUploadSessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == uploadSessionId, CancellationToken.None);
            if (currentSession?.Status == SubmissionConstants.UploadSessionStatus.Completed)
                return await LoadResponseAsync(retryDb, projectId, currentSession.SubmissionId, CancellationToken.None);
            await TryDeleteFinalAsync(session.FinalObjectKey);
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.Conflict, "The submission changed while this upload was being finalized. Refresh and try again.", innerException: exception);
        }

        await TryDeleteTemporaryAsync(session.TemporaryObjectKey);
        return BuildResponse(submission, requirement, [version]);
    }

    public async Task<SubmissionDownloadUrlResponse> GetDownloadUrlAsync(Guid projectId, Guid submissionId, Guid versionId, bool inline, CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanAccessAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var submission = await db.ResearchSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == submissionId && x.ProjectId == projectId, cancellationToken)
            ?? throw NotFound("Submission not found.");
        var version = await db.SubmissionVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == versionId && x.SubmissionId == submission.Id, cancellationToken)
            ?? throw NotFound("Submission version not found.");
        var grant = await _storage.CreateDownloadGrantAsync(version.ObjectKey, version.OriginalFileName, inline, cancellationToken);
        return new SubmissionDownloadUrlResponse(grant.Url, grant.ExpiresAt);
    }

    private (string FileName, string Extension, string ContentType, string? Note) ValidateUploadRequest(SubmissionRequirement requirement, CreateUploadSessionRequest request)
    {
        var errors = new List<ApiFieldError>();
        var fileName = SubmissionFileRules.SafeOriginalFileName(request.FileName ?? string.Empty, _submissionOptions.MaxFileNameLength);
        if (fileName.Length == 0) errors.Add(new("fileName", ["File name is required."]));
        else if ((request.FileName ?? string.Empty).Trim().Length > _submissionOptions.MaxFileNameLength) errors.Add(new("fileName", [$"File name must be at most {_submissionOptions.MaxFileNameLength} characters."]));
        var extension = SubmissionFileRules.ExtensionFromFileName(fileName) ?? string.Empty;
        var requirementTypes = SubmissionFileRules.ParseAllowedTypes(requirement.AllowedFileTypes).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (extension.Length == 0 || !requirementTypes.Contains(extension)) errors.Add(new("fileName", [$"Allowed file types: {string.Join(", ", requirementTypes.Select(x => x.ToUpperInvariant()))}."]));
        var contentType = (request.ContentType ?? string.Empty).Trim().ToLowerInvariant();
        if (extension.Length > 0 && !SubmissionFileRules.ContentTypeMatchesExtension(extension, contentType)) errors.Add(new("contentType", ["File content type does not match its extension."]));
        if (request.FileSizeBytes <= 0) errors.Add(new("fileSizeBytes", ["File must not be empty."]));
        else if (request.FileSizeBytes > requirement.MaxFileSizeBytes) errors.Add(new("fileSizeBytes", [$"File size must not exceed {requirement.MaxFileSizeBytes} bytes."]));
        var note = string.IsNullOrWhiteSpace(request.SubmissionNote) ? null : request.SubmissionNote.Trim();
        if (note?.Length > SubmissionConstants.SubmissionNoteMaxLength) errors.Add(new("submissionNote", [$"Submission note must be at most {SubmissionConstants.SubmissionNoteMaxLength} characters."]));
        if (errors.Count > 0) throw new ApiValidationException(errors);
        return (fileName, extension, SubmissionFileRules.CanonicalContentType(extension) ?? contentType, note);
    }

    private static void ValidateStoredObject(SubmissionUploadSession session, StoredObjectMetadata metadata)
    {
        var errors = new List<ApiFieldError>();
        if (metadata.ContentLength <= 0) errors.Add(new("file", ["Uploaded file is empty."]));
        if (metadata.ContentLength > session.ExpectedMaxFileSizeBytes) errors.Add(new("file", ["Uploaded file exceeds the requirement size limit."]));
        if (metadata.ContentLength != session.DeclaredFileSizeBytes) errors.Add(new("file", ["Uploaded file size does not match the selected file."]));
        if (!SubmissionFileRules.ContentTypeMatchesExtension(session.FileExtension, metadata.ContentType)) errors.Add(new("file", ["Uploaded object content type is invalid."]));
        if (errors.Count > 0) throw new ApiValidationException(errors);
    }

    private async Task TryDeleteTemporaryAsync(string key)
    {
        try { await _storage.DeleteIfExistsAsync(key, CancellationToken.None); }
        catch (Exception exception) { _logger.LogWarning(exception, "Unable to remove temporary submission object {ObjectKey}", key); }
    }

    private async Task TryDeleteFinalAsync(string key)
    {
        try { await _storage.DeleteIfExistsAsync(key, CancellationToken.None); }
        catch (Exception exception) { _logger.LogWarning(exception, "Unable to remove orphaned finalized submission object {ObjectKey}", key); }
    }

    private static async Task<ResearchSubmissionResponse> LoadResponseAsync(SubmissionDbContext db, Guid projectId, Guid submissionId, CancellationToken cancellationToken)
    {
        var submission = await db.ResearchSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == submissionId && x.ProjectId == projectId, cancellationToken)
            ?? throw NotFound("Submission not found.");
        var requirement = await db.SubmissionRequirements.AsNoTracking().SingleAsync(x => x.Id == submission.RequirementId, cancellationToken);
        var versions = await db.SubmissionVersions.AsNoTracking().Where(x => x.SubmissionId == submissionId).OrderByDescending(x => x.VersionNumber).ToListAsync(cancellationToken);
        return BuildResponse(submission, requirement, versions);
    }

    private static ResearchSubmissionResponse BuildResponse(ResearchSubmission submission, SubmissionRequirement requirement, IReadOnlyList<SubmissionVersion> versions) =>
        new(submission.Id, submission.ProjectId, submission.RequirementId, submission.Status, submission.VersionCount,
            submission.CurrentVersionId ?? throw new InvalidOperationException("Completed submission has no current version."), submission.ApprovedVersionId,
            submission.LastSubmittedAt, submission.ApprovedAt,
            new SubmissionRequirementSummaryResponse(requirement.Id, requirement.Title, requirement.Description, requirement.DueAt,
                SubmissionFileRules.ParseAllowedTypes(requirement.AllowedFileTypes), requirement.MaxFileSizeBytes, requirement.Status),
            versions.Select(v => new SubmissionVersionResponse(v.Id, v.SubmissionId, v.VersionNumber, v.OriginalFileName, v.FileExtension, v.ContentType,
                v.FileSizeBytes, v.UploadedBy, v.UploadedByName, v.SubmissionNote, v.SubmittedAt, v.IsLate,
                submission.CurrentVersionId == v.Id, submission.ApprovedVersionId == v.Id)).ToArray());

    private static ApiException NotFound(string message) => new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, message);
    private static ApiException Conflict(string message) => new(StatusCodes.Status409Conflict, ErrorCodes.Conflict, message);
}
