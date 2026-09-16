namespace ResearchTrack.GitHubService.Features.Synchronization;

public interface IGitHubRepositorySyncClient
{
    Task<GitHubSyncRepository> GetRepositoryAsync(string owner, string repository, string? token, CancellationToken cancellationToken);
    Task<GitHubDefaultBranchHead> GetDefaultBranchHeadAsync(string owner, string repository, string branch, string? token, CancellationToken cancellationToken);
    Task<IReadOnlyList<GitHubSyncCommit>> GetCommitsAsync(string owner, string repository, string branch, string? token, CancellationToken cancellationToken);
    Task<GitHubSyncCommit> GetCommitAsync(string owner, string repository, string sha, string? token, CancellationToken cancellationToken);
    Task<IReadOnlyList<GitHubSyncContributor>> GetContributorsAsync(string owner, string repository, string? token, CancellationToken cancellationToken);
    Task<IReadOnlyList<GitHubSyncPullRequest>> GetPullRequestsAsync(string owner, string repository, string? token, CancellationToken cancellationToken);
    Task<GitHubSyncPullRequest> GetPullRequestAsync(string owner, string repository, int number, string? token, CancellationToken cancellationToken);
    Task<IReadOnlyList<GitHubSyncReview>> GetPullRequestReviewsAsync(string owner, string repository, int number, string? token, CancellationToken cancellationToken);
}
