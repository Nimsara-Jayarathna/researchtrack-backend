using Microsoft.EntityFrameworkCore;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Features;

public sealed class GitHubSyncStateQueryService : IGitHubSyncStateQueryService
{
    private readonly GitHubDbContext _dbContext;
    private readonly IProjectAuthorizationClient _authorization;

    public GitHubSyncStateQueryService(GitHubDbContext dbContext, IProjectAuthorizationClient authorization)
    {
        _dbContext = dbContext;
        _authorization = authorization;
    }

    public async Task<GitHubSyncStateResponse> GetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await _authorization.EnsureCanViewAsync(projectId, cancellationToken);
        var repositories = await _dbContext.ProjectRepositoryLinks
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.Active)
            .OrderByDescending(x => x.Primary)
            .ThenBy(x => x.LinkedAt)
            .Select(x => new GitHubRepositorySyncStateResponse(
                x.Id, x.SyncRevision, x.SyncStatus, x.LastSyncedAt, x.Enabled, x.Primary))
            .ToListAsync(cancellationToken);
        return new GitHubSyncStateResponse(projectId, repositories);
    }
}
