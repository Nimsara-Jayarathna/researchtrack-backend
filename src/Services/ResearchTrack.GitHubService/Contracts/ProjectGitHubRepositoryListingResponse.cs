namespace ResearchTrack.GitHubService.Contracts;

public sealed record ProjectGitHubRepositoryListingResponse(
    Guid ProjectId,
    IReadOnlyList<GitHubAvailableRepositoriesResponse> Inventory);
