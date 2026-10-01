using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueProjectKeyPrefixPerWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_projects_workspace_id_key_prefix",
                table: "projects",
                columns: new[] { "workspace_id", "key_prefix" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_projects_workspace_id_key_prefix",
                table: "projects");
        }
    }
}
