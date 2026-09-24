namespace ResearchTrack.GitHubService.Contracts;

public sealed record GitHubPage<T>(
    IReadOnlyList<T> Items,
    int Page,
    int Size,
    int Total,
    bool HasMore);

public sealed record GitHubCommitResponse(
    string Sha,
    string Message,
    long? AuthorGitHubId,
    string? AuthorLogin,
    string? AuthorName,
    DateTime? AuthoredAt,
    DateTime? CommittedAt,
    string HtmlUrl,
    int? Additions,
    int? Deletions,
    int? ChangedFiles);

public sealed record GitHubContributorResponse(
    long GitHubUserId,
    string Login,
    string? AvatarUrl,
    string? ProfileUrl,
    int GitHubContributionCount,
    int ObservedCommitCount,
    long ObservedAdditions,
    long ObservedDeletions,
    long ObservedChangedFiles,
    DateTime? FirstCommitAt,
    DateTime? LastCommitAt,
    DateTime LastSyncedAt);

public sealed record GitHubPullRequestResponse(
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

public sealed record GitHubSyncRunResponse(
    Guid Id,
    string Trigger,
    string Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int CommitsFetched,
    int ContributorsFetched,
    int PullRequestsFetched,
    int ReviewsFetched,
    int BranchesFetched,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record GitHubSyncQueuedResponse(Guid LinkedRepositoryId, string Status);
public sealed record GitHubProjectSyncQueuedResponse(Guid ProjectId, string Status);
