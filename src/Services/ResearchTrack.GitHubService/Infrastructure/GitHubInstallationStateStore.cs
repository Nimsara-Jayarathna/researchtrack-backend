using Microsoft.EntityFrameworkCore;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Installation;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Infrastructure;

public sealed class GitHubInstallationStateStore : IGitHubInstallationStateStore
{
    private readonly IDbContextFactory<GitHubDbContext> _dbContextFactory;

    public GitHubInstallationStateStore(IDbContextFactory<GitHubDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task CreateAsync(
        GitHubInstallationFlowState state,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.InstallationFlowStates.Add(state);
        await dbContext.SaveChangesAsync(cancellationToken);
    }


    public async Task<GitHubInstallationFlowState?> TryBindInstallationAsync(
        string stateHash,
        long installationId,
        string expectedFlowType,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var affected = await dbContext.InstallationFlowStates
            .Where(state => state.StateHash == stateHash
                && state.FlowType == expectedFlowType
                && state.ConsumedAt == null
                && state.ExpiresAt > now
                && state.PendingInstallationId == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(state => state.PendingInstallationId, installationId)
                    .SetProperty(state => state.AuthorizationStartedAt, now),
                cancellationToken);

        if (affected == 0)
        {
            var existing = await dbContext.InstallationFlowStates
                .AsNoTracking()
                .SingleOrDefaultAsync(state => state.StateHash == stateHash, cancellationToken);
            if (existing is null
                || existing.ConsumedAt is not null
                || existing.ExpiresAt <= now
                || !string.Equals(existing.FlowType, expectedFlowType, StringComparison.Ordinal)
                || existing.PendingInstallationId != installationId)
            {
                return null;
            }

            return existing;
        }

        return await dbContext.InstallationFlowStates
            .AsNoTracking()
            .SingleAsync(state => state.StateHash == stateHash, cancellationToken);
    }

    public async Task<ConsumedGitHubInstallationState?> TryConsumeAsync(
        string stateHash,
        Guid expectedUserId,
        string expectedFlowType,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var affected = await dbContext.InstallationFlowStates
            .Where(state => state.StateHash == stateHash
                && state.InitiatingUserId == expectedUserId
                && state.FlowType == expectedFlowType
                && state.ConsumedAt == null
                && state.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(state => state.ConsumedAt, now),
                cancellationToken);

        if (affected != 1)
        {
            return null;
        }

        return await dbContext.InstallationFlowStates
            .AsNoTracking()
            .Where(state => state.StateHash == stateHash)
            .Select(state => new ConsumedGitHubInstallationState(
                state.ProjectId,
                state.InitiatingUserId,
                state.FlowType,
                state.ReturnPath,
                state.PendingInstallationId,
                state.ConsumedAt!.Value))
            .SingleAsync(cancellationToken);
    }

    public async Task<GitHubInstallationFlowState?> FindAsync(
        string stateHash,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.InstallationFlowStates
            .AsNoTracking()
            .SingleOrDefaultAsync(state => state.StateHash == stateHash, cancellationToken);
    }
}
