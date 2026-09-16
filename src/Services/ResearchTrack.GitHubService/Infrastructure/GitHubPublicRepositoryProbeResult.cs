namespace ResearchTrack.GitHubService.Infrastructure;

public sealed record GitHubPublicRepositoryProbeResult(
    string OwnerLogin,
    string Name,
    string FullName,
    string HtmlUrl,
    string? DefaultBranch);
