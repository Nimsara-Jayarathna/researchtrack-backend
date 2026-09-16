using ResearchTrack.GitHubService.Configuration;

namespace ResearchTrack.GitHubService.Features.Synchronization;

public sealed class GitHubReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly GitHubReconciliationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitHubReconciliationWorker> _logger;

    public GitHubReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        GitHubReconciliationOptions options,
        TimeProvider timeProvider,
        ILogger<GitHubReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;

        GitHubReconciliationMetrics.IntervalSeconds.Set(_options.Interval.TotalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "GitHub reconciliation worker started. IntervalMinutes={IntervalMinutes}",
            _options.Interval.TotalMinutes);

        await RunCycleAsync(stoppingToken);

        using var timer = new PeriodicTimer(_options.Interval, _timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCycleAsync(stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IGitHubReconciliationService>();
            var result = await service.ReconcileAsync(cancellationToken);
            var nowUnixSeconds = _timeProvider.GetUtcNow().ToUnixTimeSeconds();

            GitHubReconciliationMetrics.LastCompletedTimestampSeconds.Set(nowUnixSeconds);
            if (result.Successful)
            {
                GitHubReconciliationMetrics.CyclesTotal.WithLabels("completed").Inc();
                GitHubReconciliationMetrics.LastSuccessTimestampSeconds.Set(nowUnixSeconds);
            }
            else
            {
                GitHubReconciliationMetrics.CyclesTotal.WithLabels("partial_failure").Inc();
            }

            _logger.LogInformation(
                "GitHub reconciliation cycle completed. EligibleLinks={EligibleLinks} CheckedLinks={CheckedLinks} QueuedLinks={QueuedLinks} FailedChecks={FailedChecks}",
                result.EligibleLinks,
                result.CheckedLinks,
                result.QueuedLinks,
                result.FailedChecks);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            GitHubReconciliationMetrics.CyclesTotal.WithLabels("failed").Inc();
            _logger.LogError(exception, "GitHub reconciliation cycle failed.");
        }
    }
}
