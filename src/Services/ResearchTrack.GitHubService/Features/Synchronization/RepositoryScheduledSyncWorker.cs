using Microsoft.EntityFrameworkCore;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Features.Synchronization;

public sealed class RepositoryScheduledSyncWorker : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromHours(1);

    private readonly IDbContextFactory<GitHubDbContext> _dbContextFactory;
    private readonly IRepositorySyncQueue _queue;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RepositoryScheduledSyncWorker> _logger;

    public RepositoryScheduledSyncWorker(
        IDbContextFactory<GitHubDbContext> dbContextFactory,
        IRepositorySyncQueue queue,
        TimeProvider timeProvider,
        ILogger<RepositoryScheduledSyncWorker> logger)
    {
        _dbContextFactory = dbContextFactory;
        _queue = queue;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnqueueStaleRepositoriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Scheduled GitHub synchronization scan failed.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }

    private async Task EnqueueStaleRepositoriesAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var cutoff = now - StaleAfter;
        var failureCutoff = now - FailureCooldown;
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var repositoryIds = await dbContext.ProjectRepositoryLinks
            .AsNoTracking()
            .Where(link =>
                link.Active
                && link.Enabled
                && link.SyncStatus != GitHubSyncStatuses.InProgress
                && (link.LastFailedSyncAt == null || link.LastFailedSyncAt < failureCutoff)
                && (link.LastSyncedAt == null || link.LastSyncedAt < cutoff))
            .OrderBy(link => link.LastSyncedAt)
            .Select(link => link.Id)
            .Take(25)
            .ToListAsync(cancellationToken);

        foreach (var repositoryId in repositoryIds)
        {
            await _queue.EnqueueAsync(
                new RepositorySyncWorkItem(repositoryId, GitHubSyncTriggers.Scheduled),
                cancellationToken);
        }
    }
}
