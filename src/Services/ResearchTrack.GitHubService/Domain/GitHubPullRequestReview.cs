namespace ResearchTrack.GitHubService.Domain;

public sealed class GitHubPullRequestReview
{
    public Guid Id { get; set; }
    public Guid PullRequestId { get; set; }
    public long GitHubReviewId { get; set; }
    public long? ReviewerGitHubId { get; set; }
    public string? ReviewerLogin { get; set; }
    public required string State { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime LastSyncedAt { get; set; }
}
