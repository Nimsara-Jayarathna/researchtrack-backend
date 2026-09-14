using Microsoft.EntityFrameworkCore;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Installation;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Infrastructure;

public sealed class GitHubInstallationStateStore : IGitHubInstallationStateStore
{
    private readonly IDbContextFactory<GitHubDbContext> _dbContextFactory;
    private readonly TimeProvider _timeProvider;

    public GitHubInstallationStateStore(IDbContextFactory<GitHubDbContext> dbContextFactory, TimeProvider timeProvider)
    {
        _dbContextFactory = dbContextFactory;
        _timeProvider = timeProvider;
    }

    public async Task CreateAsync(
        GitHubInstallationFlowState state,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.InstallationFlowStates.Add(state);
        await dbContext.SaveChangesAsync(cancellationToken);
    }


    public async Task<RequestedInstallationStateCreateOutcome> CreateRequestedAsync(
        GitHubInstallationFlowState state,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (state.RepositoryAccessRequestId is not Guid requestId || requestId == Guid.Empty
            || !string.Equals(state.FlowType, GitHubInstallationFlowTypes.Requested, StringComparison.Ordinal))
        {
            return RequestedInstallationStateCreateOutcome.ContextMismatch;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var request = await dbContext.RepositoryAccessRequests
            .SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken);
        now = Later(now, _timeProvider.GetUtcNow().UtcDateTime);
        if (request is null
            || request.ProjectId != state.ProjectId
            || request.InitiatingUserId != state.InitiatingUserId
            || !string.Equals(request.FlowType, GitHubInstallationFlowTypes.Requested, StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return RequestedInstallationStateCreateOutcome.ContextMismatch;
        }

        if (!string.Equals(request.Status, GitHubRepositoryAccessRequestStatuses.Pending, StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return string.Equals(request.Status, GitHubRepositoryAccessRequestStatuses.Expired, StringComparison.Ordinal)
                ? RequestedInstallationStateCreateOutcome.Expired
                : RequestedInstallationStateCreateOutcome.NotPending;
        }

        if (request.ExpiresAt <= now)
        {
            request.Status = GitHubRepositoryAccessRequestStatuses.Expired;
            request.FailureCode = null;
            request.ConsumedAt = now;
            request.Version++;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RequestedInstallationStateCreateOutcome.Expired;
        }

        request.AuthorizationStartedAt ??= now;
        request.Version++;
        dbContext.InstallationFlowStates.Add(state);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ResearchTrack.BuildingBlocks.Api.Exceptions.ApiException(
                StatusCodes.Status409Conflict,
                ResearchTrack.BuildingBlocks.Api.Constants.ErrorCodes.Conflict,
                "The access request changed. Please validate the link again.");
        }
        await transaction.CommitAsync(cancellationToken);
        return RequestedInstallationStateCreateOutcome.Created;
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

    public async Task<GitHubInstallationFlowState?> TryBindRequestedInstallationAsync(
        string stateHash,
        Guid repositoryAccessRequestId,
        long installationId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var state = await dbContext.InstallationFlowStates
            .SingleOrDefaultAsync(item => item.StateHash == stateHash, cancellationToken);
        var request = await dbContext.RepositoryAccessRequests
            .SingleOrDefaultAsync(item => item.Id == repositoryAccessRequestId, cancellationToken);
        now = Later(now, _timeProvider.GetUtcNow().UtcDateTime);

        if (state is null || request is null)
        {
            return null;
        }

        var contextMatches = state.RepositoryAccessRequestId == repositoryAccessRequestId
            && string.Equals(state.FlowType, GitHubInstallationFlowTypes.Requested, StringComparison.Ordinal)
            && state.ProjectId == request.ProjectId
            && state.InitiatingUserId == request.InitiatingUserId
            && string.Equals(request.FlowType, GitHubInstallationFlowTypes.Requested, StringComparison.Ordinal);

        // Close the race between callback pre-validation and installation binding. If the owner
        // request expires after validation but before this transaction acquires the rows, materialize
        // EXPIRED and consume this one-time state atomically. The expired request can never bind an
        // installation on a later replay.
        if (contextMatches
            && state.ConsumedAt is null
            && state.ExpiresAt > now
            && string.Equals(request.Status, GitHubRepositoryAccessRequestStatuses.Pending, StringComparison.Ordinal)
            && request.ExpiresAt <= now)
        {
            request.Status = GitHubRepositoryAccessRequestStatuses.Expired;
            request.FailureCode = null;
            request.ConsumedAt = now;
            request.Version++;
            state.ConsumedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        if (!contextMatches
            || state.ConsumedAt is not null
            || state.ExpiresAt <= now
            || !string.Equals(request.Status, GitHubRepositoryAccessRequestStatuses.Pending, StringComparison.Ordinal)
            || (state.PendingInstallationId is long stateInstallation && stateInstallation != installationId)
            || (request.PendingInstallationId is long requestInstallation && requestInstallation != installationId))
        {
            return null;
        }

        state.PendingInstallationId ??= installationId;
        state.AuthorizationStartedAt ??= now;
        request.PendingInstallationId ??= installationId;
        request.AuthorizationStartedAt ??= now;
        request.Version++;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }
        await transaction.CommitAsync(cancellationToken);
        dbContext.Entry(state).State = EntityState.Detached;
        return state;
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
                state.RepositoryAccessRequestId,
                state.PendingInstallationId,
                state.ConsumedAt!.Value))
            .SingleAsync(cancellationToken);
    }

    private static DateTime Later(DateTime first, DateTime second) => first > second ? first : second;

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
