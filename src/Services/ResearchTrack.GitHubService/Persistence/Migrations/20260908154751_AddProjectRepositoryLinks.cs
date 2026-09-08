using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResearchTrack.GitHubService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectRepositoryLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_repository_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SourceId = table.Column<Guid>(type: "char(36)", nullable: false),
                    GitHubRepositoryId = table.Column<Guid>(type: "char(36)", nullable: false),
                    GitHubRepoId = table.Column<long>(type: "bigint", nullable: false),
                    LinkedByUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AccessType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    FullName = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    Name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    CustomName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                    OwnerLogin = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    DefaultBranch = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                    Url = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false),
                    Active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Primary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ActiveRepositoryKey = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: true),
                    PrimaryProjectKey = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    LinkedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SyncStatus = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_repository_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_repository_links_github_access_sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "github_access_sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_project_repository_links_github_repositories_GitHubRepositor~",
                        column: x => x.GitHubRepositoryId,
                        principalTable: "github_repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_project_repository_links_GitHubRepositoryId",
                table: "project_repository_links",
                column: "GitHubRepositoryId");

            migrationBuilder.CreateIndex(
                name: "ix_project_repository_links_project_active",
                table: "project_repository_links",
                columns: new[] { "ProjectId", "Active" });

            migrationBuilder.CreateIndex(
                name: "ix_project_repository_links_source_id",
                table: "project_repository_links",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "ux_project_repository_links_active_repository",
                table: "project_repository_links",
                column: "ActiveRepositoryKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_project_repository_links_primary_project",
                table: "project_repository_links",
                column: "PrimaryProjectKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_repository_links");
        }
    }
}
