using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.SubmissionService.Persistence.Migrations;

public partial class AddSubmissionFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "submission_requirements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                ProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                DueAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                AllowedFileTypes = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                MaxFileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                CreatedBy = table.Column<Guid>(type: "char(36)", nullable: false),
                CreatedByName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_submission_requirements", x => x.Id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "submission_upload_sessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                ProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                RequirementId = table.Column<Guid>(type: "char(36)", nullable: false),
                SubmissionId = table.Column<Guid>(type: "char(36)", nullable: false),
                VersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                ExpectedVersionNumber = table.Column<int>(type: "int", nullable: false),
                TemporaryObjectKey = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                FinalObjectKey = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                OriginalFileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                FileExtension = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                ExpectedContentType = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                DeclaredFileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                ExpectedMaxFileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                SubmissionNote = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                CreatedBy = table.Column<Guid>(type: "char(36)", nullable: false),
                CreatedByName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                ActiveSlot = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                ExpiresAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                FailureReason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_submission_upload_sessions", x => x.Id);
                table.ForeignKey("fk_upload_session_requirement", x => x.RequirementId, "submission_requirements", "Id", onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "research_submissions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                ProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                RequirementId = table.Column<Guid>(type: "char(36)", nullable: false),
                Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                CurrentVersionId = table.Column<Guid>(type: "char(36)", nullable: true),
                VersionCount = table.Column<int>(type: "int", nullable: false),
                LastSubmittedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                ApprovedVersionId = table.Column<Guid>(type: "char(36)", nullable: true),
                ApprovedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_research_submissions", x => x.Id);
                table.ForeignKey("fk_research_submission_requirement", x => x.RequirementId, "submission_requirements", "Id", onDelete: ReferentialAction.Restrict);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "submission_versions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                SubmissionId = table.Column<Guid>(type: "char(36)", nullable: false),
                VersionNumber = table.Column<int>(type: "int", nullable: false),
                ObjectKey = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                OriginalFileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                FileExtension = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                ContentType = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                ObjectETag = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                UploadedBy = table.Column<Guid>(type: "char(36)", nullable: false),
                UploadedByName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                SubmissionNote = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                SubmittedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                IsLate = table.Column<bool>(type: "tinyint(1)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_submission_versions", x => x.Id);
                table.ForeignKey("fk_submission_version_submission", x => x.SubmissionId, "research_submissions", "Id", onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateIndex("ix_submission_requirements_project_status", "submission_requirements", new[] { "ProjectId", "Status" });
        migrationBuilder.CreateIndex("ix_submission_requirements_project_due_at", "submission_requirements", new[] { "ProjectId", "DueAt" });
        migrationBuilder.CreateIndex("ix_submission_upload_sessions_project_requirement_status", "submission_upload_sessions", new[] { "ProjectId", "RequirementId", "Status" });
        migrationBuilder.CreateIndex("ix_submission_upload_sessions_requirement_id", "submission_upload_sessions", "RequirementId");
        migrationBuilder.CreateIndex("ix_submission_upload_sessions_expires_at", "submission_upload_sessions", "ExpiresAt");
        migrationBuilder.CreateIndex("ux_submission_upload_sessions_active_version", "submission_upload_sessions", new[] { "ProjectId", "RequirementId", "ExpectedVersionNumber", "ActiveSlot" }, unique: true);
        migrationBuilder.CreateIndex("ix_research_submissions_project_status", "research_submissions", new[] { "ProjectId", "Status" });
        migrationBuilder.CreateIndex("ix_research_submissions_requirement_id", "research_submissions", "RequirementId");
        migrationBuilder.CreateIndex("ux_research_submissions_project_requirement", "research_submissions", new[] { "ProjectId", "RequirementId" }, unique: true);
        migrationBuilder.CreateIndex("ux_submission_versions_submission_version", "submission_versions", new[] { "SubmissionId", "VersionNumber" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("submission_upload_sessions");
        migrationBuilder.DropTable("submission_versions");
        migrationBuilder.DropTable("research_submissions");
        migrationBuilder.DropTable("submission_requirements");
    }
}
