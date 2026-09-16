namespace ResearchTrack.GitHubService.Features.Synchronization;

public interface IInitialRepositorySyncRequester
{
    Task RequestAsync(
        InitialRepositorySyncRequest request,
        CancellationToken cancellationToken);
}
