using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Features;

public interface IGitHubEvidenceQueryService
{
    Task<GitHubPage<GitHubCommitResponse>> GetCommitsAsync(Guid userId, Guid projectId, Guid linkedRepositoryId, int page, int size, CancellationToken cancellationToken);
    Task<GitHubPage<GitHubContributorResponse>> GetContributorsAsync(Guid userId, Guid projectId, Guid linkedRepositoryId, int page, int size, CancellationToken cancellationToken);
    Task<GitHubPage<GitHubPullRequestResponse>> GetPullRequestsAsync(
        Guid userId,
        Guid projectId,
        Guid linkedRepositoryId,
        int page,
        int size,
        string? status,
        string? search,
        CancellationToken cancellationToken);
    Task<GitHubPage<GitHubSyncRunResponse>> GetSyncRunsAsync(Guid userId, Guid projectId, Guid linkedRepositoryId, int page, int size, CancellationToken cancellationToken);
}
