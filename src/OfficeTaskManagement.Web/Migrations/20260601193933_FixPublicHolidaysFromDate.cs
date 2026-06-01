using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OfficeTaskManagement.Migrations
{
    /// <inheritdoc />
    public partial class FixPublicHolidaysFromDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"PublicHolidays\" SET \"FromDate\" = \"ToDate\" WHERE \"FromDate\" = '-infinity';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
