using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.GitHubService.Persistence.Migrations
{
    [DbContext(typeof(GitHubDbContext))]
    [Migration("20260914150000_SimplifyGitHubAppAccessSources")]
    public partial class SimplifyGitHubAppAccessSources : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove legacy URL-only access-source data before dropping its uniqueness column.
            // These rows are identifiable by the absence of a GitHub App installation id and
            // the presence of the repository-scoped active key used only by that legacy flow.
            migrationBuilder.Sql("""
                DELETE FROM github_pull_request_reviews
                WHERE PullRequestId IN (
                    SELECT Id FROM github_pull_requests
                    WHERE RepositoryLinkId IN (
                        SELECT Id FROM project_repository_links
                        WHERE SourceId IN (
                            SELECT Id FROM github_access_sources
                            WHERE InstallationId IS NULL AND ActiveRepositoryKey IS NOT NULL
                        )
                    )
                )
                """);

            migrationBuilder.Sql("""
                DELETE FROM github_branches
                WHERE RepositoryLinkId IN (
                    SELECT Id FROM project_repository_links
                    WHERE SourceId IN (
                        SELECT Id FROM github_access_sources
                        WHERE InstallationId IS NULL AND ActiveRepositoryKey IS NOT NULL
                    )
                )
                """);

            migrationBuilder.Sql("""
                DELETE FROM github_commits
                WHERE RepositoryLinkId IN (
                    SELECT Id FROM project_repository_links
                    WHERE SourceId IN (
                        SELECT Id FROM github_access_sources
                        WHERE InstallationId IS NULL AND ActiveRepositoryKey IS NOT NULL
                    )
                )
                """);

            migrationBuilder.Sql("""
                DELETE FROM github_contributors
                WHERE RepositoryLinkId IN (
                    SELECT Id FROM project_repository_links
                    WHERE SourceId IN (
                        SELECT Id FROM github_access_sources
                        WHERE InstallationId IS NULL AND ActiveRepositoryKey IS NOT NULL
                    )
                )
                """);

            migrationBuilder.Sql("""
                DELETE FROM github_pull_requests
                WHERE RepositoryLinkId IN (
                    SELECT Id FROM project_repository_links
                    WHERE SourceId IN (
                        SELECT Id FROM github_access_sources
                        WHERE InstallationId IS NULL AND ActiveRepositoryKey IS NOT NULL
                    )
                )
                """);

            migrationBuilder.Sql("""
                DELETE FROM github_sync_runs
                WHERE RepositoryLinkId IN (
                    SELECT Id FROM project_repository_links
                    WHERE SourceId IN (
                        SELECT Id FROM github_access_sources
                        WHERE InstallationId IS NULL AND ActiveRepositoryKey IS NOT NULL
                    )
                )
                """);

            migrationBuilder.Sql("""
                DELETE FROM project_repository_links
                WHERE SourceId IN (
                    SELECT Id FROM github_access_sources
                    WHERE InstallationId IS NULL AND ActiveRepositoryKey IS NOT NULL
                )
                """);

            migrationBuilder.Sql("""
                DELETE FROM github_access_sources
                WHERE InstallationId IS NULL AND ActiveRepositoryKey IS NOT NULL
                """);

            migrationBuilder.DropIndex(
                name: "ux_github_access_sources_active_repository",
                table: "github_access_sources");

            migrationBuilder.DropColumn(
                name: "ActiveRepositoryKey",
                table: "github_access_sources");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActiveRepositoryKey",
                table: "github_access_sources",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_github_access_sources_active_repository",
                table: "github_access_sources",
                column: "ActiveRepositoryKey",
                unique: true);
        }
    }
}
