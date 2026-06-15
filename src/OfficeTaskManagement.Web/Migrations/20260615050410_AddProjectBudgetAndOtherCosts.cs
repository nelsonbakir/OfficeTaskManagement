using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OfficeTaskManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectBudgetAndOtherCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ApprovedBudget",
                table: "Projects",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BudgetMode",
                table: "Projects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "BudgetSetAt",
                table: "Projects",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BudgetSetById",
                table: "Projects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ContingencyReserve",
                table: "Projects",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectOtherCosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "text", nullable: false, defaultValue: "default-tenant-id"),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Frequency = table.Column<int>(type: "integer", nullable: false),
                    EstimatedAmount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    ActualAmount = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    PlannedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ActualDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsContingency = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedById = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectOtherCosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectOtherCosts_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProjectOtherCosts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_BudgetSetById",
                table: "Projects",
                column: "BudgetSetById");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectOtherCosts_CreatedById",
                table: "ProjectOtherCosts",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectOtherCosts_ProjectId",
                table: "ProjectOtherCosts",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_AspNetUsers_BudgetSetById",
                table: "Projects",
                column: "BudgetSetById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_AspNetUsers_BudgetSetById",
                table: "Projects");

            migrationBuilder.DropTable(
                name: "ProjectOtherCosts");

            migrationBuilder.DropIndex(
                name: "IX_Projects_BudgetSetById",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ApprovedBudget",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "BudgetMode",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "BudgetSetAt",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "BudgetSetById",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ContingencyReserve",
                table: "Projects");
        }
    }
}
