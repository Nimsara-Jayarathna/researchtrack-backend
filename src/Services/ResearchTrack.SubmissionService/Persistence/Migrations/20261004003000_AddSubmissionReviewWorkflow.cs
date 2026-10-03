using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.SubmissionService.Persistence.Migrations;

public partial class AddSubmissionReviewWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "submission_reviews",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                SubmissionId = table.Column<Guid>(type: "char(36)", nullable: false),
                VersionId = table.Column<Guid>(type: "char(36)", nullable: false),
                Decision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                Feedback = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                ReviewedBy = table.Column<Guid>(type: "char(36)", nullable: false),
                ReviewedByName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                ReviewedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_submission_reviews", x => x.Id);
                table.ForeignKey("fk_review_submission", x => x.SubmissionId, "research_submissions", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_review_version", x => x.VersionId, "submission_versions", "Id", onDelete: ReferentialAction.Restrict);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "submission_comments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                SubmissionId = table.Column<Guid>(type: "char(36)", nullable: false),
                VersionId = table.Column<Guid>(type: "char(36)", nullable: true),
                AuthorId = table.Column<Guid>(type: "char(36)", nullable: false),
                AuthorName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                AuthorRole = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                Comment = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_submission_comments", x => x.Id);
                table.ForeignKey("fk_comment_submission", x => x.SubmissionId, "research_submissions", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_comment_version", x => x.VersionId, "submission_versions", "Id", onDelete: ReferentialAction.Restrict);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "ix_submission_reviews_submission_time",
            table: "submission_reviews",
            columns: new[] { "SubmissionId", "ReviewedAt" });
        migrationBuilder.CreateIndex(
            name: "ux_submission_reviews_version",
            table: "submission_reviews",
            column: "VersionId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_submission_comments_submission_time",
            table: "submission_comments",
            columns: new[] { "SubmissionId", "CreatedAt" });
        migrationBuilder.CreateIndex(
            name: "ix_submission_comments_version",
            table: "submission_comments",
            column: "VersionId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "submission_comments");
        migrationBuilder.DropTable(name: "submission_reviews");
    }
}
