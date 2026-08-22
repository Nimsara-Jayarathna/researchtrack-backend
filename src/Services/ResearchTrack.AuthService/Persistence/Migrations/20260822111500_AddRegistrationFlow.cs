using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.AuthService.Persistence.Migrations;

[DbContext(typeof(AuthDbContext))]
[Migration("20260822111500_AddRegistrationFlow")]
public sealed class AddRegistrationFlow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "email_otps",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                Email = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false),
                OtpHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UsedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_email_otps", x => x.Id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "registration_sessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                TokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                Email = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false),
                Role = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UsedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_registration_sessions", x => x.Id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "refresh_tokens",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                TokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                RevokedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                table.ForeignKey("FK_refresh_tokens_users_UserId", x => x.UserId, "users", "Id", onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateIndex("ix_email_otps_email", "email_otps", "Email");
        migrationBuilder.CreateIndex("ix_email_otps_expires_at", "email_otps", "ExpiresAt");
        migrationBuilder.CreateIndex("ux_registration_sessions_token_hash", "registration_sessions", "TokenHash", unique: true);
        migrationBuilder.CreateIndex("ix_registration_sessions_expires_at", "registration_sessions", "ExpiresAt");
        migrationBuilder.CreateIndex("ux_refresh_tokens_token_hash", "refresh_tokens", "TokenHash", unique: true);
        migrationBuilder.CreateIndex("ix_refresh_tokens_user_id", "refresh_tokens", "UserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("email_otps");
        migrationBuilder.DropTable("registration_sessions");
        migrationBuilder.DropTable("refresh_tokens");
    }
}
