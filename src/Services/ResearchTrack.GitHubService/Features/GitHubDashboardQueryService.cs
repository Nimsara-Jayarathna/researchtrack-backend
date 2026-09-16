using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Features;

public sealed class GitHubDashboardQueryService : IGitHubDashboardQueryService
{
    private readonly IDbContextFactory<GitHubDbContext> _dbContextFactory;
    private readonly IProjectAuthorizationClient _projectAuthorization;

    public GitHubDashboardQueryService(
        IDbContextFactory<GitHubDbContext> dbContextFactory,
        IProjectAuthorizationClient projectAuthorization)
    {
        _dbContextFactory = dbContextFactory;
        _projectAuthorization = projectAuthorization;
    }

    public async Task<GitHubDashboardResponse> GetDashboardAsync(
        Guid userId,
        Guid projectId,
        Guid? linkedRepositoryId,
        CancellationToken cancellationToken)
    {
        await AuthorizeAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var links = await LoadAccessibleEnabledLinksAsync(
            db,
            projectId,
            cancellationToken);

        var hasUnacknowledgedAccess = await db.AccessRequests.AsNoTracking()
            .AnyAsync(request => request.ProjectId == projectId
                && request.Status == GitHubAccessRequestStatuses.Completed
                && request.SourceId != null
                && request.InstallationId != null
                && request.AcknowledgedAt == null
                && db.AccessSources.Any(source => source.Id == request.SourceId && source.Active),
                cancellationToken);

        var selected = SelectLink(links, linkedRepositoryId);
        var repositories = links.Select(link => new GitHubDashboardRepositoryResponse(
            link.Id,
            string.IsNullOrWhiteSpace(link.CustomName) ? link.FullName : link.CustomName!,
            link.Url,
            link.DefaultBranch,
            link.LastSyncedAt,
            link.LinkedAt,
            link.UpdatedAt)).ToList();

        if (selected is null)
        {
            return new GitHubDashboardResponse(
                false,
                null,
                0,
                "NOT_AUTHORIZED",
                repositories,
                null,
                new GitHubDashboardActivitySummaryResponse(
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    null,
                    null,
                    null,
                    null,
                    "idle"),
                [],
                [],
                hasUnacknowledgedAccess);
        }

        var source = await db.AccessSources.AsNoTracking()
            .SingleOrDefaultAsync(source => source.Id == selected.SourceId, cancellationToken);

        var totalCommits = await db.Commits.AsNoTracking()
            .CountAsync(commit => commit.RepositoryLinkId == selected.Id, cancellationToken);
        var latestCommitAt = await db.Commits.AsNoTracking()
            .Where(commit => commit.RepositoryLinkId == selected.Id)
            .MaxAsync(commit => (DateTime?)commit.CommittedAt, cancellationToken);

        var pullRequestSummary = await db.PullRequests.AsNoTracking()
            .Where(pullRequest => pullRequest.RepositoryLinkId == selected.Id)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                Open = group.Count(pullRequest =>
                    !pullRequest.IsMerged
                    && !pullRequest.IsDraft
                    && pullRequest.State == "OPEN"),
                Draft = group.Count(pullRequest =>
                    !pullRequest.IsMerged
                    && pullRequest.IsDraft
                    && pullRequest.State == "OPEN"),
                Merged = group.Count(pullRequest => pullRequest.IsMerged),
                Closed = group.Count(pullRequest =>
                    !pullRequest.IsMerged
                    && pullRequest.State == "CLOSED")
            })
            .SingleOrDefaultAsync(cancellationToken);

        var latestPullRequest = await db.PullRequests.AsNoTracking()
            .Where(pullRequest => pullRequest.RepositoryLinkId == selected.Id)
            .OrderByDescending(pullRequest => pullRequest.UpdatedAt)
            .ThenByDescending(pullRequest => pullRequest.Number)
            .Select(pullRequest => new
            {
                pullRequest.Number,
                pullRequest.UpdatedAt,
                pullRequest.IsMerged,
                pullRequest.IsDraft,
                pullRequest.State
            })
            .FirstOrDefaultAsync(cancellationToken);

        var lastActivityIsPullRequest =
            latestPullRequest is not null
            && (latestCommitAt is null || latestPullRequest.UpdatedAt >= latestCommitAt.Value);
        var lastActivityAt = lastActivityIsPullRequest
            ? latestPullRequest!.UpdatedAt
            : latestCommitAt;
        var lastActivityPullRequestStatus = lastActivityIsPullRequest
            ? NormalizePullRequestStatus(
                latestPullRequest!.State,
                latestPullRequest.IsDraft,
                latestPullRequest.IsMerged)
            : null;

        var contributorPreview = await db.Contributors.AsNoTracking()
            .Where(contributor => contributor.RepositoryLinkId == selected.Id)
            .OrderByDescending(contributor => contributor.ObservedCommitCount)
            .ThenByDescending(contributor => contributor.GitHubContributionCount)
            .Take(5)
            .Select(contributor => new GitHubDashboardContributorResponse(
                contributor.Login,
                contributor.ObservedCommitCount,
                contributor.Login,
                contributor.AvatarUrl))
            .ToListAsync(cancellationToken);

        var avatarByLogin = contributorPreview
            .Where(item => !string.IsNullOrWhiteSpace(item.GitHubUsername))
            .ToDictionary(item => item.GitHubUsername!, item => item.AvatarUrl, StringComparer.OrdinalIgnoreCase);

        var commitRows = await db.Commits.AsNoTracking()
            .Where(commit => commit.RepositoryLinkId == selected.Id)
            .OrderByDescending(commit => commit.CommittedAt)
            .ThenByDescending(commit => commit.FirstSeenAt)
            .Take(5)
            .ToListAsync(cancellationToken);
        var recentCommits = commitRows.Select(commit => ToDashboardCommit(commit, avatarByLogin)).ToList();

        var accessScope = links.Count switch
        {
            0 => "NO_REPOSITORIES",
            1 => "SINGLE_REPOSITORY",
            _ => "MULTIPLE_REPOSITORIES"
        };

        return new GitHubDashboardResponse(
            true,
            source?.InstallationId,
            links.Count,
            accessScope,
            repositories,
            links.FirstOrDefault(link => link.Primary)?.Url ?? selected.Url,
            new GitHubDashboardActivitySummaryResponse(
                totalCommits,
                pullRequestSummary?.Total ?? 0,
                pullRequestSummary?.Open ?? 0,
                pullRequestSummary?.Draft ?? 0,
                pullRequestSummary?.Merged ?? 0,
                pullRequestSummary?.Closed ?? 0,
                lastActivityAt,
                lastActivityIsPullRequest ? "pull_request" : latestCommitAt is not null ? "commit" : null,
                lastActivityIsPullRequest ? latestPullRequest!.Number : null,
                lastActivityPullRequestStatus,
                totalCommits > 0 || (pullRequestSummary?.Total ?? 0) > 0 ? "active" : "idle"),
            contributorPreview,
            recentCommits,
            hasUnacknowledgedAccess);
    }

    public async Task<GitHubCompatibilityPage<GitHubDashboardCommitResponse>> GetActivityAsync(
        Guid userId,
        Guid projectId,
        Guid? linkedRepositoryId,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        ValidatePage(page, size);
        await AuthorizeAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var selected = await LoadSelectedAsync(db, projectId, linkedRepositoryId, cancellationToken);
        if (selected is null)
        {
            return new GitHubCompatibilityPage<GitHubDashboardCommitResponse>([], false, page, size, 0);
        }

        var total = await db.Commits.AsNoTracking()
            .CountAsync(commit => commit.RepositoryLinkId == selected.Id, cancellationToken);
        var rows = await db.Commits.AsNoTracking()
            .Where(commit => commit.RepositoryLinkId == selected.Id)
            .OrderByDescending(commit => commit.CommittedAt)
            .ThenByDescending(commit => commit.FirstSeenAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
        var logins = rows.Where(item => item.AuthorLogin != null).Select(item => item.AuthorLogin!).Distinct().ToList();
        var avatarRows = await db.Contributors.AsNoTracking()
            .Where(item => item.RepositoryLinkId == selected.Id && logins.Contains(item.Login))
            .Select(item => new { item.Login, item.AvatarUrl })
            .ToListAsync(cancellationToken);
        var avatars = avatarRows.ToDictionary(
            item => item.Login,
            item => item.AvatarUrl,
            StringComparer.OrdinalIgnoreCase);

        return new GitHubCompatibilityPage<GitHubDashboardCommitResponse>(
            rows.Select(row => ToDashboardCommit(row, avatars)).ToList(),
            page * size < total,
            page,
            size,
            total);
    }

    public async Task<GitHubCompatibilityPage<GitHubDashboardContributorResponse>> GetContributorsAsync(
        Guid userId,
        Guid projectId,
        Guid? linkedRepositoryId,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        ValidatePage(page, size);
        await AuthorizeAsync(projectId, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var selected = await LoadSelectedAsync(db, projectId, linkedRepositoryId, cancellationToken);
        if (selected is null)
        {
            return new GitHubCompatibilityPage<GitHubDashboardContributorResponse>([], false, page, size, 0);
        }

        var total = await db.Contributors.AsNoTracking()
            .CountAsync(item => item.RepositoryLinkId == selected.Id, cancellationToken);
        var items = await db.Contributors.AsNoTracking()
            .Where(item => item.RepositoryLinkId == selected.Id)
            .OrderByDescending(item => item.ObservedCommitCount)
            .ThenByDescending(item => item.GitHubContributionCount)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(item => new GitHubDashboardContributorResponse(
                item.Login,
                item.ObservedCommitCount,
                item.Login,
                item.AvatarUrl))
            .ToListAsync(cancellationToken);

        return new GitHubCompatibilityPage<GitHubDashboardContributorResponse>(
            items,
            page * size < total,
            page,
            size,
            total);
    }

    private Task AuthorizeAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty)
        {
            throw new ApiValidationException([new ApiFieldError("projectId", ["Project id is required."])]);
        }
        return _projectAuthorization.EnsureCanViewAsync(projectId, cancellationToken);
    }

    private static Task<List<ProjectRepositoryLink>> LoadAccessibleEnabledLinksAsync(
        GitHubDbContext db,
        Guid projectId,
        CancellationToken cancellationToken) =>
        (from link in db.ProjectRepositoryLinks.AsNoTracking()
         join source in db.AccessSources.AsNoTracking() on link.SourceId equals source.Id
         join repository in db.Repositories.AsNoTracking() on link.GitHubRepositoryId equals repository.Id
         where link.ProjectId == projectId
             && link.Active
             && link.Enabled
             && source.Active
             && source.ConnectionStatus == GitHubConnectionStatuses.Connected
             && repository.Available
         orderby link.Primary descending, link.LinkedAt
         select link).ToListAsync(cancellationToken);

    private static ProjectRepositoryLink? SelectLink(
        IReadOnlyList<ProjectRepositoryLink> links,
        Guid? linkedRepositoryId)
    {
        if (linkedRepositoryId.HasValue)
        {
            return links.FirstOrDefault(link => link.Id == linkedRepositoryId.Value)
                ?? throw new ApiException(
                    StatusCodes.Status404NotFound,
                    ResearchTrack.BuildingBlocks.Api.Constants.ErrorCodes.NotFound,
                    "The linked GitHub repository was not found for this project.");
        }
        return links.FirstOrDefault(link => link.Primary) ?? links.FirstOrDefault();
    }

    private static async Task<ProjectRepositoryLink?> LoadSelectedAsync(
        GitHubDbContext db,
        Guid projectId,
        Guid? linkedRepositoryId,
        CancellationToken cancellationToken)
    {
        var links = await LoadAccessibleEnabledLinksAsync(
            db,
            projectId,
            cancellationToken);
        return SelectLink(links, linkedRepositoryId);
    }

    private static string NormalizePullRequestStatus(
        string state,
        bool isDraft,
        bool isMerged)
    {
        if (isMerged)
        {
            return "MERGED";
        }

        if (string.Equals(state, "CLOSED", StringComparison.OrdinalIgnoreCase))
        {
            return "CLOSED";
        }

        if (isDraft)
        {
            return "DRAFT";
        }

        return "OPEN";
    }

    private static GitHubDashboardCommitResponse ToDashboardCommit(
        GitHubCommit commit,
        IReadOnlyDictionary<string, string?> avatars)
    {
        var author = commit.AuthorName ?? commit.AuthorLogin ?? "Unknown contributor";
        var avatar = commit.AuthorLogin is not null && avatars.TryGetValue(commit.AuthorLogin, out var value)
            ? value
            : null;
        return new GitHubDashboardCommitResponse(
            commit.Sha,
            commit.Message,
            author,
            commit.AuthorLogin,
            avatar,
            commit.CommittedAt,
            "commit");
    }

    private static void ValidatePage(int page, int size)
    {
        if (page < 1 || size is < 1 or > 100)
        {
            throw new ApiValidationException([
                new ApiFieldError("page", ["Page must be at least 1."]),
                new ApiFieldError("size", ["Size must be between 1 and 100."])
            ]);
        }
    }
}
