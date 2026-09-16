namespace ResearchTrack.GitHubService.Features.Synchronization;

public sealed record GitHubSyncRepository(
    long Id,
    string OwnerLogin,
    string Name,
    string FullName,
    string HtmlUrl,
    string DefaultBranch,
    bool IsPrivate,
    bool IsArchived,
    bool IsFork,
    string? Description,
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    DateTime? PushedAt);

public sealed record GitHubSyncBranch(string Name, string HeadSha, bool Protected);

public sealed record GitHubSyncCommit(
    string Sha,
    string Message,
    long? AuthorGitHubId,
    string? AuthorLogin,
    string? AuthorName,
    string? AuthorEmail,
    long? CommitterGitHubId,
    string? CommitterLogin,
    DateTime? AuthoredAt,
    DateTime? CommittedAt,
    string HtmlUrl,
    int ParentsCount,
    int? Additions,
    int? Deletions,
    int? ChangedFiles);

public sealed record GitHubSyncContributor(
    long GitHubUserId,
    string Login,
    string? AvatarUrl,
    string? ProfileUrl,
    int Contributions);

public sealed record GitHubSyncPullRequest(
    long Id,
    int Number,
    string Title,
    string? Body,
    string State,
    bool Draft,
    bool Merged,
    long? AuthorGitHubId,
    string? AuthorLogin,
    long? MergedByGitHubId,
    string? MergedByLogin,
    string SourceBranch,
    string SourceSha,
    string TargetBranch,
    string TargetSha,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ClosedAt,
    DateTime? MergedAt,
    string? MergeCommitSha,
    string HtmlUrl,
    int? Additions,
    int? Deletions,
    int? ChangedFiles,
    int? Commits,
    int? Comments,
    int? ReviewComments);

public sealed record GitHubSyncReview(
    long Id,
    long? ReviewerGitHubId,
    string? ReviewerLogin,
    string State,
    DateTime? SubmittedAt);
