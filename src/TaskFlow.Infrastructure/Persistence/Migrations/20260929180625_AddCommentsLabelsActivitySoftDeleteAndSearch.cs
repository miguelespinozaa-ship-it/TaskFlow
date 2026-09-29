using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace TaskFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentsLabelsActivitySoftDeleteAndSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tasks_project_id_status_position",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_workspace_id_status",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_projects_workspace_id",
                table: "projects");

            migrationBuilder.AddColumn<Guid>(
                name: "assignee_id",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reporter_id",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "tasks",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('spanish', coalesce(title, '') || ' ' || coalesce(description, ''))",
                stored: true);

            migrationBuilder.CreateTable(
                name: "activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    changes = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activities", x => x.id);
                    table.ForeignKey(
                        name: "fk_activities_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    edited_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_comments_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_comments_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_comments_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "labels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_labels", x => x.id);
                    table.ForeignKey(
                        name: "fk_labels_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_labels",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_labels", x => new { x.task_id, x.label_id });
                    table.ForeignKey(
                        name: "fk_task_labels_labels_label_id",
                        column: x => x.label_id,
                        principalTable: "labels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_labels_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_assignee_id",
                table: "tasks",
                column: "assignee_id",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_project_id_status_position",
                table: "tasks",
                columns: new[] { "project_id", "status", "position" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_reporter_id",
                table: "tasks",
                column: "reporter_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_search_vector",
                table: "tasks",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_workspace_created_id",
                table: "tasks",
                columns: new[] { "workspace_id", "created_at", "id" },
                descending: new[] { false, true, true },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_workspace_id_status",
                table: "tasks",
                columns: new[] { "workspace_id", "status" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_projects_workspace_id",
                table: "projects",
                column: "workspace_id",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_activities_entity_id_created_at",
                table: "activities",
                columns: new[] { "entity_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_activities_workspace_id_created_at_id",
                table: "activities",
                columns: new[] { "workspace_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_comments_author_id",
                table: "comments",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_comments_task_id_created_at",
                table: "comments",
                columns: new[] { "task_id", "created_at" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_comments_workspace_id",
                table: "comments",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_labels_workspace_id_name",
                table: "labels",
                columns: new[] { "workspace_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_labels_label_id",
                table: "task_labels",
                column: "label_id");

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_users_assignee_id",
                table: "tasks",
                column: "assignee_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_users_reporter_id",
                table: "tasks",
                column: "reporter_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tasks_users_assignee_id",
                table: "tasks");

            migrationBuilder.DropForeignKey(
                name: "fk_tasks_users_reporter_id",
                table: "tasks");

            migrationBuilder.DropTable(
                name: "activities");

            migrationBuilder.DropTable(
                name: "comments");

            migrationBuilder.DropTable(
                name: "task_labels");

            migrationBuilder.DropTable(
                name: "labels");

            migrationBuilder.DropIndex(
                name: "ix_tasks_assignee_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_project_id_status_position",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_reporter_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_search_vector",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_workspace_created_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_workspace_id_status",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_projects_workspace_id",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "assignee_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "reporter_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "projects");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_project_id_status_position",
                table: "tasks",
                columns: new[] { "project_id", "status", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_workspace_id_status",
                table: "tasks",
                columns: new[] { "workspace_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_projects_workspace_id",
                table: "projects",
                column: "workspace_id");
        }
    }
}
