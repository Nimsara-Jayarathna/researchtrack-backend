using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.GitHubService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenGitHubInstallationIdentityBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AuthorizationStartedAt",
                table: "github_installation_flow_states",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PendingInstallationId",
                table: "github_installation_flow_states",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthorizationStartedAt",
                table: "github_installation_flow_states");

            migrationBuilder.DropColumn(
                name: "PendingInstallationId",
                table: "github_installation_flow_states");
        }
    }
}
