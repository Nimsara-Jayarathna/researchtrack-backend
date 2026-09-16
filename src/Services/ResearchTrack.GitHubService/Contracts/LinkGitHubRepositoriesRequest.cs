using System.Text.Json.Serialization;

namespace ResearchTrack.GitHubService.Contracts;

public sealed record LinkGitHubRepositoriesRequest(
    Guid ProjectId,
    Guid SourceId,
    IReadOnlyList<LinkGitHubRepositoryRequestItem>? Repositories);

public sealed record LinkGitHubRepositoryRequestItem(
    [property: JsonPropertyName("githubRepositoryId")] Guid GitHubRepositoryId,
    string? CustomName,
    bool? Primary);
