using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ResearchTrack.GitHubService.Persistence;

#nullable disable

namespace ResearchTrack.GitHubService.Persistence.Migrations;

[DbContext(typeof(GitHubDbContext))]
[Migration("20260913043000_AddGitHubSynchronizationEvidence")]
public sealed class AddGitHubSynchronizationEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "LastSyncStartedAt",
            table: "project_repository_links",
            type: "datetime(6)",
            nullable: true);
        migrationBuilder.AddColumn<DateTime>(
            name: "LastFailedSyncAt",
            table: "project_repository_links",
            type: "datetime(6)",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "LastSyncError",
            table: "project_repository_links",
            type: "varchar(2048)",
            maxLength: 2048,
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "LastKnownHeadSha",
            table: "project_repository_links",
            type: "varchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "github_branches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                RepositoryLinkId = table.Column<Guid>(type: "char(36)", nullable: false),
                Name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                HeadSha = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                IsProtected = table.Column<bool>(type: "tinyint(1)", nullable: false),
                LastSeenAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_github_branches", x => x.Id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "github_commits",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                RepositoryLinkId = table.Column<Guid>(type: "char(36)", nullable: false),
                Sha = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                Message = table.Column<string>(type: "text", nullable: false),
                AuthorGitHubId = table.Column<long>(type: "bigint", nullable: true),
                AuthorLogin = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                AuthorName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                AuthorEmail = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: true),
                CommitterGitHubId = table.Column<long>(type: "bigint", nullable: true),
                CommitterLogin = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                AuthoredAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                CommittedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                HtmlUrl = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false),
                ParentsCount = table.Column<int>(type: "int", nullable: false),
                Additions = table.Column<int>(type: "int", nullable: true),
                Deletions = table.Column<int>(type: "int", nullable: true),
                ChangedFiles = table.Column<int>(type: "int", nullable: true),
                FirstSeenAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                LastSeenAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_github_commits", x => x.Id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "github_contributors",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                RepositoryLinkId = table.Column<Guid>(type: "char(36)", nullable: false),
                GitHubUserId = table.Column<long>(type: "bigint", nullable: false),
                Login = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                AvatarUrl = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true),
                ProfileUrl = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true),
                GitHubContributionCount = table.Column<int>(type: "int", nullable: false),
                ObservedCommitCount = table.Column<int>(type: "int", nullable: false),
                ObservedAdditions = table.Column<long>(type: "bigint", nullable: false),
                ObservedDeletions = table.Column<long>(type: "bigint", nullable: false),
                ObservedChangedFiles = table.Column<long>(type: "bigint", nullable: false),
                FirstCommitAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                LastCommitAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                LastSyncedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_github_contributors", x => x.Id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "github_pull_requests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                RepositoryLinkId = table.Column<Guid>(type: "char(36)", nullable: false),
                GitHubPullRequestId = table.Column<long>(type: "bigint", nullable: false),
                Number = table.Column<int>(type: "int", nullable: false),
                Title = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                Body = table.Column<string>(type: "longtext", nullable: true),
                State = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                IsDraft = table.Column<bool>(type: "tinyint(1)", nullable: false),
                IsMerged = table.Column<bool>(type: "tinyint(1)", nullable: false),
                AuthorGitHubId = table.Column<long>(type: "bigint", nullable: true),
                AuthorLogin = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                SourceBranch = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                SourceSha = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                TargetBranch = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                TargetSha = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                ClosedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                MergedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                MergeCommitSha = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                HtmlUrl = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false),
                Additions = table.Column<int>(type: "int", nullable: true),
                Deletions = table.Column<int>(type: "int", nullable: true),
                ChangedFiles = table.Column<int>(type: "int", nullable: true),
                CommitsCount = table.Column<int>(type: "int", nullable: true),
                CommentsCount = table.Column<int>(type: "int", nullable: true),
                ReviewCommentsCount = table.Column<int>(type: "int", nullable: true),
                LastSyncedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_github_pull_requests", x => x.Id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "github_pull_request_reviews",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                PullRequestId = table.Column<Guid>(type: "char(36)", nullable: false),
                GitHubReviewId = table.Column<long>(type: "bigint", nullable: false),
                ReviewerGitHubId = table.Column<long>(type: "bigint", nullable: true),
                ReviewerLogin = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                State = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                SubmittedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                LastSyncedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_github_pull_request_reviews", x => x.Id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "github_sync_runs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                ProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                RepositoryLinkId = table.Column<Guid>(type: "char(36)", nullable: false),
                Trigger = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                CommitsFetched = table.Column<int>(type: "int", nullable: false),
                ContributorsFetched = table.Column<int>(type: "int", nullable: false),
                PullRequestsFetched = table.Column<int>(type: "int", nullable: false),
                ReviewsFetched = table.Column<int>(type: "int", nullable: false),
                BranchesFetched = table.Column<int>(type: "int", nullable: false),
                ErrorCode = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                ErrorMessage = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_github_sync_runs", x => x.Id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateIndex("ux_github_branches_link_name", "github_branches", new[] { "RepositoryLinkId", "Name" }, unique: true);
        migrationBuilder.CreateIndex("ux_github_commits_link_sha", "github_commits", new[] { "RepositoryLinkId", "Sha" }, unique: true);
        migrationBuilder.CreateIndex("ix_github_commits_link_committed", "github_commits", new[] { "RepositoryLinkId", "CommittedAt" });
        migrationBuilder.CreateIndex("ux_github_contributors_link_user", "github_contributors", new[] { "RepositoryLinkId", "GitHubUserId" }, unique: true);
        migrationBuilder.CreateIndex("ix_github_contributors_link_login", "github_contributors", new[] { "RepositoryLinkId", "Login" });
        migrationBuilder.CreateIndex("ux_github_pull_requests_link_id", "github_pull_requests", new[] { "RepositoryLinkId", "GitHubPullRequestId" }, unique: true);
        migrationBuilder.CreateIndex("ux_github_pull_requests_link_number", "github_pull_requests", new[] { "RepositoryLinkId", "Number" }, unique: true);
        migrationBuilder.CreateIndex("ix_github_pull_requests_link_updated", "github_pull_requests", new[] { "RepositoryLinkId", "UpdatedAt" });
        migrationBuilder.CreateIndex("ux_github_reviews_pr_review", "github_pull_request_reviews", new[] { "PullRequestId", "GitHubReviewId" }, unique: true);
        migrationBuilder.CreateIndex("ix_github_sync_runs_link_started", "github_sync_runs", new[] { "RepositoryLinkId", "StartedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "github_branches");
        migrationBuilder.DropTable(name: "github_commits");
        migrationBuilder.DropTable(name: "github_contributors");
        migrationBuilder.DropTable(name: "github_pull_request_reviews");
        migrationBuilder.DropTable(name: "github_pull_requests");
        migrationBuilder.DropTable(name: "github_sync_runs");
        migrationBuilder.DropColumn(name: "LastSyncStartedAt", table: "project_repository_links");
        migrationBuilder.DropColumn(name: "LastFailedSyncAt", table: "project_repository_links");
        migrationBuilder.DropColumn(name: "LastSyncError", table: "project_repository_links");
        migrationBuilder.DropColumn(name: "LastKnownHeadSha", table: "project_repository_links");
    }
}
