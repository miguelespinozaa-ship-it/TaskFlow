using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubRepositoryLinksAndCommits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "repository_commits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sha = table.Column<string>(type: "character(40)", fixedLength: true, maxLength: 40, nullable: false),
                    message = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    author_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    author_login = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: true),
                    author_avatar_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    committed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    additions = table.Column<int>(type: "integer", nullable: false),
                    deletions = table.Column<int>(type: "integer", nullable: false),
                    files_changed = table.Column<int>(type: "integer", nullable: false),
                    folders = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repository_commits", x => x.id);
                    table.ForeignKey(
                        name: "fk_repository_commits_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_repository_commits_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "repository_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    default_branch = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    etag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_synced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_sync_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repository_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_repository_links_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_repository_links_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_repository_commits_project_id_committed_at_id",
                table: "repository_commits",
                columns: new[] { "project_id", "committed_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_repository_commits_project_id_sha",
                table: "repository_commits",
                columns: new[] { "project_id", "sha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_repository_commits_workspace_id",
                table: "repository_commits",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_repository_links_project_id",
                table: "repository_links",
                column: "project_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_repository_links_workspace_id",
                table: "repository_links",
                column: "workspace_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repository_commits");

            migrationBuilder.DropTable(
                name: "repository_links");
        }
    }
}
