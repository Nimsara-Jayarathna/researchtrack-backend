namespace ResearchTrack.GitHubService.Contracts;

public sealed record GitHubDashboardRepositoryResponse(
    Guid? Id,
    string Name,
    string Url,
    string? DefaultBranch,
    DateTime? LastSyncedAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record GitHubDashboardActivitySummaryResponse(
    int TotalCommits,
    int TotalPullRequests,
    int OpenPullRequests,
    int DraftPullRequests,
    int MergedPullRequests,
    int ClosedPullRequests,
    DateTime? LastActivityAt,
    string? LastActivityType,
    int? LastActivityPullRequestNumber,
    string? LastActivityPullRequestStatus,
    string Status);

public sealed record GitHubDashboardContributorResponse(
    string Name,
    int CommitCount,
    string? GitHubUsername,
    string? AvatarUrl);

public sealed record GitHubDashboardCommitResponse(
    string? Sha,
    string Message,
    string Author,
    string? GitHubUsername,
    string? AvatarUrl,
    DateTime? CommittedAt,
    string? Type);


public sealed record GitHubDashboardPullRequestResponse(
    long GitHubPullRequestId,
    int Number,
    string Title,
    string? Body,
    string State,
    bool IsDraft,
    bool IsMerged,
    string? AuthorLogin,
    long? MergedByGitHubId,
    string? MergedByLogin,
    string SourceBranch,
    string TargetBranch,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ClosedAt,
    DateTime? MergedAt,
    string HtmlUrl,
    int? Additions,
    int? Deletions,
    int? ChangedFiles,
    int? CommitsCount,
    int? CommentsCount,
    int? ReviewCommentsCount);

public sealed record GitHubDashboardResponse(
    bool RepositoryLinked,
    long? AuthorizedInstallationId,
    int? AccessibleRepositoryCount,
    string AccessScope,
    IReadOnlyList<GitHubDashboardRepositoryResponse> Repositories,
    string? PrimaryRepositoryUrl,
    GitHubDashboardActivitySummaryResponse ActivitySummary,
    IReadOnlyList<GitHubDashboardContributorResponse> ContributorsPreview,
    IReadOnlyList<GitHubDashboardCommitResponse> RecentCommitsPreview,
    IReadOnlyList<GitHubDashboardPullRequestResponse> PullRequestsPreview,
    bool HasUnacknowledgedAccess);

