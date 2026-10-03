using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.MeetingService.Persistence.Migrations;

public partial class AddMeetingRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "meeting_records",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                ProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                MeetingDate = table.Column<DateTime>(type: "date", nullable: false),
                DurationMinutes = table.Column<int>(type: "int", nullable: false),
                DiscussionSummary = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                DiscussionDetails = table.Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: true),
                ChannelId = table.Column<Guid>(type: "char(36)", nullable: true),
                AddedBy = table.Column<Guid>(type: "char(36)", nullable: false),
                AddedByName = table.Column<string>(type: "varchar(201)", maxLength: 201, nullable: false),
                AddedByRole = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                ApprovedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                ApprovedByName = table.Column<string>(type: "varchar(201)", maxLength: 201, nullable: true),
                ApprovedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_meeting_records", x => x.Id);
                table.ForeignKey(
                    name: "FK_meeting_records_meeting_channels_ChannelId",
                    column: x => x.ChannelId,
                    principalTable: "meeting_channels",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "ix_meeting_records_channel_id",
            table: "meeting_records",
            column: "ChannelId");

        migrationBuilder.CreateIndex(
            name: "ix_meeting_records_project_id",
            table: "meeting_records",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "ix_meeting_records_project_status_date_created_at",
            table: "meeting_records",
            columns: new[] { "ProjectId", "Status", "MeetingDate", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "meeting_records");
    }
}
