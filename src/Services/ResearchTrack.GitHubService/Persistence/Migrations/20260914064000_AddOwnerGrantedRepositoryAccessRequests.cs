using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ResearchTrack.GitHubService.Persistence;

#nullable disable

namespace ResearchTrack.GitHubService.Persistence.Migrations;

[DbContext(typeof(GitHubDbContext))]
[Migration("20260914064000_AddOwnerGrantedRepositoryAccessRequests")]
public sealed class AddOwnerGrantedRepositoryAccessRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "github_repository_access_requests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                ProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                InitiatingUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                RequestedOwner = table.Column<string>(type: "varchar(39)", maxLength: 39, nullable: false),
                RequestedRepositoryName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                RequestedFullName = table.Column<string>(type: "varchar(140)", maxLength: 140, nullable: false),
                GitHubRepositoryId = table.Column<long>(type: "bigint", nullable: true),
                RequestTokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                FlowType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                PendingInstallationId = table.Column<long>(type: "bigint", nullable: true),
                AuthorizationStartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ConsumedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                FailureCode = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_github_repository_access_requests", x => x.Id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.AddColumn<Guid>(
            name: "RepositoryAccessRequestId",
            table: "github_installation_flow_states",
            type: "char(36)",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ux_github_repository_access_requests_token_hash",
            table: "github_repository_access_requests",
            column: "RequestTokenHash",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_github_repository_access_requests_status_expiry",
            table: "github_repository_access_requests",
            columns: new[] { "Status", "ExpiresAt" });
        migrationBuilder.CreateIndex(
            name: "ix_github_repository_access_requests_project_id",
            table: "github_repository_access_requests",
            column: "ProjectId");
        migrationBuilder.CreateIndex(
            name: "ix_github_installation_states_access_request",
            table: "github_installation_flow_states",
            column: "RepositoryAccessRequestId");
        migrationBuilder.AddForeignKey(
            name: "fk_github_installation_states_access_request",
            table: "github_installation_flow_states",
            column: "RepositoryAccessRequestId",
            principalTable: "github_repository_access_requests",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_github_installation_states_access_request",
            table: "github_installation_flow_states");
        migrationBuilder.DropIndex(
            name: "ix_github_installation_states_access_request",
            table: "github_installation_flow_states");
        migrationBuilder.DropColumn(
            name: "RepositoryAccessRequestId",
            table: "github_installation_flow_states");
        migrationBuilder.DropTable(name: "github_repository_access_requests");
    }
}
