using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OfficeTaskManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleIdToWorkflowStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RoleId",
                table: "WorkflowStages",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStages_RoleId",
                table: "WorkflowStages",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowStages_AspNetRoles_RoleId",
                table: "WorkflowStages",
                column: "RoleId",
                principalTable: "AspNetRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowStages_AspNetRoles_RoleId",
                table: "WorkflowStages");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowStages_RoleId",
                table: "WorkflowStages");

            migrationBuilder.DropColumn(
                name: "RoleId",
                table: "WorkflowStages");
        }
    }
}
