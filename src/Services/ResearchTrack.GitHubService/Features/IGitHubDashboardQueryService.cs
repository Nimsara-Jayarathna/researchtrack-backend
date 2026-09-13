using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Features;

public interface IGitHubDashboardQueryService
{
    Task<GitHubDashboardResponse> GetDashboardAsync(
        Guid userId,
        Guid projectId,
        Guid? linkedRepositoryId,
        CancellationToken cancellationToken);

    Task<GitHubCompatibilityPage<GitHubDashboardCommitResponse>> GetActivityAsync(
        Guid userId,
        Guid projectId,
        Guid? linkedRepositoryId,
        int page,
        int size,
        CancellationToken cancellationToken);

    Task<GitHubCompatibilityPage<GitHubDashboardContributorResponse>> GetContributorsAsync(
        Guid userId,
        Guid projectId,
        Guid? linkedRepositoryId,
        int page,
        int size,
        CancellationToken cancellationToken);
}
