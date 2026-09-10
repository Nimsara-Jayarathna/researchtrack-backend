namespace ResearchTrack.GitHubService.Features.Synchronization;

public sealed class DeferredInitialRepositorySyncRequester : IInitialRepositorySyncRequester
{
    private readonly ILogger<DeferredInitialRepositorySyncRequester> _logger;

    public DeferredInitialRepositorySyncRequester(
        ILogger<DeferredInitialRepositorySyncRequester> logger)
    {
        _logger = logger;
    }

    public Task RequestAsync(
        InitialRepositorySyncRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Initial repository synchronization is pending Story 11 integration. ProjectId={ProjectId} LinkedRepositoryId={LinkedRepositoryId}",
            request.ProjectId,
            request.LinkedRepositoryId);
        return Task.CompletedTask;
    }
}
