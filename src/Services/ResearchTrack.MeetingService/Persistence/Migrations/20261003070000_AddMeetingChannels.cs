using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.MeetingService.Persistence.Migrations;

public partial class AddMeetingChannels : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "meeting_channels",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                ProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                Platform = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                ChannelName = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                LinkOrIdentifier = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
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
                table.PrimaryKey("PK_meeting_channels", x => x.Id);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "ix_meeting_channels_project_id",
            table: "meeting_channels",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "ix_meeting_channels_project_status_created_at",
            table: "meeting_channels",
            columns: new[] { "ProjectId", "Status", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "meeting_channels");
    }
}
