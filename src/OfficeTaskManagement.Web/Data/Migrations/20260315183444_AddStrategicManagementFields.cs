using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OfficeTaskManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategicManagementFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPaused",
                table: "Tasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PauseReason",
                table: "Tasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PausedAt",
                table: "Tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PausedById",
                table: "Tasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOnExecutiveRadar",
                table: "Projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PlannedStartWeek",
                table: "Projects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StrategicStatus",
                table: "Projects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "StrategicStatusChangedAt",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StrategicStatusChangedById",
                table: "Projects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StrategicStatusReason",
                table: "Projects",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PortfolioDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DecisionType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: true),
                    TaskId = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Metadata = table.Column<string>(type: "text", nullable: true),
                    MadeById = table.Column<string>(type: "text", nullable: false),
                    MadeAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioDecisions_AspNetUsers_MadeById",
                        column: x => x.MadeById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PortfolioDecisions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_PausedById",
                table: "Tasks",
                column: "PausedById");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_StrategicStatusChangedById",
                table: "Projects",
                column: "StrategicStatusChangedById");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioDecisions_MadeById",
                table: "PortfolioDecisions",
                column: "MadeById");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioDecisions_ProjectId",
                table: "PortfolioDecisions",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_AspNetUsers_StrategicStatusChangedById",
                table: "Projects",
                column: "StrategicStatusChangedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_AspNetUsers_PausedById",
                table: "Tasks",
                column: "PausedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_AspNetUsers_StrategicStatusChangedById",
                table: "Projects");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_AspNetUsers_PausedById",
                table: "Tasks");

            migrationBuilder.DropTable(
                name: "PortfolioDecisions");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_PausedById",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Projects_StrategicStatusChangedById",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "IsPaused",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "PauseReason",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "PausedAt",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "PausedById",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "IsOnExecutiveRadar",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "PlannedStartWeek",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "StrategicStatus",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "StrategicStatusChangedAt",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "StrategicStatusChangedById",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "StrategicStatusReason",
                table: "Projects");
        }
    }
}
