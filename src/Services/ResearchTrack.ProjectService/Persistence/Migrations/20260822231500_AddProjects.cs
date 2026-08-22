using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.ProjectService.Persistence.Migrations;

[DbContext(typeof(ProjectDbContext))]
[Migration("20260822231500_AddProjects")]
public sealed class AddProjects : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                Title = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                Summary = table.Column<string>(type: "varchar(250)", maxLength: 250, nullable: false),
                Batch = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                Semester = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                LifecycleStatus = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                ProgressPercent = table.Column<int>(type: "int", nullable: false),
                SupervisorUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_projects", x => x.Id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "ix_projects_supervisor_user_id",
            table: "projects",
            column: "SupervisorUserId");
        migrationBuilder.CreateIndex(
            name: "ix_projects_supervisor_created_at",
            table: "projects",
            columns: new[] { "SupervisorUserId", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "projects");
    }
}
