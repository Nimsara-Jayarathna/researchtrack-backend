using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Features.Installation;

public sealed class GitHubAccessRequestService : IGitHubAccessRequestService
{
    private readonly IDbContextFactory<GitHubDbContext> _dbContextFactory;
    private readonly IProjectAuthorizationClient _projectAuthorization;
    private readonly IProjectMetadataClient _projectMetadata;
    private readonly IGitHubAccessRequestTokenService _tokens;
    private readonly GitHubAppOptions _options;
    private readonly TimeProvider _timeProvider;

    public GitHubAccessRequestService(
        IDbContextFactory<GitHubDbContext> dbContextFactory,
        IProjectAuthorizationClient projectAuthorization,
        IProjectMetadataClient projectMetadata,
        IGitHubAccessRequestTokenService tokens,
        GitHubAppOptions options,
        TimeProvider timeProvider)
    {
        _dbContextFactory = dbContextFactory;
        _projectAuthorization = projectAuthorization;
        _projectMetadata = projectMetadata;
        _tokens = tokens;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<GitHubAccessRequestCreateResponse> CreateAsync(
        Guid userId,
        Guid projectId,
        string ownerLogin,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty)
        {
            throw Validation("projectId", "Project id is required.");
        }
        if (userId == Guid.Empty)
        {
            throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "Authentication is required.");
        }

        var normalizedOwnerLogin = NormalizeOwnerLogin(ownerLogin);
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        var title = await _projectMetadata.GetTitleAsync(projectId, cancellationToken);
        var now = UtcNow();

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var pendingRequests = await db.AccessRequests
            .Where(request => request.ProjectId == projectId
                && request.Status == GitHubAccessRequestStatuses.Pending)
            .OrderByDescending(request => request.CreatedAt)
            .ToListAsync(cancellationToken);
        foreach (var pending in pendingRequests)
        {
            NormalizeExpiry(pending, now);
        }
        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        if (pendingRequests.Any(request => request.Status == GitHubAccessRequestStatuses.Pending))
        {
            throw Conflict("A pending GitHub access request already exists for this project. Revoke it before creating another request.");
        }

