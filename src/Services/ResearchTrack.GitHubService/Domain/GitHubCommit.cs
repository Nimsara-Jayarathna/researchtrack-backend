namespace ResearchTrack.GitHubService.Domain;

public sealed class GitHubCommit
{
    public Guid Id { get; set; }
    public Guid RepositoryLinkId { get; set; }
    public required string Sha { get; set; }
    public required string Message { get; set; }
    public long? AuthorGitHubId { get; set; }
    public string? AuthorLogin { get; set; }
    public string? AuthorName { get; set; }
    public string? AuthorEmail { get; set; }
    public long? CommitterGitHubId { get; set; }
    public string? CommitterLogin { get; set; }
    public DateTime? AuthoredAt { get; set; }
    public DateTime? CommittedAt { get; set; }
    public required string HtmlUrl { get; set; }
    public int ParentsCount { get; set; }
    public int? Additions { get; set; }
    public int? Deletions { get; set; }
    public int? ChangedFiles { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}
