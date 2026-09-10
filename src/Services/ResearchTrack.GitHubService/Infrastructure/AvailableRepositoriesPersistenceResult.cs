using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Infrastructure;

public sealed record AvailableRepositoriesPersistenceResult(
    Guid ProjectId,
    GitHubAvailableRepositoriesResponse Response);
