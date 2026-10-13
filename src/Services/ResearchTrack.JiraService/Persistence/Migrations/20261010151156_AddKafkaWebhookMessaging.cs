using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace ResearchTrack.JiraService.Persistence.Migrations;

public partial class AddKafkaWebhookMessaging : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
            migrationBuilder.CreateTable(
                name: "kafka_inbox_receipts",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayloadHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    HandedOffAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kafka_inbox_receipts", x => x.EventId);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "kafka_outbox_messages",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    EventId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Topic = table.Column<string>(type: "varchar(249)", maxLength: 249, nullable: false),
                    MessageKey = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LeaseId = table.Column<Guid>(type: "char(36)", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastErrorCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kafka_outbox_messages", x => x.Sequence);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_kafka_outbox_messages_EventId",
                table: "kafka_outbox_messages",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kafka_outbox_messages_MessageKey_Sequence",
                table: "kafka_outbox_messages",
                columns: new[] { "MessageKey", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_kafka_outbox_messages_Status_NextAttemptAtUtc",
                table: "kafka_outbox_messages",
                columns: new[] { "Status", "NextAttemptAtUtc" });
        }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "kafka_inbox_receipts");
        migrationBuilder.DropTable(name: "kafka_outbox_messages");
    }
}
