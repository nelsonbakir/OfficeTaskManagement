using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OfficeTaskManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateTaskStatusEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Safely map existing integers (0-3) to the new 6-tier (0-5) mappings
            migrationBuilder.Sql("UPDATE \"Tasks\" SET \"Status\" = 5 WHERE \"Status\" = 3;"); // Old Done -> New Done
            migrationBuilder.Sql("UPDATE \"Tasks\" SET \"Status\" = 4 WHERE \"Status\" = 2;"); // Old Tested -> New Tested
            migrationBuilder.Sql("UPDATE \"Tasks\" SET \"Status\" = 2 WHERE \"Status\" = 1;"); // Old InProgress -> New InProgress
            migrationBuilder.Sql("UPDATE \"Tasks\" SET \"Status\" = 1 WHERE \"Status\" = 0;"); // Old ToDo -> New ToDo
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert mapped integers
            migrationBuilder.Sql("UPDATE \"Tasks\" SET \"Status\" = 0 WHERE \"Status\" = 1;"); // New ToDo -> Old ToDo
            migrationBuilder.Sql("UPDATE \"Tasks\" SET \"Status\" = 1 WHERE \"Status\" = 2;"); // New InProgress -> Old InProgress
            migrationBuilder.Sql("UPDATE \"Tasks\" SET \"Status\" = 2 WHERE \"Status\" = 4;"); // New Tested -> Old Tested
            migrationBuilder.Sql("UPDATE \"Tasks\" SET \"Status\" = 3 WHERE \"Status\" = 5;"); // New Done -> Old Done
        }
    }
}
