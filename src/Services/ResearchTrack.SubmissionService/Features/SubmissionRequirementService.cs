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

public sealed class SubmissionRequirementService : ISubmissionRequirementService
{
    private readonly IDbContextFactory<SubmissionDbContext> _dbContextFactory;
    private readonly IProjectAuthorizationClient _projectAuthorization;
    private readonly IUserProfileClient _userProfileClient;
    private readonly SubmissionOptions _submissionOptions;
    private readonly StorageOptions _storageOptions;
    private readonly TimeProvider _timeProvider;

    public SubmissionRequirementService(
        IDbContextFactory<SubmissionDbContext> dbContextFactory,
        IProjectAuthorizationClient projectAuthorization,
        IUserProfileClient userProfileClient,
        SubmissionOptions submissionOptions,
        StorageOptions storageOptions,
        TimeProvider timeProvider)
    {
        _dbContextFactory = dbContextFactory;
        _projectAuthorization = projectAuthorization;
        _userProfileClient = userProfileClient;
        _submissionOptions = submissionOptions;
        _storageOptions = storageOptions;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<SubmissionRequirementResponse>> ListAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanAccessAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var requirements = await db.SubmissionRequirements.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.Status == SubmissionConstants.RequirementStatus.Open ? 0 : x.Status == SubmissionConstants.RequirementStatus.Closed ? 1 : 2)
            .ThenBy(x => x.DueAt == null)
            .ThenBy(x => x.DueAt)
            .ThenBy(x => x.Title)
            .ToListAsync(cancellationToken);

        var submissions = await db.ResearchSubmissions.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        // Avoid MySql.EntityFrameworkCore 10 multi-value Guid.Contains(...) parameterization.
        // Load versions through their project-scoped submission relationship and resolve
        // CurrentVersionId in memory.
        var versions = await (
                from version in db.SubmissionVersions.AsNoTracking()
                join submission in db.ResearchSubmissions.AsNoTracking()
                    on version.SubmissionId equals submission.Id
                where submission.ProjectId == projectId
                select version)
            .ToListAsync(cancellationToken);
        var versionById = versions.ToDictionary(x => x.Id);
        var submissionByRequirement = submissions.ToDictionary(x => x.RequirementId);

