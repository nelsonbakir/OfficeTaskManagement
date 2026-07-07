using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OfficeTaskManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddSprintAiPlanFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiGeneratedGoal",
                table: "Sprints",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AiPlanSessionId",
                table: "Sprints",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiGeneratedGoal",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "AiPlanSessionId",
                table: "Sprints");
        }
    }
}
