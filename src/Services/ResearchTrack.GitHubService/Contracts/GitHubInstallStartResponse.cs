using System.Text.Json.Serialization;

namespace ResearchTrack.GitHubService.Contracts;

public sealed record GitHubInstallStartResponse(
    Guid ProjectId,
    [property: JsonPropertyName("githubAuthorizeUrl")] string GitHubAuthorizeUrl,
    string FlowType,
    DateTime ExpiresAt);
