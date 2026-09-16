using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.GitHubService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubInstallationFlowStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActiveInstallationKey",
                table: "github_access_sources",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_github_access_sources_active_installation",
                table: "github_access_sources",
                column: "ActiveInstallationKey",
                unique: true);

            migrationBuilder.CreateTable(
                name: "github_installation_flow_states",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    StateHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                    InitiatingUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    FlowType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    ReturnPath = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_github_installation_flow_states", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_github_installation_flow_states_expires_at",
                table: "github_installation_flow_states",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "ix_github_installation_flow_states_project_user",
                table: "github_installation_flow_states",
                columns: new[] { "ProjectId", "InitiatingUserId" });

            migrationBuilder.CreateIndex(
                name: "ux_github_installation_flow_states_hash",
                table: "github_installation_flow_states",
                column: "StateHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "github_installation_flow_states");

            migrationBuilder.DropIndex(
                name: "ux_github_access_sources_active_installation",
                table: "github_access_sources");

            migrationBuilder.DropColumn(
                name: "ActiveInstallationKey",
                table: "github_access_sources");
        }
    }
}
