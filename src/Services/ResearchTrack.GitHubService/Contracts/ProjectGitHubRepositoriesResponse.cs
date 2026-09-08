using System.Text.Json.Serialization;

namespace ResearchTrack.GitHubService.Contracts;

public sealed record ProjectGitHubRepositoriesResponse(
    Guid ProjectId,
    int MaxLinkedRepositories,
    int MaxEnabledRepositories,
    IReadOnlyList<GitHubAccessSourceResponse> AccessSources,
    IReadOnlyList<ProjectRepositoryLinkResponse> Repositories);

public sealed record GitHubAccessSourceResponse(
    Guid Id,
    Guid ProjectId,
    long? InstallationId,
    string OwnerLogin,
    string OwnerType,
    string AccessType,
    bool Active,
    DateTime CreatedAt);

public sealed record ProjectRepositoryLinkResponse(
    Guid Id,
    Guid? SourceId,
    string? AccessType,
    [property: JsonPropertyName("githubRepositoryId")] Guid? GitHubRepositoryId,
    [property: JsonPropertyName("githubRepoId")] long GitHubRepoId,
    string? FullName,
    string? Name,
    string? CustomName,
    string? OwnerLogin,
    string? DefaultBranch,
    string? Url,
    bool Primary,
    bool Enabled,
    DateTime LinkedAt,
    DateTime? LastSyncedAt,
    string? SyncStatus);
