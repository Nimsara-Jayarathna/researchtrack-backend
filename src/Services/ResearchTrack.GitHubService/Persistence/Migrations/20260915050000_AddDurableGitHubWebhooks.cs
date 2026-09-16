using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.GitHubService.Persistence.Migrations
{
    [DbContext(typeof(GitHubDbContext))]
    [Migration("20260915050000_AddDurableGitHubWebhooks")]
    public partial class AddDurableGitHubWebhooks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConnectionStatus",
                table: "github_access_sources",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "CONNECTED");

            migrationBuilder.AddColumn<bool>(
                name: "Available",
                table: "github_repositories",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAt",
                table: "github_repositories",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.Sql("UPDATE github_repositories SET LastSeenAt = UpdatedAt WHERE Available = 1 AND LastSeenAt IS NULL");

            migrationBuilder.CreateTable(
                name: "github_webhook_deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    DeliveryId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    InstallationId = table.Column<long>(type: "bigint", nullable: true),
                    GitHubRepositoryId = table.Column<long>(type: "bigint", nullable: true),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false),
                    PayloadSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ProcessingStartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastError = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_github_webhook_deliveries", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "ux_github_webhook_deliveries_delivery_id",
                table: "github_webhook_deliveries",
                column: "DeliveryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_github_webhook_deliveries_status_next_attempt",
                table: "github_webhook_deliveries",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "ix_github_webhook_deliveries_received_at",
                table: "github_webhook_deliveries",
                column: "ReceivedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "github_webhook_deliveries");
            migrationBuilder.DropColumn(name: "ConnectionStatus", table: "github_access_sources");
            migrationBuilder.DropColumn(name: "Available", table: "github_repositories");
            migrationBuilder.DropColumn(name: "LastSeenAt", table: "github_repositories");
        }
    }
}
