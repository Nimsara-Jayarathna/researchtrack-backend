using System.Text.Json.Serialization;

namespace ResearchTrack.GitHubService.Contracts;

public sealed record GitHubAvailableRepositoriesResponse(
    Guid SourceId,
    IReadOnlyList<GitHubRepositoryOptionResponse> Items,
    int TotalCount);

public sealed record GitHubRepositoryOptionResponse(
    Guid Id,
    [property: JsonPropertyName("githubRepoId")] long GitHubRepoId,
    string FullName,
    string Name,
    string OwnerLogin,
    string? DefaultBranch,
    string Url);
