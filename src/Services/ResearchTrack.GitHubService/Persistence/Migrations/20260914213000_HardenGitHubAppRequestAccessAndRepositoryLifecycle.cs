using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.GitHubService.Persistence.Migrations
{
    [DbContext(typeof(GitHubDbContext))]
    [Migration("20260914213000_HardenGitHubAppRequestAccessAndRepositoryLifecycle")]
    public partial class HardenGitHubAppRequestAccessAndRepositoryLifecycle : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_project_repository_links_active_repository",
                table: "project_repository_links");

            migrationBuilder.DropColumn(
                name: "ActiveRepositoryKey",
                table: "project_repository_links");

            migrationBuilder.AddColumn<Guid>(
                name: "AccessRequestId",
                table: "github_installation_flow_states",
                type: "char(36)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "github_access_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ProjectTitle = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    TargetOwnerLogin = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    TokenNonce = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ResultNonce = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    ResultTokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    PendingProjectKey = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    SourceId = table.Column<Guid>(type: "char(36)", nullable: true),
                    InstallationId = table.Column<long>(type: "bigint", nullable: true),
                    ErrorCode = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AuthorizationStartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_github_access_requests", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "ix_github_installation_flow_states_access_request",
                table: "github_installation_flow_states",
                column: "AccessRequestId");

            migrationBuilder.CreateIndex(
                name: "ix_github_access_requests_expires_at",
                table: "github_access_requests",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "ix_github_access_requests_project_status",
                table: "github_access_requests",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ux_github_access_requests_pending_project",
                table: "github_access_requests",
                column: "PendingProjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_github_access_requests_result_token_hash",
                table: "github_access_requests",
                column: "ResultTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_github_access_requests_token_hash",
                table: "github_access_requests",
                column: "TokenHash",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "github_access_requests");

            migrationBuilder.DropIndex(
                name: "ix_github_installation_flow_states_access_request",
                table: "github_installation_flow_states");

            migrationBuilder.DropColumn(
                name: "AccessRequestId",
                table: "github_installation_flow_states");

            migrationBuilder.AddColumn<string>(
                name: "ActiveRepositoryKey",
                table: "project_repository_links",
                type: "varchar(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_project_repository_links_active_repository",
                table: "project_repository_links",
                column: "ActiveRepositoryKey",
                unique: true);
        }
    }
}
