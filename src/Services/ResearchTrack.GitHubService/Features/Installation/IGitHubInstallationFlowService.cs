using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Features.Installation;

public interface IGitHubInstallationFlowService
{
    Task<GitHubInstallStartResponse> StartAsync(
        Guid userId,
        StartGitHubInstallationRequest request,
        CancellationToken cancellationToken);

    Task<GitHubInstallationCallbackResult> CompleteCallbackAsync(
        string? state,
        long? installationId,
        string? setupAction,
        string? code,
        string? error,
        CancellationToken cancellationToken);
}
