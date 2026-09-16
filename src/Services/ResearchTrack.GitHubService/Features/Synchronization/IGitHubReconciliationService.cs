namespace ResearchTrack.GitHubService.Features.Synchronization;

public interface IGitHubReconciliationService
{
    Task<GitHubReconciliationResult> ReconcileAsync(CancellationToken cancellationToken);
}
