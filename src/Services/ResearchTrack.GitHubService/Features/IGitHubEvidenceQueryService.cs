using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Features;

public interface IGitHubEvidenceQueryService
{
    Task<GitHubEvidencePage<GitHubCommitResponse>> GetCommitsAsync(Guid userId, Guid projectId, Guid linkedRepositoryId, int page, int size, CancellationToken cancellationToken);
    Task<GitHubEvidencePage<GitHubContributorResponse>> GetContributorsAsync(Guid userId, Guid projectId, Guid linkedRepositoryId, int page, int size, CancellationToken cancellationToken);
    Task<GitHubEvidencePage<GitHubPullRequestResponse>> GetPullRequestsAsync(Guid userId, Guid projectId, Guid linkedRepositoryId, int page, int size, CancellationToken cancellationToken);
    Task<GitHubEvidencePage<GitHubSyncRunResponse>> GetSyncRunsAsync(Guid userId, Guid projectId, Guid linkedRepositoryId, int page, int size, CancellationToken cancellationToken);
}
