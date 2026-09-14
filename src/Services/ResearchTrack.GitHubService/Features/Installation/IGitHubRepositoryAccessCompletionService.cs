using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Features.Installation;

public interface IGitHubRepositoryAccessCompletionService
{
    Task<GitHubRepositoryAccessCompletionResult> CompleteAsync(
        Guid requestId,
        GitHubInstallationInfo installation,
        GitHubInstallationRepository repository,
        CancellationToken cancellationToken);
}
