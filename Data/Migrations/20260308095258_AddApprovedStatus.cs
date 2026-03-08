using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OfficeTaskManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovedStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Shift existing statuses to accommodate Approved (1)
            migrationBuilder.Sql("UPDATE \"Tasks\" SET \"Status\" = \"Status\" + 1 WHERE \"Status\" >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Shift back to original values
            migrationBuilder.Sql("UPDATE \"Tasks\" SET \"Status\" = \"Status\" - 1 WHERE \"Status\" >= 2");
        }
    }
}
