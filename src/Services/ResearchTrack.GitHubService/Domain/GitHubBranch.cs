namespace ResearchTrack.GitHubService.Domain;

public sealed class GitHubBranch
{
    public Guid Id { get; set; }
    public Guid RepositoryLinkId { get; set; }
    public required string Name { get; set; }
    public required string HeadSha { get; set; }
    public bool IsProtected { get; set; }
    public DateTime LastSeenAt { get; set; }
}
