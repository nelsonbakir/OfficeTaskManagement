using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OfficeTaskManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddFromDateToDateToPublicHolidays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Date",
                table: "PublicHolidays",
                newName: "ToDate");

            migrationBuilder.AddColumn<DateTime>(
                name: "FromDate",
                table: "PublicHolidays",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FromDate",
                table: "PublicHolidays");

            migrationBuilder.RenameColumn(
                name: "ToDate",
                table: "PublicHolidays",
                newName: "Date");
        }
    }
}
