using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.JiraService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJiraProjectLinking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "jira_connections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ResearchProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CloudId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    WorkspaceName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    WorkspaceUrl = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                    JiraProjectId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    JiraProjectKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    JiraProjectName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    JiraBoardId = table.Column<long>(type: "bigint", nullable: true),
                    JiraBoardName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                    JiraBoardType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    AccessTokenProtected = table.Column<string>(type: "text", nullable: false),
                    RefreshTokenProtected = table.Column<string>(type: "text", nullable: true),
                    TokenExpiresAt = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    Scope = table.Column<string>(type: "longtext", nullable: true),
                    ConnectedByUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    SyncStatus = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    LastSyncError = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jira_connections", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "jira_oauth_selections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    SelectionTokenHash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    ResearchProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SupervisorUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AccessTokenProtected = table.Column<string>(type: "text", nullable: false),
                    RefreshTokenProtected = table.Column<string>(type: "text", nullable: true),
                    TokenExpiresAt = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    Scope = table.Column<string>(type: "longtext", nullable: true),
                    WorkspacesJson = table.Column<string>(type: "text", nullable: false),
                    SelectedCloudId = table.Column<string>(type: "longtext", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jira_oauth_selections", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "jira_oauth_states",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    StateHash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    ResearchProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SupervisorUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jira_oauth_states", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_jira_connections_ResearchProjectId",
                table: "jira_connections",
                column: "ResearchProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_jira_oauth_selections_SelectionTokenHash",
                table: "jira_oauth_selections",
                column: "SelectionTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_jira_oauth_states_ResearchProjectId_SupervisorUserId",
                table: "jira_oauth_states",
                columns: new[] { "ResearchProjectId", "SupervisorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_jira_oauth_states_StateHash",
                table: "jira_oauth_states",
                column: "StateHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "jira_connections");

            migrationBuilder.DropTable(
                name: "jira_oauth_selections");

            migrationBuilder.DropTable(
                name: "jira_oauth_states");
        }
    }
}
