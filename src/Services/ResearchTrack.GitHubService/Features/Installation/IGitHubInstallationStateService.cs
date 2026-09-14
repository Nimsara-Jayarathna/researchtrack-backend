namespace ResearchTrack.GitHubService.Features.Installation;

public interface IGitHubInstallationStateService
{
    Task<GitHubInstallationState> CreateAsync(
        Guid projectId,
        Guid initiatingUserId,
        string flowType,
        string returnPath,
        CancellationToken cancellationToken);

    Task<GitHubInstallationState> CreateRequestedAsync(
        Guid projectId,
        Guid initiatingUserId,
        Guid repositoryAccessRequestId,
        string returnPath,
        CancellationToken cancellationToken);

    Task<ValidatedGitHubInstallationState> ValidateExternalCallbackAsync(
        string state,
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


    Task<ValidatedGitHubInstallationState> BindRequestedInstallationAsync(
        string state,
        Guid repositoryAccessRequestId,
        long installationId,
        CancellationToken cancellationToken);

    Task<ConsumedGitHubInstallationState> ConsumeAsync(
        string state,
        Guid expectedUserId,
        string expectedFlowType,
        CancellationToken cancellationToken);
}
