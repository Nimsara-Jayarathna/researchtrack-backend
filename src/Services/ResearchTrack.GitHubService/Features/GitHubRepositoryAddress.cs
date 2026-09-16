namespace ResearchTrack.GitHubService.Features;

public sealed record GitHubRepositoryAddress(
    string Owner,
    string Repository,
    string NormalizedUrl);
