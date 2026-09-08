namespace ResearchTrack.GitHubService.Contracts;

public sealed record CreatePublicAccessSourceRequest(
    Guid ProjectId,
    string RepositoryUrl);
