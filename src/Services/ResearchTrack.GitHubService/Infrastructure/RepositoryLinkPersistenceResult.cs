using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Features.Synchronization;

namespace ResearchTrack.GitHubService.Infrastructure;

public sealed record RepositoryLinkPersistenceResult(
    ProjectGitHubRepositoriesResponse Response,
    IReadOnlyList<InitialRepositorySyncRequest> SyncRequests);
