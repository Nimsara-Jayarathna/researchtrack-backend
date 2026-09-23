using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace ResearchTrack.GitHubService.Persistence.Migrations;

[DbContext(typeof(GitHubDbContext))]
[Migration("20260923043000_AddGitHubSyncRevision")]
public sealed class AddGitHubSyncRevision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "SyncRevision", table: "project_repository_links", type: "bigint", nullable: false, defaultValue: 0L);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SyncRevision", table: "project_repository_links");
    }
}
