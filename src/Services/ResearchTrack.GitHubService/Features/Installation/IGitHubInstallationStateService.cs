namespace ResearchTrack.GitHubService.Features.Installation;

public interface IGitHubInstallationStateService
{
    Task<GitHubInstallationState> CreateAsync(
        Guid projectId,
        Guid initiatingUserId,
        string flowType,
        string returnPath,
        CancellationToken cancellationToken);

    Task<ValidatedGitHubInstallationState> ValidateAsync(
        string state,
        Guid expectedUserId,
        string expectedFlowType,
        CancellationToken cancellationToken);

    Task<ValidatedGitHubInstallationState> ValidateExternalCallbackAsync(
        string state,
        string expectedFlowType,
        CancellationToken cancellationToken);

    Task<ValidatedGitHubInstallationState> BindInstallationAsync(
        string state,
        long installationId,
        string expectedFlowType,
        CancellationToken cancellationToken);

    Task<ConsumedGitHubInstallationState> ConsumeAsync(
        string state,
        Guid expectedUserId,
        string expectedFlowType,
        CancellationToken cancellationToken);
}
