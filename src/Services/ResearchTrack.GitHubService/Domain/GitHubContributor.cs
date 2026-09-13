namespace ResearchTrack.GitHubService.Domain;

public sealed class GitHubContributor
{
    public Guid Id { get; set; }
    public Guid RepositoryLinkId { get; set; }
    public long GitHubUserId { get; set; }
    public required string Login { get; set; }
    public string? AvatarUrl { get; set; }
    public string? ProfileUrl { get; set; }
    public int GitHubContributionCount { get; set; }
    public int ObservedCommitCount { get; set; }
    public long ObservedAdditions { get; set; }
    public long ObservedDeletions { get; set; }
    public long ObservedChangedFiles { get; set; }
    public DateTime? FirstCommitAt { get; set; }
    public DateTime? LastCommitAt { get; set; }
    public DateTime LastSyncedAt { get; set; }
}
