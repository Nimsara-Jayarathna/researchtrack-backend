namespace ResearchTrack.GitHubService.Domain;

public sealed class GitHubPullRequest
{
    public Guid Id { get; set; }
    public Guid RepositoryLinkId { get; set; }
    public long GitHubPullRequestId { get; set; }
    public int Number { get; set; }
    public required string Title { get; set; }
    public string? Body { get; set; }
    public required string State { get; set; }
    public bool IsDraft { get; set; }
    public bool IsMerged { get; set; }
    public long? AuthorGitHubId { get; set; }
    public string? AuthorLogin { get; set; }
    public long? MergedByGitHubId { get; set; }
    public string? MergedByLogin { get; set; }
    public required string SourceBranch { get; set; }
    public required string SourceSha { get; set; }
    public required string TargetBranch { get; set; }
    public required string TargetSha { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? MergedAt { get; set; }
    public string? MergeCommitSha { get; set; }
    public required string HtmlUrl { get; set; }
    public int? Additions { get; set; }
    public int? Deletions { get; set; }
    public int? ChangedFiles { get; set; }
    public int? CommitsCount { get; set; }
    public int? CommentsCount { get; set; }
    public int? ReviewCommentsCount { get; set; }
    public DateTime LastSyncedAt { get; set; }
}
