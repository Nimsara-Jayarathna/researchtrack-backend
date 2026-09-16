namespace ResearchTrack.GitHubService.Contracts;

public sealed record StartGitHubInstallationRequest(
    Guid? ProjectId,
    string? RequestToken);
