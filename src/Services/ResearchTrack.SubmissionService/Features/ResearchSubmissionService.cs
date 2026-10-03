using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
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

        var submissions = await db.ResearchSubmissions.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.LastSubmittedAt)
            .ToListAsync(cancellationToken);
        if (submissions.Count == 0) return Array.Empty<ResearchSubmissionResponse>();

        // MySql.EntityFrameworkCore 10 can throw while parameterizing multi-value
        // Guid.Contains(...) predicates. Query by the stable project relationship
        // instead of sending Guid arrays to the provider; resolve dictionaries in memory.
        var requirementRows = await db.SubmissionRequirements.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        var requirements = requirementRows.ToDictionary(x => x.Id);

        var versions = await (
                from version in db.SubmissionVersions.AsNoTracking()
                join submission in db.ResearchSubmissions.AsNoTracking()
                    on version.SubmissionId equals submission.Id
                where submission.ProjectId == projectId
                orderby version.VersionNumber descending
                select version)
            .ToListAsync(cancellationToken);

        var reviewRows = await (
                from review in db.SubmissionReviews.AsNoTracking()
                join submission in db.ResearchSubmissions.AsNoTracking()
                    on review.SubmissionId equals submission.Id
                where submission.ProjectId == projectId
                select review)
            .ToListAsync(cancellationToken);
        var reviews = reviewRows.ToDictionary(x => x.VersionId);
        var versionsBySubmission = versions
            .GroupBy(x => x.SubmissionId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<SubmissionVersion>)x.ToArray());

        return submissions
            .Select(x => BuildResponse(
                x,
                requirements[x.RequirementId],
                versionsBySubmission.GetValueOrDefault(x.Id) ?? Array.Empty<SubmissionVersion>(),
                reviews))
            .ToArray();
    }

    public async Task<ResearchSubmissionResponse> GetAsync(Guid projectId, Guid submissionId, CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanAccessAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await LoadResponseAsync(db, projectId, submissionId, cancellationToken);
    }

    public async Task<SubmissionUploadSessionResponse> CreateUploadSessionAsync(
        Guid projectId,
        Guid requirementId,
        Guid userId,
        CreateUploadSessionRequest request,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanAccessAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var requirement = await db.SubmissionRequirements.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == requirementId && x.ProjectId == projectId, cancellationToken)
            ?? throw NotFound("Submission requirement not found.");
        if (requirement.Status != SubmissionConstants.RequirementStatus.Open)
            throw Conflict("This submission requirement is not open for uploads.");

        var existingSubmission = await db.ResearchSubmissions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.RequirementId == requirementId, cancellationToken);

        Guid submissionId;
        int nextVersionNumber;
        if (existingSubmission is null)
        {
            submissionId = Guid.NewGuid();
            nextVersionNumber = 1;
        }
        else
        {
            if (existingSubmission.Status != SubmissionConstants.SubmissionStatus.ChangesRequested)
                throw Conflict("A revised version can only be submitted after the Supervisor requests changes.");
            if (!existingSubmission.CurrentVersionId.HasValue)
                throw Conflict("The current submission version could not be resolved. Refresh and try again.");

            var latestReview = await db.SubmissionReviews.AsNoTracking()
                .SingleOrDefaultAsync(x => x.VersionId == existingSubmission.CurrentVersionId.Value, cancellationToken);
            if (latestReview?.Decision != SubmissionConstants.ReviewDecision.ChangesRequested)
                throw Conflict("The current version has not been formally marked for revision.");

            submissionId = existingSubmission.Id;
            nextVersionNumber = checked(existingSubmission.VersionCount + 1);
        }

        if (await db.SubmissionUploadSessions.AnyAsync(
                x => x.ProjectId == projectId
                     && x.RequirementId == requirementId
                     && x.ExpectedVersionNumber == nextVersionNumber
                     && x.ActiveSlot == "ACTIVE",
                cancellationToken))
            throw Conflict("An upload is already in progress for this submission version.");

        var validated = ValidateUploadRequest(requirement, request);
        var displayName = await _userProfileClient.GetCurrentUserDisplayNameAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var sessionId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var session = new SubmissionUploadSession
        {
            Id = sessionId,
            ProjectId = projectId,
            RequirementId = requirementId,
            SubmissionId = submissionId,
            VersionId = versionId,
            ExpectedVersionNumber = nextVersionNumber,
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
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Another upload session was created at the same time. Refresh and try again.",
                innerException: exception);
        }

        try
        {
            var grant = await _storage.CreateUploadGrantAsync(session.TemporaryObjectKey, session.ExpectedContentType, cancellationToken);
            var expiresAt = grant.ExpiresAt < session.ExpiresAt ? grant.ExpiresAt : session.ExpiresAt;
            return new SubmissionUploadSessionResponse(
                session.Id,
                grant.Url,
                expiresAt,
                nextVersionNumber,
                requirement.MaxFileSizeBytes,
                grant.RequiredHeaders);
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

    public async Task<ResearchSubmissionResponse> CompleteUploadSessionAsync(
        Guid projectId,
        Guid uploadSessionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanAccessAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var session = await db.SubmissionUploadSessions
            .SingleOrDefaultAsync(x => x.Id == uploadSessionId && x.ProjectId == projectId, cancellationToken)
            ?? throw NotFound("Upload session not found.");
        if (session.CreatedBy != userId)
            throw new ApiException(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, "Only the user who started this upload can finalize it.");
        if (session.Status == SubmissionConstants.UploadSessionStatus.Completed)
            return await LoadResponseAsync(db, projectId, session.SubmissionId, cancellationToken);
        if (session.Status != SubmissionConstants.UploadSessionStatus.Pending)
            throw Conflict("This upload session is no longer active.");

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

        var requirement = await db.SubmissionRequirements.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == session.RequirementId && x.ProjectId == projectId, cancellationToken)
            ?? throw NotFound("Submission requirement not found.");
        if (requirement.Status != SubmissionConstants.RequirementStatus.Open)
            throw Conflict("The requirement was closed or archived before the upload was finalized.");

        var submission = await db.ResearchSubmissions
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.RequirementId == session.RequirementId, cancellationToken);
        if (session.ExpectedVersionNumber == 1)
        {
            if (submission is not null)
                throw Conflict("A submission has already been recorded for this requirement.");
        }
        else
        {
            if (submission is null || submission.Id != session.SubmissionId)
                throw Conflict("The submission changed while this revised version was being uploaded.");
            if (submission.Status != SubmissionConstants.SubmissionStatus.ChangesRequested)
                throw Conflict("This submission is no longer accepting a revised version.");
            if (!submission.CurrentVersionId.HasValue)
                throw Conflict("The current submission version could not be resolved.");
            if (submission.VersionCount + 1 != session.ExpectedVersionNumber)
                throw Conflict("A newer version already exists. Refresh before uploading again.");

            var latestReview = await db.SubmissionReviews.AsNoTracking()
                .SingleOrDefaultAsync(x => x.VersionId == submission.CurrentVersionId.Value, cancellationToken);
            if (latestReview?.Decision != SubmissionConstants.ReviewDecision.ChangesRequested)
                throw Conflict("The current version is no longer marked for revision.");
        }

        if (await db.SubmissionVersions.AnyAsync(
                x => x.SubmissionId == session.SubmissionId && x.VersionNumber == session.ExpectedVersionNumber,
                cancellationToken))
            throw Conflict("This version has already been recorded.");

        var metadata = await _storage.GetMetadataAsync(session.TemporaryObjectKey, cancellationToken)
            ?? throw new ApiValidationException([new ApiFieldError("file", ["The uploaded object could not be found in storage."])]);
        ValidateStoredObject(session, metadata);

        await _storage.PromoteAsync(session.TemporaryObjectKey, session.FinalObjectKey, cancellationToken);
        var finalMetadata = await _storage.GetMetadataAsync(session.FinalObjectKey, cancellationToken) ?? metadata;
        var isLate = requirement.DueAt.HasValue && now > requirement.DueAt.Value;

        if (submission is null)
        {
            submission = new ResearchSubmission
            {
                Id = session.SubmissionId,
                ProjectId = projectId,
                RequirementId = session.RequirementId,
                Status = SubmissionConstants.SubmissionStatus.PendingReview,
                CurrentVersionId = session.VersionId,
                VersionCount = 1,
                LastSubmittedAt = now,
                ApprovedVersionId = null,
                ApprovedAt = null,
                CreatedAt = now
            };
            db.ResearchSubmissions.Add(submission);
        }
        else
        {
            submission.Status = SubmissionConstants.SubmissionStatus.PendingReview;
            submission.CurrentVersionId = session.VersionId;
            submission.VersionCount = session.ExpectedVersionNumber;
            submission.LastSubmittedAt = now;
            submission.ApprovedVersionId = null;
            submission.ApprovedAt = null;
            submission.UpdatedAt = now;
        }

        var version = new SubmissionVersion
        {
            Id = session.VersionId,
            SubmissionId = session.SubmissionId,
            VersionNumber = session.ExpectedVersionNumber,
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
            var currentSession = await retryDb.SubmissionUploadSessions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == uploadSessionId, CancellationToken.None);
            if (currentSession?.Status == SubmissionConstants.UploadSessionStatus.Completed)
                return await LoadResponseAsync(retryDb, projectId, currentSession.SubmissionId, CancellationToken.None);

            await TryDeleteFinalAsync(session.FinalObjectKey);
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "The submission changed while this upload was being finalized. Refresh and try again.",
                innerException: exception);
        }

        await TryDeleteTemporaryAsync(session.TemporaryObjectKey);
        return await LoadResponseAsync(db, projectId, submission.Id, cancellationToken);
    }

    public async Task<SubmissionDownloadUrlResponse> GetDownloadUrlAsync(
        Guid projectId,
        Guid submissionId,
        Guid versionId,
        bool inline,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanAccessAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var submission = await db.ResearchSubmissions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == submissionId && x.ProjectId == projectId, cancellationToken)
            ?? throw NotFound("Submission not found.");
        var version = await db.SubmissionVersions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == versionId && x.SubmissionId == submission.Id, cancellationToken)
            ?? throw NotFound("Submission version not found.");
        var grant = await _storage.CreateDownloadGrantAsync(version.ObjectKey, version.OriginalFileName, inline, cancellationToken);
        return new SubmissionDownloadUrlResponse(grant.Url, grant.ExpiresAt);
    }

    public async Task<ResearchSubmissionResponse> ReviewAsync(
        Guid projectId,
        Guid submissionId,
        Guid reviewerId,
        CreateSubmissionReviewRequest request,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var decision = NormalizeDecision(request.Decision);
        var feedback = string.IsNullOrWhiteSpace(request.Feedback) ? null : request.Feedback.Trim();
        var errors = new List<ApiFieldError>();
        if (!SubmissionConstants.ReviewDecision.IsKnown(decision))
            errors.Add(new ApiFieldError("decision", ["Decision must be APPROVED, CHANGES_REQUESTED, or REJECTED."]));
        if (SubmissionConstants.ReviewDecision.RequiresFeedback(decision) && string.IsNullOrWhiteSpace(feedback))
            errors.Add(new ApiFieldError("feedback", ["Feedback is required when requesting changes or rejecting a submission."]));
        if (feedback?.Length > SubmissionConstants.ReviewFeedbackMaxLength)
            errors.Add(new ApiFieldError("feedback", [$"Feedback must be at most {SubmissionConstants.ReviewFeedbackMaxLength} characters."]));
        if (request.VersionId == Guid.Empty)
            errors.Add(new ApiFieldError("versionId", ["A version is required for formal review."]));
        if (errors.Count > 0) throw new ApiValidationException(errors);

        var submission = await db.ResearchSubmissions
            .SingleOrDefaultAsync(x => x.Id == submissionId && x.ProjectId == projectId, cancellationToken)
            ?? throw NotFound("Submission not found.");
        if (submission.Status != SubmissionConstants.SubmissionStatus.PendingReview)
            throw Conflict("Only a submission that is pending review can receive a formal decision.");
        if (!submission.CurrentVersionId.HasValue || submission.CurrentVersionId.Value != request.VersionId)
            throw Conflict("This review is stale because the submission's current version changed. Refresh before reviewing.");

        var versionExists = await db.SubmissionVersions.AsNoTracking()
            .AnyAsync(x => x.Id == request.VersionId && x.SubmissionId == submission.Id, cancellationToken);
        if (!versionExists) throw NotFound("Submission version not found.");
        if (await db.SubmissionReviews.AnyAsync(x => x.VersionId == request.VersionId, cancellationToken))
            throw Conflict("This version already has a formal review.");

        var reviewerName = await _userProfileClient.GetCurrentUserDisplayNameAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var review = new SubmissionReview
        {
            Id = Guid.NewGuid(),
            SubmissionId = submission.Id,
            VersionId = request.VersionId,
            Decision = decision,
            Feedback = feedback,
            ReviewedBy = reviewerId,
            ReviewedByName = reviewerName,
            ReviewedAt = now
        };

        submission.Status = decision switch
        {
            SubmissionConstants.ReviewDecision.Approved => SubmissionConstants.SubmissionStatus.Approved,
            SubmissionConstants.ReviewDecision.ChangesRequested => SubmissionConstants.SubmissionStatus.ChangesRequested,
            SubmissionConstants.ReviewDecision.Rejected => SubmissionConstants.SubmissionStatus.Rejected,
            _ => throw new InvalidOperationException("Unknown review decision.")
        };
        submission.ApprovedVersionId = decision == SubmissionConstants.ReviewDecision.Approved ? request.VersionId : null;
        submission.ApprovedAt = decision == SubmissionConstants.ReviewDecision.Approved ? now : null;
        submission.UpdatedAt = now;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.SubmissionReviews.Add(review);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "The submission was reviewed or changed by another request. Refresh before trying again.",
                innerException: exception);
        }

        return await LoadResponseAsync(db, projectId, submission.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<SubmissionCommentResponse>> ListCommentsAsync(
        Guid projectId,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanAccessAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var exists = await db.ResearchSubmissions.AsNoTracking()
            .AnyAsync(x => x.Id == submissionId && x.ProjectId == projectId, cancellationToken);
        if (!exists) throw NotFound("Submission not found.");

        var comments = await db.SubmissionComments.AsNoTracking()
            .Where(x => x.SubmissionId == submissionId)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        return comments.Select(BuildCommentResponse).ToArray();
    }

    public async Task<SubmissionCommentResponse> AddCommentAsync(
        Guid projectId,
        Guid submissionId,
        Guid authorId,
        string authorRole,
        CreateSubmissionCommentRequest request,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanAccessAsync(projectId, cancellationToken);
        var normalizedRole = (authorRole ?? string.Empty).Trim().ToUpperInvariant();
        if (normalizedRole != AuthSecurityConstants.Roles.Student && normalizedRole != AuthSecurityConstants.Roles.Supervisor)
            throw new ApiException(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, "Only project Students and Supervisors can comment on submissions.");

        var text = (request.Comment ?? string.Empty).Trim();
        var errors = new List<ApiFieldError>();
        if (text.Length == 0)
            errors.Add(new ApiFieldError("comment", ["Comment is required."]));
        else if (text.Length > SubmissionConstants.CommentMaxLength)
            errors.Add(new ApiFieldError("comment", [$"Comment must be at most {SubmissionConstants.CommentMaxLength} characters."]));
        if (errors.Count > 0) throw new ApiValidationException(errors);

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var submission = await db.ResearchSubmissions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == submissionId && x.ProjectId == projectId, cancellationToken)
            ?? throw NotFound("Submission not found.");

        if (request.VersionId.HasValue)
        {
            var versionExists = await db.SubmissionVersions.AsNoTracking()
                .AnyAsync(x => x.Id == request.VersionId.Value && x.SubmissionId == submission.Id, cancellationToken);
            if (!versionExists)
                throw new ApiValidationException([new ApiFieldError("versionId", ["The selected version does not belong to this submission."])]);
        }

        var authorName = await _userProfileClient.GetCurrentUserDisplayNameAsync(cancellationToken);
        var comment = new SubmissionComment
        {
            Id = Guid.NewGuid(),
            SubmissionId = submission.Id,
            VersionId = request.VersionId,
            AuthorId = authorId,
            AuthorName = authorName,
            AuthorRole = normalizedRole,
            Comment = text,
            CreatedAt = _timeProvider.GetUtcNow()
        };
        db.SubmissionComments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);
        return BuildCommentResponse(comment);
    }

    private (string FileName, string Extension, string ContentType, string? Note) ValidateUploadRequest(
        SubmissionRequirement requirement,
        CreateUploadSessionRequest request)
    {
        var errors = new List<ApiFieldError>();
        var fileName = SubmissionFileRules.SafeOriginalFileName(request.FileName ?? string.Empty, _submissionOptions.MaxFileNameLength);
        if (fileName.Length == 0)
            errors.Add(new ApiFieldError("fileName", ["File name is required."]));
        else if ((request.FileName ?? string.Empty).Trim().Length > _submissionOptions.MaxFileNameLength)
            errors.Add(new ApiFieldError("fileName", [$"File name must be at most {_submissionOptions.MaxFileNameLength} characters."]));

        var extension = SubmissionFileRules.ExtensionFromFileName(fileName) ?? string.Empty;
        var requirementTypes = SubmissionFileRules.ParseAllowedTypes(requirement.AllowedFileTypes).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (extension.Length == 0 || !requirementTypes.Contains(extension))
            errors.Add(new ApiFieldError("fileName", [$"Allowed file types: {string.Join(", ", requirementTypes.Select(x => x.ToUpperInvariant()))}."]));

        var contentType = (request.ContentType ?? string.Empty).Trim().ToLowerInvariant();
        if (extension.Length > 0 && !SubmissionFileRules.ContentTypeMatchesExtension(extension, contentType))
            errors.Add(new ApiFieldError("contentType", ["File content type does not match its extension."]));
        if (request.FileSizeBytes <= 0)
            errors.Add(new ApiFieldError("fileSizeBytes", ["File must not be empty."]));
        else if (request.FileSizeBytes > requirement.MaxFileSizeBytes)
            errors.Add(new ApiFieldError("fileSizeBytes", [$"File size must not exceed {requirement.MaxFileSizeBytes} bytes."]));

        var note = string.IsNullOrWhiteSpace(request.SubmissionNote) ? null : request.SubmissionNote.Trim();
        if (note?.Length > SubmissionConstants.SubmissionNoteMaxLength)
            errors.Add(new ApiFieldError("submissionNote", [$"Submission note must be at most {SubmissionConstants.SubmissionNoteMaxLength} characters."]));
        if (errors.Count > 0) throw new ApiValidationException(errors);

        return (fileName, extension, SubmissionFileRules.CanonicalContentType(extension) ?? contentType, note);
    }

    private static void ValidateStoredObject(SubmissionUploadSession session, StoredObjectMetadata metadata)
    {
        var errors = new List<ApiFieldError>();
        if (metadata.ContentLength <= 0)
            errors.Add(new ApiFieldError("file", ["Uploaded file is empty."]));
        if (metadata.ContentLength > session.ExpectedMaxFileSizeBytes)
            errors.Add(new ApiFieldError("file", ["Uploaded file exceeds the requirement size limit."]));
        if (metadata.ContentLength != session.DeclaredFileSizeBytes)
            errors.Add(new ApiFieldError("file", ["Uploaded file size does not match the selected file."]));
        if (!SubmissionFileRules.ContentTypeMatchesExtension(session.FileExtension, metadata.ContentType))
            errors.Add(new ApiFieldError("file", ["Uploaded object content type is invalid."]));
        if (errors.Count > 0) throw new ApiValidationException(errors);
    }

    private async Task TryDeleteTemporaryAsync(string key)
    {
        try
        {
            await _storage.DeleteIfExistsAsync(key, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to remove temporary submission object {ObjectKey}", key);
        }
    }

    private async Task TryDeleteFinalAsync(string key)
    {
        try
        {
            await _storage.DeleteIfExistsAsync(key, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to remove orphaned finalized submission object {ObjectKey}", key);
        }
    }

    private static async Task<ResearchSubmissionResponse> LoadResponseAsync(
        SubmissionDbContext db,
        Guid projectId,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var submission = await db.ResearchSubmissions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == submissionId && x.ProjectId == projectId, cancellationToken)
            ?? throw NotFound("Submission not found.");
        var requirement = await db.SubmissionRequirements.AsNoTracking()
            .SingleAsync(x => x.Id == submission.RequirementId, cancellationToken);
        var versions = await db.SubmissionVersions.AsNoTracking()
            .Where(x => x.SubmissionId == submissionId)
            .OrderByDescending(x => x.VersionNumber)
            .ToListAsync(cancellationToken);
        var reviewRows = await db.SubmissionReviews.AsNoTracking()
            .Where(x => x.SubmissionId == submissionId)
            .ToListAsync(cancellationToken);
        var reviews = reviewRows.ToDictionary(x => x.VersionId);
        return BuildResponse(submission, requirement, versions, reviews);
    }

    private static ResearchSubmissionResponse BuildResponse(
        ResearchSubmission submission,
        SubmissionRequirement requirement,
        IReadOnlyList<SubmissionVersion> versions,
        IReadOnlyDictionary<Guid, SubmissionReview> reviews) =>
        new(
            submission.Id,
            submission.ProjectId,
            submission.RequirementId,
            submission.Status,
            submission.VersionCount,
            submission.CurrentVersionId ?? throw new InvalidOperationException("Completed submission has no current version."),
            submission.ApprovedVersionId,
            submission.LastSubmittedAt,
            submission.ApprovedAt,
            new SubmissionRequirementSummaryResponse(
                requirement.Id,
                requirement.Title,
                requirement.Description,
                requirement.DueAt,
                SubmissionFileRules.ParseAllowedTypes(requirement.AllowedFileTypes),
                requirement.MaxFileSizeBytes,
                requirement.Status),
            versions.Select(v => new SubmissionVersionResponse(
                v.Id,
                v.SubmissionId,
                v.VersionNumber,
                v.OriginalFileName,
                v.FileExtension,
                v.ContentType,
                v.FileSizeBytes,
                v.UploadedBy,
                v.UploadedByName,
                v.SubmissionNote,
                v.SubmittedAt,
                v.IsLate,
                submission.CurrentVersionId == v.Id,
                submission.ApprovedVersionId == v.Id,
                reviews.TryGetValue(v.Id, out var review) ? BuildReviewResponse(review) : null))
                .ToArray());

    private static SubmissionReviewResponse BuildReviewResponse(SubmissionReview review) =>
        new(
            review.Id,
            review.SubmissionId,
            review.VersionId,
            review.Decision,
            review.Feedback,
            review.ReviewedBy,
            review.ReviewedByName,
            review.ReviewedAt);

    private static SubmissionCommentResponse BuildCommentResponse(SubmissionComment comment) =>
        new(
            comment.Id,
            comment.SubmissionId,
            comment.VersionId,
            comment.AuthorId,
            comment.AuthorName,
            comment.AuthorRole,
            comment.Comment,
            comment.CreatedAt);

    private static string NormalizeDecision(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    private static ApiException NotFound(string message) => new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, message);
    private static ApiException Conflict(string message) => new(StatusCodes.Status409Conflict, ErrorCodes.Conflict, message);
}
