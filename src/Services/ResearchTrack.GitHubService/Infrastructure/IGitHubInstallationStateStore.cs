using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Installation;

namespace ResearchTrack.GitHubService.Infrastructure;

public interface IGitHubInstallationStateStore
{
    Task CreateAsync(GitHubInstallationFlowState state, CancellationToken cancellationToken);

    Task<GitHubInstallationFlowState?> TryBindInstallationAsync(
        string stateHash,
        long installationId,
        string expectedFlowType,
        DateTime now,
        CancellationToken cancellationToken);

    Task<ConsumedGitHubInstallationState?> TryConsumeAsync(
        string stateHash,
        Guid expectedUserId,
        string expectedFlowType,
        DateTime now,
        CancellationToken cancellationToken);

    Task<GitHubInstallationFlowState?> FindAsync(
        string stateHash,
        CancellationToken cancellationToken);
}
