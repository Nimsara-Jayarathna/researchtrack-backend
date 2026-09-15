namespace ResearchTrack.GitHubService.Features.Synchronization;

public sealed class RepositorySyncWorker : BackgroundService
{
    private readonly IRepositorySyncQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RepositorySyncWorker> _logger;

    public RepositorySyncWorker(
        IRepositorySyncQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<RepositorySyncWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in _queue.ReadAllAsync(stoppingToken))
        {
            _queue.MarkRunning(workItem.LinkedRepositoryId);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IGitHubRepositorySynchronizationService>();
                var outcome = await service.SynchronizeAsync(
                    workItem.LinkedRepositoryId,
                    workItem.Trigger,
                    stoppingToken);
                if (outcome == GitHubSynchronizationOutcome.AlreadyRunning)
                {
                    // Another synchronization path (for example the durable
                    // webhook worker) owns this repository right now. Ask the
                    // coalescing queue for one follow-up pass instead of silently
                    // losing this manual/initial reconciliation request.
                    await _queue.EnqueueAsync(workItem, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Queued GitHub synchronization failed. LinkedRepositoryId={LinkedRepositoryId} Trigger={Trigger}",
                    workItem.LinkedRepositoryId,
                    workItem.Trigger);
            }
            finally
            {
                await _queue.CompleteAsync(workItem.LinkedRepositoryId, stoppingToken);
            }
        }
    }
}
