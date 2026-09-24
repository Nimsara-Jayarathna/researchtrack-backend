using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace ResearchTrack.JiraService.Persistence.Migrations;

[DbContext(typeof(JiraDbContext))]
[Migration("20260923043100_AddJiraSyncRevision")]
public sealed class AddJiraSyncRevision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "SyncRevision", table: "jira_connections", type: "bigint", nullable: false, defaultValue: 0L);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SyncRevision", table: "jira_connections");
    }
}
