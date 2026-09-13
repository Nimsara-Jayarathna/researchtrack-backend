namespace ResearchTrack.GitHubService.Domain;

public sealed class GitHubSyncRun
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid RepositoryLinkId { get; set; }
    public required string Trigger { get; set; }
    public required string Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int CommitsFetched { get; set; }
    public int ContributorsFetched { get; set; }
    public int PullRequestsFetched { get; set; }
    public int ReviewsFetched { get; set; }
    public int BranchesFetched { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
