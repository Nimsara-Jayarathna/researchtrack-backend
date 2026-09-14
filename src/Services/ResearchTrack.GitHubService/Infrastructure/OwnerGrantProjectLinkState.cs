namespace ResearchTrack.GitHubService.Infrastructure;

public sealed record OwnerGrantProjectLinkState(
    int ActiveLinkedRepositories,
    int ActiveEnabledRepositories,
    bool ExactRepositoryAlreadyLinked);
