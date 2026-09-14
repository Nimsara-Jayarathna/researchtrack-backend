using Microsoft.EntityFrameworkCore;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Infrastructure;

public sealed class GitHubRepositoryAccessRequestStore : IGitHubRepositoryAccessRequestStore
{
    private readonly IDbContextFactory<GitHubDbContext> _dbContextFactory;

    public GitHubRepositoryAccessRequestStore(IDbContextFactory<GitHubDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task CreateAsync(GitHubRepositoryAccessRequest request, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.RepositoryAccessRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<GitHubRepositoryAccessRequest?> FindByIdAsync(Guid requestId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.RepositoryAccessRequests.AsNoTracking()
            .SingleOrDefaultAsync(request => request.Id == requestId, cancellationToken);
    }

    public async Task<GitHubRepositoryAccessRequest?> FindByTokenHashAsync(
        string requestTokenHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestTokenHash))
        {
            return null;
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.RepositoryAccessRequests.AsNoTracking()
            .SingleOrDefaultAsync(
                request => request.RequestTokenHash == requestTokenHash,
                cancellationToken);
    }

    public async Task<OwnerGrantProjectLinkState> GetProjectLinkStateAsync(
        Guid projectId,
        string normalizedFullName,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var active = await db.ProjectRepositoryLinks.AsNoTracking()
            .Where(link => link.ProjectId == projectId && link.Active)
            .Select(link => new { link.FullName, link.Enabled })
            .ToListAsync(cancellationToken);

        return new OwnerGrantProjectLinkState(
            active.Count,
            active.Count(link => link.Enabled),
            active.Any(link => string.Equals(
                link.FullName,
                normalizedFullName,
                StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<GitHubRepositoryAccessRequest?> TryFailAsync(
        Guid requestId, string failureCode, DateTime now, CancellationToken cancellationToken)
    {
        GitHubRepositoryAccessFailureCodes.RequireSafe(failureCode);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var affected = await db.RepositoryAccessRequests
            .Where(request => request.Id == requestId
                && request.Status == GitHubRepositoryAccessRequestStatuses.Pending
                && request.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(request => request.Status, GitHubRepositoryAccessRequestStatuses.Failed)
                .SetProperty(request => request.FailureCode, failureCode)
                .SetProperty(request => request.ConsumedAt, now)
                .SetProperty(request => request.Version, request => request.Version + 1), cancellationToken);
        if (affected == 0) return null;
        return await db.RepositoryAccessRequests.AsNoTracking().SingleAsync(request => request.Id == requestId, cancellationToken);
    }

    public async Task<GitHubRepositoryAccessRequest?> TryExpireAsync(
        Guid requestId, DateTime now, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var affected = await db.RepositoryAccessRequests
            .Where(request => request.Id == requestId
                && request.Status == GitHubRepositoryAccessRequestStatuses.Pending
                && request.ExpiresAt <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(request => request.Status, GitHubRepositoryAccessRequestStatuses.Expired)
                .SetProperty(request => request.ConsumedAt, now)
                .SetProperty(request => request.Version, request => request.Version + 1), cancellationToken);
        if (affected == 0) return null;
        return await db.RepositoryAccessRequests.AsNoTracking().SingleAsync(request => request.Id == requestId, cancellationToken);
    }
}
