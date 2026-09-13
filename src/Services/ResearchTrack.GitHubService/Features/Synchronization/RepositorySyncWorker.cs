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
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IGitHubRepositorySynchronizationService>();
                await service.SynchronizeAsync(
                    workItem.LinkedRepositoryId,
                    workItem.Trigger,
                    stoppingToken);
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
                _queue.Complete(workItem.LinkedRepositoryId);
            }
        }
    }
}
