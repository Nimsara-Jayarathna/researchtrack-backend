using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Features.Installation;

public interface IGitHubRepositoryAccessRequestService
{
    Task<GitHubRepositoryAccessRequestCreateResponse> CreateAsync(
        Guid userId,
        CreateGitHubRepositoryAccessRequest request,
        CancellationToken cancellationToken);

    Task<GitHubRepositoryAccessRequestMemberStatusResponse> GetStatusAsync(
        Guid userId,
        Guid requestId,
        CancellationToken cancellationToken);
}
