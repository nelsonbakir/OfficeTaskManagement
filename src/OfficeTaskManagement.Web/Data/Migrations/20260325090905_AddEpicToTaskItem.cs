using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OfficeTaskManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEpicToTaskItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EpicId",
                table: "Tasks",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_EpicId",
                table: "Tasks",
                column: "EpicId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Epics_EpicId",
                table: "Tasks",
                column: "EpicId",
                principalTable: "Epics",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Epics_EpicId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_EpicId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "EpicId",
                table: "Tasks");
        }
    }
}
