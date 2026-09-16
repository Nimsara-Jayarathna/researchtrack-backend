using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.GitHubService.Persistence.Migrations
{
    [DbContext(typeof(GitHubDbContext))]
    [Migration("20260916080000_AddPullRequestMergeActor")]
    public partial class AddPullRequestMergeActor : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MergedByGitHubId",
                table: "github_pull_requests",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MergedByLogin",
                table: "github_pull_requests",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MergedByGitHubId",
                table: "github_pull_requests");

            migrationBuilder.DropColumn(
                name: "MergedByLogin",
                table: "github_pull_requests");
        }
    }
}
