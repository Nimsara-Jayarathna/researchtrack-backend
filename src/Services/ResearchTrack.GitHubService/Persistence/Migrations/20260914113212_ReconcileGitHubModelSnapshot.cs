using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.GitHubService.Persistence.Migrations
{
    // The preceding handwritten migrations already created the owner-request and sync schema.
    // EF generated this migration's designer/snapshot from the current model. Do not recreate
    // those tables: only reconcile their existing index names with the model conventions.
    public partial class ReconcileGitHubModelSnapshot : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(name: "ux_github_branches_link_name", table: "github_branches", newName: "IX_github_branches_RepositoryLinkId_Name");
            migrationBuilder.RenameIndex(name: "ux_github_commits_link_sha", table: "github_commits", newName: "IX_github_commits_RepositoryLinkId_Sha");
            migrationBuilder.RenameIndex(name: "ix_github_commits_link_committed", table: "github_commits", newName: "IX_github_commits_RepositoryLinkId_CommittedAt");
            migrationBuilder.RenameIndex(name: "ux_github_contributors_link_user", table: "github_contributors", newName: "IX_github_contributors_RepositoryLinkId_GitHubUserId");
            migrationBuilder.RenameIndex(name: "ix_github_contributors_link_login", table: "github_contributors", newName: "IX_github_contributors_RepositoryLinkId_Login");
            migrationBuilder.RenameIndex(name: "ux_github_pull_requests_link_id", table: "github_pull_requests", newName: "IX_github_pull_requests_RepositoryLinkId_GitHubPullRequestId");
            migrationBuilder.RenameIndex(name: "ux_github_pull_requests_link_number", table: "github_pull_requests", newName: "IX_github_pull_requests_RepositoryLinkId_Number");
            migrationBuilder.RenameIndex(name: "ix_github_pull_requests_link_updated", table: "github_pull_requests", newName: "IX_github_pull_requests_RepositoryLinkId_UpdatedAt");
            migrationBuilder.RenameIndex(name: "ux_github_reviews_pr_review", table: "github_pull_request_reviews", newName: "IX_github_pull_request_reviews_PullRequestId_GitHubReviewId");
            migrationBuilder.RenameIndex(name: "ix_github_sync_runs_link_started", table: "github_sync_runs", newName: "IX_github_sync_runs_RepositoryLinkId_StartedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(name: "IX_github_sync_runs_RepositoryLinkId_StartedAt", table: "github_sync_runs", newName: "ix_github_sync_runs_link_started");
            migrationBuilder.RenameIndex(name: "IX_github_pull_request_reviews_PullRequestId_GitHubReviewId", table: "github_pull_request_reviews", newName: "ux_github_reviews_pr_review");
            migrationBuilder.RenameIndex(name: "IX_github_pull_requests_RepositoryLinkId_UpdatedAt", table: "github_pull_requests", newName: "ix_github_pull_requests_link_updated");
            migrationBuilder.RenameIndex(name: "IX_github_pull_requests_RepositoryLinkId_Number", table: "github_pull_requests", newName: "ux_github_pull_requests_link_number");
            migrationBuilder.RenameIndex(name: "IX_github_pull_requests_RepositoryLinkId_GitHubPullRequestId", table: "github_pull_requests", newName: "ux_github_pull_requests_link_id");
            migrationBuilder.RenameIndex(name: "IX_github_contributors_RepositoryLinkId_Login", table: "github_contributors", newName: "ix_github_contributors_link_login");
            migrationBuilder.RenameIndex(name: "IX_github_contributors_RepositoryLinkId_GitHubUserId", table: "github_contributors", newName: "ux_github_contributors_link_user");
            migrationBuilder.RenameIndex(name: "IX_github_commits_RepositoryLinkId_CommittedAt", table: "github_commits", newName: "ix_github_commits_link_committed");
            migrationBuilder.RenameIndex(name: "IX_github_commits_RepositoryLinkId_Sha", table: "github_commits", newName: "ux_github_commits_link_sha");
            migrationBuilder.RenameIndex(name: "IX_github_branches_RepositoryLinkId_Name", table: "github_branches", newName: "ux_github_branches_link_name");
        }
    }
}
