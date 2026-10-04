using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.SubmissionService.Persistence.Migrations;

public partial class AddSubmissionResponsibility : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "AssignedStudentId",
            table: "submission_requirements",
            type: "char(36)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AssignedStudentName",
            table: "submission_requirements",
            type: "varchar(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ResponsibilityMode",
            table: "submission_requirements",
            type: "varchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "PROJECT_LEADER");

        migrationBuilder.AddColumn<string>(
            name: "ResponsibilityModeSnapshot",
            table: "submission_versions",
            type: "varchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SubmitterRoleSnapshot",
            table: "submission_versions",
            type: "varchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_submission_requirements_project_assignee",
            table: "submission_requirements",
            columns: new[] { "ProjectId", "AssignedStudentId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_submission_requirements_project_assignee",
            table: "submission_requirements");

        migrationBuilder.DropColumn(name: "AssignedStudentId", table: "submission_requirements");
        migrationBuilder.DropColumn(name: "AssignedStudentName", table: "submission_requirements");
        migrationBuilder.DropColumn(name: "ResponsibilityMode", table: "submission_requirements");
        migrationBuilder.DropColumn(name: "ResponsibilityModeSnapshot", table: "submission_versions");
        migrationBuilder.DropColumn(name: "SubmitterRoleSnapshot", table: "submission_versions");
    }
}
