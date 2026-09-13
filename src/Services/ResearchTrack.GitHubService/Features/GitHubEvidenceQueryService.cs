using ResearchTrack.BuildingBlocks.Api.Constants;
using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Features;

public sealed class GitHubEvidenceQueryService : IGitHubEvidenceQueryService
{
    private readonly IDbContextFactory<GitHubDbContext> _dbContextFactory;
    private readonly IProjectAuthorizationClient _projectAuthorization;

    public GitHubEvidenceQueryService(
        IDbContextFactory<GitHubDbContext> dbContextFactory,
        IProjectAuthorizationClient projectAuthorization)
    {
        _dbContextFactory = dbContextFactory;
        _projectAuthorization = projectAuthorization;
    }

    public async Task<GitHubEvidencePage<GitHubCommitResponse>> GetCommitsAsync(
        Guid userId,
        Guid projectId,
        Guid linkedRepositoryId,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        await EnsureRepositoryAsync(projectId, linkedRepositoryId, page, size, cancellationToken);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.Commits
            .AsNoTracking()
            .Where(item => item.RepositoryLinkId == linkedRepositoryId)
            .OrderByDescending(item => item.CommittedAt);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * size)
            .Take(size)
            .Select(item => new GitHubCommitResponse(
                item.Sha,
                item.Message,
                item.AuthorGitHubId,
                item.AuthorLogin,
                item.AuthorName,
                item.AuthoredAt,
                item.CommittedAt,
                item.HtmlUrl,
                item.Additions,
                item.Deletions,
                item.ChangedFiles))
            .ToListAsync(cancellationToken);
        return Page(items, page, size, total);
    }

    public async Task<GitHubEvidencePage<GitHubContributorResponse>> GetContributorsAsync(
        Guid userId,
        Guid projectId,
        Guid linkedRepositoryId,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        await EnsureRepositoryAsync(projectId, linkedRepositoryId, page, size, cancellationToken);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.Contributors
            .AsNoTracking()
            .Where(item => item.RepositoryLinkId == linkedRepositoryId)
            .OrderByDescending(item => item.ObservedCommitCount)
            .ThenBy(item => item.Login);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * size)
            .Take(size)
            .Select(item => new GitHubContributorResponse(
                item.GitHubUserId,
                item.Login,
                item.AvatarUrl,
                item.ProfileUrl,
                item.GitHubContributionCount,
                item.ObservedCommitCount,
                item.ObservedAdditions,
                item.ObservedDeletions,
                item.ObservedChangedFiles,
                item.FirstCommitAt,
                item.LastCommitAt,
                item.LastSyncedAt))
            .ToListAsync(cancellationToken);
        return Page(items, page, size, total);
    }

    public async Task<GitHubEvidencePage<GitHubPullRequestResponse>> GetPullRequestsAsync(
        Guid userId,
        Guid projectId,
        Guid linkedRepositoryId,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        await EnsureRepositoryAsync(projectId, linkedRepositoryId, page, size, cancellationToken);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.PullRequests
            .AsNoTracking()
            .Where(item => item.RepositoryLinkId == linkedRepositoryId)
            .OrderByDescending(item => item.UpdatedAt);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * size)
            .Take(size)
            .Select(item => new GitHubPullRequestResponse(
                item.GitHubPullRequestId,
                item.Number,
                item.Title,
                item.Body,
                item.State,
                item.IsDraft,
                item.IsMerged,
                item.AuthorLogin,
                item.SourceBranch,
                item.TargetBranch,
                item.CreatedAt,
                item.UpdatedAt,
                item.ClosedAt,
                item.MergedAt,
                item.HtmlUrl,
                item.Additions,
                item.Deletions,
                item.ChangedFiles,
                item.CommitsCount,
                item.CommentsCount,
                item.ReviewCommentsCount))
            .ToListAsync(cancellationToken);
        return Page(items, page, size, total);
    }

    public async Task<GitHubEvidencePage<GitHubSyncRunResponse>> GetSyncRunsAsync(
        Guid userId,
        Guid projectId,
        Guid linkedRepositoryId,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        await EnsureRepositoryAsync(projectId, linkedRepositoryId, page, size, cancellationToken);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.SyncRuns
            .AsNoTracking()
            .Where(item => item.RepositoryLinkId == linkedRepositoryId)
            .OrderByDescending(item => item.StartedAt);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * size)
            .Take(size)
            .Select(item => new GitHubSyncRunResponse(
                item.Id,
                item.Trigger,
                item.Status,
                item.StartedAt,
                item.CompletedAt,
                item.CommitsFetched,
                item.ContributorsFetched,
                item.PullRequestsFetched,
                item.ReviewsFetched,
                item.BranchesFetched,
                item.ErrorCode,
                item.ErrorMessage))
            .ToListAsync(cancellationToken);
        return Page(items, page, size, total);
    }

    private async Task EnsureRepositoryAsync(
        Guid projectId,
        Guid linkedRepositoryId,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty
            || linkedRepositoryId == Guid.Empty
            || page < 1
            || size is < 1 or > 100)
        {
            throw new ApiValidationException([
                new ApiFieldError("query", ["Valid project, repository, page, and size values are required."])
            ]);
        }

        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var exists = await dbContext.ProjectRepositoryLinks
            .AsNoTracking()
            .AnyAsync(
                item => item.Id == linkedRepositoryId
                    && item.ProjectId == projectId
                    && item.Active,
                cancellationToken);
        if (!exists)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The linked GitHub repository was not found for this project.");
        }
    }

    private static GitHubEvidencePage<T> Page<T>(IReadOnlyList<T> items, int page, int size, int total) =>
        new(items, page, size, total, page * size < total);
}
