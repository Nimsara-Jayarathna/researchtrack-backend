using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Infrastructure;

public sealed record RepositoryEnablementPersistenceResult(
    ProjectGitHubRepositoriesResponse Response,
    bool Changed,
    bool Enabled);
