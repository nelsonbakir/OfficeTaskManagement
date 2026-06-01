using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OfficeTaskManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddSalaryHistoryAndResourceType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "HourlyRate",
                table: "ResourceProfiles",
                type: "numeric(10,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "ResourceProfiles",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentSalaryAmount",
                table: "ResourceProfiles",
                type: "numeric(14,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "CurrentSalaryType",
                table: "ResourceProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ResourceType",
                table: "ResourceProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "SalaryHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ResourceProfileId = table.Column<int>(type: "integer", nullable: false),
                    SalaryType = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    EffectiveHourlyRate = table.Column<decimal>(type: "numeric(10,4)", nullable: false),
                    BillRate = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecordedById = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryHistories_AspNetUsers_RecordedById",
                        column: x => x.RecordedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SalaryHistories_ResourceProfiles_ResourceProfileId",
                        column: x => x.ResourceProfileId,
                        principalTable: "ResourceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryHistories_RecordedById",
                table: "SalaryHistories",
                column: "RecordedById");

            migrationBuilder.CreateIndex(
                name: "UIX_SalaryHistory_OneActivePerProfile",
                table: "SalaryHistories",
                columns: new[] { "ResourceProfileId", "EffectiveTo" },
                unique: true,
                filter: "\"EffectiveTo\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalaryHistories");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "ResourceProfiles");

            migrationBuilder.DropColumn(
                name: "CurrentSalaryAmount",
                table: "ResourceProfiles");

            migrationBuilder.DropColumn(
                name: "CurrentSalaryType",
                table: "ResourceProfiles");

            migrationBuilder.DropColumn(
                name: "ResourceType",
                table: "ResourceProfiles");

            migrationBuilder.AlterColumn<decimal>(
                name: "HourlyRate",
                table: "ResourceProfiles",
                type: "numeric(10,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)");
        }
    }
}