        var id = Guid.NewGuid();
        var token = _tokens.CreateRequestToken(id);
        var expiresAt = now.Add(_options.AccessRequestLifetime);
        var entity = new GitHubAccessRequest
        {
            Id = id,
            ProjectId = projectId,
            ProjectTitle = title,
            TargetOwnerLogin = normalizedOwnerLogin,
            RequestedByUserId = userId,
            TokenNonce = token.Nonce,
            TokenHash = token.Hash,
            Status = GitHubAccessRequestStatuses.Pending,
            PendingProjectKey = projectId.ToString("N"),
            CreatedAt = now,
            ExpiresAt = expiresAt
        };
        db.AccessRequests.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            throw Conflict("A pending GitHub access request already exists for this project.", exception);
        }

        return new GitHubAccessRequestCreateResponse(
            id,
            projectId,
            normalizedOwnerLogin,
            BuildRequestUrl(token.Token),
            entity.Status,
            expiresAt);
    }

    public async Task<IReadOnlyList<GitHubAccessRequestSummaryResponse>> ListAsync(
        Guid userId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        var now = UtcNow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var requests = await db.AccessRequests
            .Where(request => request.ProjectId == projectId)
            .OrderByDescending(request => request.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);
        await ExpireAsync(db, requests, now, cancellationToken);
        return requests.Select(ToSummary).ToList();
    }

    public async Task RevokeAsync(
        Guid userId,
        Guid projectId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var request = await db.AccessRequests.SingleOrDefaultAsync(
            item => item.Id == requestId && item.ProjectId == projectId,
            cancellationToken) ?? throw NotFound("The GitHub access request was not found.");
        NormalizeExpiry(request, UtcNow());
        if (!string.Equals(request.Status, GitHubAccessRequestStatuses.Pending, StringComparison.Ordinal))
        {
            throw Conflict("Only a pending GitHub access request can be revoked.");
        }
        request.Status = GitHubAccessRequestStatuses.Revoked;
        request.PendingProjectKey = null;
        request.RevokedAt = UtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<GitHubAccessRequestValidationResponse> ValidateAsync(
        string token,
        CancellationToken cancellationToken)
    {
        var request = await FindByRequestTokenAsync(token, cancellationToken);
        return new GitHubAccessRequestValidationResponse(
            request.ProjectId,
            request.ProjectTitle,
            request.TargetOwnerLogin,
            request.Status,
            AsUtc(request.ExpiresAt));
    }

    public async Task<PendingGitHubAccessRequest> ResolvePendingAsync(
        string token,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var request = await FindByRequestTokenAsync(db, token, cancellationToken);
        NormalizeExpiry(request, UtcNow());
        if (!string.Equals(request.Status, GitHubAccessRequestStatuses.Pending, StringComparison.Ordinal))
        {
            await db.SaveChangesAsync(cancellationToken);
            throw Conflict(request.Status switch
            {
                GitHubAccessRequestStatuses.Completed => "This GitHub access request has already been completed.",
                GitHubAccessRequestStatuses.Revoked => "This GitHub access request was revoked.",
                GitHubAccessRequestStatuses.Expired => "This GitHub access request has expired.",
                _ => "This GitHub access request cannot be continued."
            });
        }

        request.AuthorizationStartedAt = UtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return new PendingGitHubAccessRequest(
            request.Id,
            request.ProjectId,
            request.RequestedByUserId,
            request.ProjectTitle,
            request.TargetOwnerLogin,
            AsUtc(request.ExpiresAt));
    }

    public async Task EnsureInstallationOwnerAsync(
        Guid requestId,
        string ownerLogin,
        CancellationToken cancellationToken)
    {
        var normalizedOwner = NormalizeOwnerLogin(ownerLogin);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var request = await db.AccessRequests.SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken)
            ?? throw NotFound("The GitHub access request was not found.");
        NormalizeExpiry(request, UtcNow());
        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        if (!string.Equals(request.Status, GitHubAccessRequestStatuses.Pending, StringComparison.Ordinal))
        {
            throw Conflict("This GitHub access request is no longer pending.");
        }
        if (!string.Equals(request.TargetOwnerLogin, normalizedOwner, StringComparison.OrdinalIgnoreCase))
        {
            throw Conflict($"GitHub App authorization must be completed for '{request.TargetOwnerLogin}'.");
        }
    }

    public Task<string?> CompleteAsync(
        Guid requestId,
        Guid sourceId,
        long installationId,
        CancellationToken cancellationToken) =>
        FinishAsync(requestId, GitHubAccessRequestStatuses.Completed, sourceId, installationId, null, cancellationToken);

    public Task<string?> FailAsync(
        Guid requestId,
        string errorCode,
        CancellationToken cancellationToken) =>
        FinishAsync(requestId, GitHubAccessRequestStatuses.Failed, null, null, errorCode, cancellationToken);

    public async Task<GitHubAccessUpdatedSummaryResponse> GetResultAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw Validation("token", "Result token is required.");
        }
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var hash = _tokens.Hash(token.Trim());
        var request = await db.AccessRequests.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ResultTokenHash == hash, cancellationToken)
            ?? throw NotFound("The GitHub access result is invalid or has expired.");
        if (request.ResultNonce is null || !_tokens.IsWellFormed(token.Trim(), request.Id, request.ResultNonce, true))
        {
            throw NotFound("The GitHub access result is invalid or has expired.");
        }
        if (!string.Equals(request.Status, GitHubAccessRequestStatuses.Completed, StringComparison.Ordinal)
            || request.SourceId is null
            || request.InstallationId is null)
        {
            throw Conflict("GitHub access was not completed successfully for this request.");
        }
        return await BuildSummaryAsync(db, request, cancellationToken);
    }

    public async Task<GitHubAccessUpdatedAcknowledgeResponse> AcknowledgeAsync(
        string token,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw Validation("token", "Result token is required.");
        }
        var normalized = token.Trim();
        var hash = _tokens.Hash(normalized);
        var request = await db.AccessRequests.SingleOrDefaultAsync(
            item => item.ResultTokenHash == hash,
            cancellationToken) ?? throw NotFound("The GitHub access result was not found.");
        if (request.ResultNonce is null || !_tokens.IsWellFormed(normalized, request.Id, request.ResultNonce, true))
        {
            throw NotFound("The GitHub access result was not found.");
        }
        if (!string.Equals(request.Status, GitHubAccessRequestStatuses.Completed, StringComparison.Ordinal))
        {
            throw Conflict("Only a completed GitHub access result can be acknowledged.");
        }
        // This endpoint is used by the external repository owner. Consuming the
        // one-time result token must not acknowledge the supervisor's pending
        // "access granted" notification. Supervisor acknowledgement is tracked
        // separately by AcknowledgeLatestAsync.
        request.ResultNonce = null;
        request.ResultTokenHash = null;
        await db.SaveChangesAsync(cancellationToken);
        return new GitHubAccessUpdatedAcknowledgeResponse(request.ProjectId);
    }

    public async Task<GitHubAccessUpdatedSummaryResponse> GetLatestCompletedAsync(
        Guid userId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var request = await db.AccessRequests.AsNoTracking()
            .Where(item => item.ProjectId == projectId
                && item.Status == GitHubAccessRequestStatuses.Completed
                && item.SourceId != null
                && item.InstallationId != null
                && item.AcknowledgedAt == null
                && db.AccessSources.Any(source => source.Id == item.SourceId && source.Active))
            .OrderByDescending(item => item.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFound("No newly completed GitHub access request was found for this project.");
        return await BuildSummaryAsync(db, request, cancellationToken);
    }

    public async Task<GitHubAccessUpdatedAcknowledgeResponse> AcknowledgeLatestAsync(
        Guid userId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var request = await db.AccessRequests
            .Where(item => item.ProjectId == projectId
                && item.Status == GitHubAccessRequestStatuses.Completed
                && item.SourceId != null
                && item.AcknowledgedAt == null
                && db.AccessSources.Any(source => source.Id == item.SourceId && source.Active))
            .OrderByDescending(item => item.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFound("No completed GitHub access request is awaiting acknowledgement.");
        request.AcknowledgedAt = UtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return new GitHubAccessUpdatedAcknowledgeResponse(projectId);
    }

    private async Task<string?> FinishAsync(
        Guid requestId,
        string status,
        Guid? sourceId,
        long? installationId,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var request = await db.AccessRequests.SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken);
        if (request is null)
        {
            return null;
        }
        if (!string.Equals(request.Status, GitHubAccessRequestStatuses.Pending, StringComparison.Ordinal))
        {
            return request.ResultNonce is null ? null : RecreateResultToken(request);
        }

        var result = _tokens.CreateResultToken(request.Id);
        request.ResultNonce = result.Nonce;
        request.ResultTokenHash = result.Hash;
        request.Status = status;
        request.PendingProjectKey = null;
        request.SourceId = sourceId;
        request.InstallationId = installationId;
        request.ErrorCode = errorCode;
        request.CompletedAt = UtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return result.Token;
    }

    private async Task<GitHubAccessRequest> FindByRequestTokenAsync(
        string token,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var request = await FindByRequestTokenAsync(db, token, cancellationToken);
        NormalizeExpiry(request, UtcNow());
        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        return request;
    }

    private async Task<GitHubAccessRequest> FindByRequestTokenAsync(
        GitHubDbContext db,
        string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw Validation("token", "Access request token is required.");
        }
        var normalized = token.Trim();
        var hash = _tokens.Hash(normalized);
        var request = await db.AccessRequests.SingleOrDefaultAsync(
            item => item.TokenHash == hash,
            cancellationToken) ?? throw NotFound("The GitHub access request is invalid or has expired.");
        if (!_tokens.IsWellFormed(normalized, request.Id, request.TokenNonce, false))
        {
            throw NotFound("The GitHub access request is invalid or has expired.");
        }
        return request;
    }

    private GitHubAccessRequestSummaryResponse ToSummary(GitHubAccessRequest request)
    {
        var requestUrl = string.Equals(request.Status, GitHubAccessRequestStatuses.Pending, StringComparison.Ordinal)
            ? BuildRequestUrl(_tokens.RecreateRequestToken(request.Id, request.TokenNonce))
            : null;
        return new GitHubAccessRequestSummaryResponse(
            request.Id,
            request.ProjectId,
            request.ProjectTitle,
            request.TargetOwnerLogin,
            request.Status,
            requestUrl,
            AsUtc(request.CreatedAt),
            AsUtc(request.ExpiresAt),
            request.CompletedAt is null ? null : AsUtc(request.CompletedAt.Value),
            request.RevokedAt is null ? null : AsUtc(request.RevokedAt.Value),
            request.SourceId,
            request.InstallationId,
            request.ErrorCode);
    }

    private async Task<GitHubAccessUpdatedSummaryResponse> BuildSummaryAsync(
        GitHubDbContext db,
        GitHubAccessRequest request,
        CancellationToken cancellationToken)
    {
        var sourceId = request.SourceId!.Value;
        var repositories = await db.Repositories.AsNoTracking()
            .Where(repository => repository.SourceId == sourceId)
            .OrderBy(repository => repository.FullName)
            .Select(repository => new GitHubInstallationRepositoryResponse(
                repository.GitHubRepositoryId,
                repository.Name,
                repository.FullName,
                repository.Url,
                repository.OwnerLogin,
                repository.DefaultBranch))
            .ToListAsync(cancellationToken);
        var scope = repositories.Count switch
        {
            0 => "NO_REPOSITORIES",
            1 => "SINGLE_REPOSITORY",
            _ => "MULTIPLE_REPOSITORIES"
        };
        return new GitHubAccessUpdatedSummaryResponse(
            request.ProjectId,
            request.ProjectTitle,
            request.InstallationId!.Value,
            request.SourceId,
            GitHubInstallationFlowTypes.Requested,
            scope,
            repositories.Count,
            repositories);
    }

    private async Task ExpireAsync(
        GitHubDbContext db,
        IReadOnlyList<GitHubAccessRequest> requests,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (var request in requests)
        {
            NormalizeExpiry(request, now);
        }
        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static void NormalizeExpiry(GitHubAccessRequest request, DateTime now)
    {
        if (string.Equals(request.Status, GitHubAccessRequestStatuses.Pending, StringComparison.Ordinal)
            && request.ExpiresAt <= now)
        {
            request.Status = GitHubAccessRequestStatuses.Expired;
            request.PendingProjectKey = null;
        }
    }

    private string RecreateResultToken(GitHubAccessRequest request)
    {
        if (request.ResultNonce is null)
        {
            throw new InvalidOperationException("Result token nonce is missing.");
        }
        return _tokens.RecreateResultToken(request.Id, request.ResultNonce);
    }

    private string BuildRequestUrl(string token)
    {
        var builder = new UriBuilder(new Uri(_options.FrontendReturnOrigin, "github/request-access"));
        builder.Query = $"token={Uri.EscapeDataString(token)}";
        return builder.Uri.AbsoluteUri;
    }

    private static string NormalizeOwnerLogin(string ownerLogin)
    {
        var normalized = ownerLogin?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 100
            || normalized.StartsWith("-", StringComparison.Ordinal)
            || normalized.EndsWith("-", StringComparison.Ordinal)
            || normalized.Contains("--", StringComparison.Ordinal)
            || normalized.Any(character => !char.IsLetterOrDigit(character) && character != '-'))
        {
            throw Validation("ownerLogin", "Enter a valid GitHub user or organization login.");
        }
        return normalized;
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static ApiException NotFound(string message) => new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, message);
    private static ApiException Conflict(string message, Exception? inner = null) => new(
        StatusCodes.Status409Conflict,
        ErrorCodes.Conflict,
        message,
        innerException: inner);

    private static bool IsDuplicateKey(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
    private static ApiValidationException Validation(string field, string message) => new([new ResearchTrack.BuildingBlocks.Api.Contracts.ApiFieldError(field, [message])]);
}
