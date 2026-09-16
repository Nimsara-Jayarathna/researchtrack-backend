namespace ResearchTrack.GitHubService.Configuration;

public sealed record GitHubRepositoryLinkOptions(
    int MaxLinkedRepositories,
    int MaxEnabledRepositories);
