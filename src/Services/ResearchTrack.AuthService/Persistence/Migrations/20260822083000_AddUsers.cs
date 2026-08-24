using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.AuthService.Persistence.Migrations;

[DbContext(typeof(AuthDbContext))]
[Migration("20260822083000_AddUsers")]
public sealed class AddUsers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false),
                Email = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false),
                FirstName = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                LastName = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                PasswordHash = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                Role = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                RegistrationNumber = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.Id);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "ux_users_email",
            table: "users",
            column: "Email",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_users_registration_number",
            table: "users",
            column: "RegistrationNumber",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "users");
    }
}
