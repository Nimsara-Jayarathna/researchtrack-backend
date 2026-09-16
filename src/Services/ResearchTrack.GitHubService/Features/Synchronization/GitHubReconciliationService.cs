using Microsoft.EntityFrameworkCore;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Features.Synchronization;

public sealed class GitHubReconciliationService : IGitHubReconciliationService
{
    private readonly IDbContextFactory<GitHubDbContext> _dbContextFactory;
    private readonly IGitHubRepositorySyncClient _gitHubClient;
    private readonly IGitHubInstallationTokenProvider _tokenProvider;
    private readonly IRepositorySyncQueue _syncQueue;
    private readonly ILogger<GitHubReconciliationService> _logger;

    public GitHubReconciliationService(
        IDbContextFactory<GitHubDbContext> dbContextFactory,
        IGitHubRepositorySyncClient gitHubClient,
        IGitHubInstallationTokenProvider tokenProvider,
        IRepositorySyncQueue syncQueue,
        ILogger<GitHubReconciliationService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _gitHubClient = gitHubClient;
        _tokenProvider = tokenProvider;
        _syncQueue = syncQueue;
        _logger = logger;
    }

    public async Task<GitHubReconciliationResult> ReconcileAsync(CancellationToken cancellationToken)
    {
        var candidates = await LoadCandidatesAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return new GitHubReconciliationResult(0, 0, 0, 0);
        }

        var checkedLinks = 0;
        var queuedLinks = 0;
        var failedChecks = 0;

        foreach (var group in candidates.GroupBy(
                     candidate => new { candidate.InstallationId, candidate.GitHubRepoId }))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sample = group.First();
            GitHubSyncRepository remoteRepository;
            GitHubDefaultBranchHead remoteHead;

            try
            {
                var token = await _tokenProvider.GetTokenAsync(group.Key.InstallationId, cancellationToken);
                remoteRepository = await _gitHubClient.GetRepositoryAsync(
                    sample.OwnerLogin,
                    sample.Name,
                    token,
                    cancellationToken);

                if (remoteRepository.Id != group.Key.GitHubRepoId)
                {
                    throw new InvalidOperationException(
                        "The remote GitHub repository identity does not match the linked ResearchTrack repository.");
                }

                remoteHead = await _gitHubClient.GetDefaultBranchHeadAsync(
                    remoteRepository.OwnerLogin,
                    remoteRepository.Name,
                    remoteRepository.DefaultBranch,
                    token,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var affectedLinks = group.Count();
                checkedLinks += affectedLinks;
                failedChecks += affectedLinks;
                GitHubReconciliationMetrics.ChecksTotal.WithLabels("failed").Inc(affectedLinks);

                _logger.LogWarning(
                    exception,
                    "GitHub reconciliation check failed. InstallationId={InstallationId} GitHubRepoId={GitHubRepoId} LinkedRepositoryCount={LinkedRepositoryCount}",
                    group.Key.InstallationId,
                    group.Key.GitHubRepoId,
                    affectedLinks);
                continue;
            }

            foreach (var candidate in group)
            {
                checkedLinks++;
                var changed = !string.Equals(
                                  candidate.DefaultBranch,
                                  remoteHead.DefaultBranch,
                                  StringComparison.Ordinal)
                              || !string.Equals(
                                  candidate.LastKnownHeadSha,
                                  remoteHead.HeadSha,
                                  StringComparison.OrdinalIgnoreCase);

                if (!changed)
                {
                    GitHubReconciliationMetrics.ChecksTotal.WithLabels("unchanged").Inc();
                    continue;
                }

                try
                {
                    await _syncQueue.EnqueueAsync(
                        new RepositorySyncWorkItem(candidate.LinkedRepositoryId, GitHubSyncTriggers.Reconciliation),
                        cancellationToken);
                    queuedLinks++;
                    GitHubReconciliationMetrics.ChecksTotal.WithLabels("changed").Inc();
                    GitHubReconciliationMetrics.QueuedTotal.Inc();

                    _logger.LogInformation(
                        "GitHub reconciliation detected a default-branch change and queued synchronization. LinkedRepositoryId={LinkedRepositoryId} DefaultBranchChanged={DefaultBranchChanged} HeadChanged={HeadChanged}",
                        candidate.LinkedRepositoryId,
                        !string.Equals(candidate.DefaultBranch, remoteHead.DefaultBranch, StringComparison.Ordinal),
                        !string.Equals(candidate.LastKnownHeadSha, remoteHead.HeadSha, StringComparison.OrdinalIgnoreCase));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failedChecks++;
                    GitHubReconciliationMetrics.ChecksTotal.WithLabels("failed").Inc();
                    _logger.LogWarning(
                        exception,
                        "GitHub reconciliation detected a change but could not queue synchronization. LinkedRepositoryId={LinkedRepositoryId}",
                        candidate.LinkedRepositoryId);
                }
            }
        }

        return new GitHubReconciliationResult(
            candidates.Count,
            checkedLinks,
            queuedLinks,
            failedChecks);
    }

    private async Task<IReadOnlyList<ReconciliationCandidate>> LoadCandidatesAsync(
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await (
                from link in dbContext.ProjectRepositoryLinks.AsNoTracking()
                join source in dbContext.AccessSources.AsNoTracking() on link.SourceId equals source.Id
                join repository in dbContext.Repositories.AsNoTracking() on link.GitHubRepositoryId equals repository.Id
                where link.Active
                      && link.Enabled
                      && source.Active
                      && source.ConnectionStatus == GitHubConnectionStatuses.Connected
                      && source.InstallationId != null
                      && repository.Available
                select new ReconciliationCandidate(
                    link.Id,
                    source.InstallationId!.Value,
                    link.GitHubRepoId,
                    link.OwnerLogin,
                    link.Name,
                    link.DefaultBranch,
                    link.LastKnownHeadSha))
            .ToListAsync(cancellationToken);
    }

    private sealed record ReconciliationCandidate(
        Guid LinkedRepositoryId,
        long InstallationId,
        long GitHubRepoId,
        string OwnerLogin,
        string Name,
        string? DefaultBranch,
        string? LastKnownHeadSha);
}
