namespace ResearchTrack.GitHubService.Infrastructure;

public sealed record GitHubPublicRepository(
    long Id,
    string OwnerLogin,
    string OwnerType,
    string Name,
    string FullName,
    string HtmlUrl,
    string? DefaultBranch);
