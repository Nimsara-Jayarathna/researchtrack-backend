using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.GitHubService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicAccessSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "github_access_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    InstallationId = table.Column<long>(type: "bigint", nullable: true),
                    OwnerLogin = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    OwnerType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    AccessType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ActiveRepositoryKey = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_github_access_sources", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "github_repositories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    SourceId = table.Column<Guid>(type: "char(36)", nullable: false),
                    GitHubRepositoryId = table.Column<long>(type: "bigint", nullable: false),
                    FullName = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    Name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    OwnerLogin = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    DefaultBranch = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                    Url = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_github_repositories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_github_repositories_github_access_sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "github_access_sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_github_access_sources_project_id",
                table: "github_access_sources",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "ux_github_access_sources_active_repository",
                table: "github_access_sources",
                column: "ActiveRepositoryKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_github_repositories_github_id",
                table: "github_repositories",
                column: "GitHubRepositoryId");

            migrationBuilder.CreateIndex(
                name: "ux_github_repositories_source_github_id",
                table: "github_repositories",
                columns: new[] { "SourceId", "GitHubRepositoryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "github_repositories");

            migrationBuilder.DropTable(
                name: "github_access_sources");
        }
    }
}
