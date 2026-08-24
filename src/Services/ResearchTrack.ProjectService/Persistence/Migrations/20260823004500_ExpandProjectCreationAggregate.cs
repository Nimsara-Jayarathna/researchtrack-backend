using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.ProjectService.Persistence.Migrations;

[DbContext(typeof(ProjectDbContext))]
[Migration("20260823004500_ExpandProjectCreationAggregate")]
public sealed class ExpandProjectCreationAggregate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "LeaderStudentUserId",
            table: "projects",
            type: "char(36)",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "MilestoneDate",
            table: "projects",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "LastActivityAt",
            table: "projects",
            type: "datetime(6)",
            nullable: false,
            defaultValueSql: "CURRENT_TIMESTAMP(6)");

        migrationBuilder.CreateTable(
            name: "project_members",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                ProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                MemberRole = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                FirstName = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                LastName = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                Email = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false),
                RegistrationNumber = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_project_members", x => x.Id);
                table.ForeignKey(
                    name: "FK_project_members_projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "project_milestones",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                ProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                Title = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                Description = table.Column<string>(type: "varchar(250)", maxLength: 250, nullable: true),
                DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                SequenceNo = table.Column<int>(type: "int", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_project_milestones", x => x.Id);
                table.ForeignKey(
                    name: "FK_project_milestones_projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "ix_project_members_project_id",
            table: "project_members",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "ux_project_members_project_user",
            table: "project_members",
            columns: new[] { "ProjectId", "UserId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_project_members_user_role",
            table: "project_members",
            columns: new[] { "UserId", "MemberRole" });

        migrationBuilder.CreateIndex(
            name: "ix_project_milestones_project_id",
            table: "project_milestones",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "ux_project_milestones_project_sequence",
            table: "project_milestones",
            columns: new[] { "ProjectId", "SequenceNo" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "project_milestones");
        migrationBuilder.DropTable(name: "project_members");
        migrationBuilder.DropColumn(name: "LeaderStudentUserId", table: "projects");
        migrationBuilder.DropColumn(name: "MilestoneDate", table: "projects");
        migrationBuilder.DropColumn(name: "LastActivityAt", table: "projects");
    }
}