        return requirements.Select(requirement =>
        {
            submissionByRequirement.TryGetValue(requirement.Id, out var submission);
            SubmissionVersion? current = null;
            if (submission?.CurrentVersionId is Guid currentId) versionById.TryGetValue(currentId, out current);
            return ToResponse(requirement, submission, current);
        }).ToArray();
    }

    public async Task<SubmissionRequirementResponse> GetAsync(Guid projectId, Guid requirementId, CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanAccessAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var requirement = await FindRequiredAsync(db, projectId, requirementId, cancellationToken);
        var submission = await db.ResearchSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.RequirementId == requirementId, cancellationToken);
        SubmissionVersion? current = null;
        if (submission?.CurrentVersionId is Guid currentId)
            current = await db.SubmissionVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == currentId, cancellationToken);
        return ToResponse(requirement, submission, current);
    }

    public async Task<SubmissionRequirementResponse> CreateAsync(Guid projectId, Guid userId, SubmissionRequirementCreateRequest request, CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        var values = Validate(request.Title, request.Description, request.AllowedFileTypes, request.MaxFileSizeBytes);
        ValidateNewDueAt(request.DueAt);
        var displayName = await _userProfileClient.GetCurrentUserDisplayNameAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var requirement = new SubmissionRequirement
        {
            Id = Guid.NewGuid(), ProjectId = projectId, Title = values.Title, Description = values.Description,
            DueAt = request.DueAt, AllowedFileTypes = SubmissionFileRules.SerializeAllowedTypes(values.AllowedTypes),
            MaxFileSizeBytes = request.MaxFileSizeBytes, Status = SubmissionConstants.RequirementStatus.Open,
            CreatedBy = userId, CreatedByName = displayName, CreatedAt = now
        };
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.SubmissionRequirements.Add(requirement);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(requirement, null, null);
    }

    public async Task<SubmissionRequirementResponse> UpdateAsync(Guid projectId, Guid requirementId, SubmissionRequirementUpdateRequest request, CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        var values = Validate(request.Title, request.Description, request.AllowedFileTypes, request.MaxFileSizeBytes);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var requirement = await FindRequiredAsync(db, projectId, requirementId, cancellationToken);
        if (requirement.Status == SubmissionConstants.RequirementStatus.Archived) throw Conflict("Archived requirements are read-only.");
        ValidateUpdatedDueAt(requirement.DueAt, request.DueAt);
        requirement.Title = values.Title;
        requirement.Description = values.Description;
        requirement.DueAt = request.DueAt;
        requirement.AllowedFileTypes = SubmissionFileRules.SerializeAllowedTypes(values.AllowedTypes);
        requirement.MaxFileSizeBytes = request.MaxFileSizeBytes;
        requirement.UpdatedAt = _timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        var submission = await db.ResearchSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.RequirementId == requirementId, cancellationToken);
        SubmissionVersion? current = null;
        if (submission?.CurrentVersionId is Guid currentId) current = await db.SubmissionVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == currentId, cancellationToken);
        return ToResponse(requirement, submission, current);
    }

    public Task<SubmissionRequirementResponse> CloseAsync(Guid projectId, Guid requirementId, CancellationToken cancellationToken) => ChangeStatusAsync(projectId, requirementId, SubmissionConstants.RequirementStatus.Closed, cancellationToken);
    public Task<SubmissionRequirementResponse> ReopenAsync(Guid projectId, Guid requirementId, CancellationToken cancellationToken) => ChangeStatusAsync(projectId, requirementId, SubmissionConstants.RequirementStatus.Open, cancellationToken);
    public Task<SubmissionRequirementResponse> ArchiveAsync(Guid projectId, Guid requirementId, CancellationToken cancellationToken) => ChangeStatusAsync(projectId, requirementId, SubmissionConstants.RequirementStatus.Archived, cancellationToken);

    public async Task DeleteAsync(Guid projectId, Guid requirementId, CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var requirement = await FindRequiredAsync(db, projectId, requirementId, cancellationToken);
        if (await db.ResearchSubmissions.AnyAsync(x => x.RequirementId == requirementId, cancellationToken))
            throw Conflict("This requirement has submission history and cannot be deleted. Archive it instead.");
        if (await db.SubmissionUploadSessions.AnyAsync(x => x.RequirementId == requirementId && x.ActiveSlot == "ACTIVE", cancellationToken))
            throw Conflict("This requirement has an active upload session. Complete or allow the session to expire before deleting it.");
        db.SubmissionRequirements.Remove(requirement);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<SubmissionRequirementResponse> ChangeStatusAsync(Guid projectId, Guid requirementId, string target, CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var requirement = await FindRequiredAsync(db, projectId, requirementId, cancellationToken);
        if (requirement.Status == SubmissionConstants.RequirementStatus.Archived) throw Conflict("Archived requirements cannot change state.");
        if (target == SubmissionConstants.RequirementStatus.Open && requirement.Status != SubmissionConstants.RequirementStatus.Closed)
            throw Conflict("Only a closed requirement can be reopened.");
        if (target == SubmissionConstants.RequirementStatus.Closed && requirement.Status != SubmissionConstants.RequirementStatus.Open)
            throw Conflict("Only an open requirement can be closed.");
        requirement.Status = target;
        requirement.UpdatedAt = _timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        var submission = await db.ResearchSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.RequirementId == requirementId, cancellationToken);
        SubmissionVersion? current = null;
        if (submission?.CurrentVersionId is Guid currentId) current = await db.SubmissionVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == currentId, cancellationToken);
        return ToResponse(requirement, submission, current);
    }

    private (string Title, string? Description, IReadOnlyList<string> AllowedTypes) Validate(string? titleRaw, string? descriptionRaw, IReadOnlyList<string>? allowedRaw, long maxSize)
    {
        var errors = new List<ApiFieldError>();
        var title = (titleRaw ?? string.Empty).Trim();
        if (title.Length == 0) errors.Add(new("title", ["Title is required."]));
        else if (title.Length > SubmissionConstants.RequirementTitleMaxLength) errors.Add(new("title", [$"Title must be at most {SubmissionConstants.RequirementTitleMaxLength} characters."]));
        var description = string.IsNullOrWhiteSpace(descriptionRaw) ? null : descriptionRaw.Trim();
        if (description?.Length > SubmissionConstants.RequirementDescriptionMaxLength) errors.Add(new("description", [$"Description must be at most {SubmissionConstants.RequirementDescriptionMaxLength} characters."]));
        var allowed = (allowedRaw ?? []).Select(SubmissionFileRules.NormalizeExtension).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var supported = _submissionOptions.AllowedFileTypes.Select(SubmissionFileRules.NormalizeExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowed.Length == 0) errors.Add(new("allowedFileTypes", ["At least one allowed file type is required."]));
        else if (allowed.Any(x => !supported.Contains(x))) errors.Add(new("allowedFileTypes", [$"Allowed file types must be a subset of: {string.Join(", ", supported.OrderBy(x => x))}."]));
        if (maxSize <= 0 || maxSize > _storageOptions.MaximumFileSizeBytes) errors.Add(new("maxFileSizeBytes", [$"Maximum file size must be between 1 and {_storageOptions.MaximumFileSizeBytes} bytes."]));
        if (errors.Count > 0) throw new ApiValidationException(errors);
        return (title, description, allowed);
    }


    private void ValidateNewDueAt(DateTimeOffset? dueAt)
    {
        if (dueAt is not null && dueAt <= _timeProvider.GetUtcNow())
            throw new ApiValidationException([new ApiFieldError("dueAt", ["Due date and time must be in the future."])]);
    }

    private void ValidateUpdatedDueAt(DateTimeOffset? existingDueAt, DateTimeOffset? requestedDueAt)
    {
        if (requestedDueAt is null) return;

        // Existing historical deadlines may already be in the past. Do not block an unrelated
        // edit when the deadline has not changed, but any newly selected deadline must be future.
        if (existingDueAt is not null &&
            Math.Abs((existingDueAt.Value - requestedDueAt.Value).TotalSeconds) < 60) return;

        if (requestedDueAt <= _timeProvider.GetUtcNow())
            throw new ApiValidationException([new ApiFieldError("dueAt", ["Due date and time must be in the future."])]);
    }

    private static async Task<SubmissionRequirement> FindRequiredAsync(SubmissionDbContext db, Guid projectId, Guid requirementId, CancellationToken cancellationToken) =>
        await db.SubmissionRequirements.SingleOrDefaultAsync(x => x.Id == requirementId && x.ProjectId == projectId, cancellationToken)
        ?? throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "Submission requirement not found.");

    private static SubmissionRequirementResponse ToResponse(SubmissionRequirement requirement, ResearchSubmission? submission, SubmissionVersion? current) =>
        new(requirement.Id, requirement.ProjectId, requirement.Title, requirement.Description, requirement.DueAt,
            SubmissionFileRules.ParseAllowedTypes(requirement.AllowedFileTypes), requirement.MaxFileSizeBytes, requirement.Status,
            requirement.CreatedBy, requirement.CreatedByName, requirement.CreatedAt, requirement.UpdatedAt,
            submission is null ? null : new SubmissionSummaryResponse(submission.Id, submission.Status, submission.VersionCount, current?.VersionNumber, submission.LastSubmittedAt));

    private static ApiException Conflict(string message) => new(StatusCodes.Status409Conflict, ErrorCodes.Conflict, message);
}
