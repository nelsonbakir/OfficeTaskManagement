using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OfficeTaskManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategicFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApprovalStatus",
                table: "ResourceAvailabilityBlocks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "ResourceAvailabilityBlocks",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedById",
                table: "ResourceAvailabilityBlocks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequiredSkills",
                table: "Projects",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceAvailabilityBlocks_ApprovedById",
                table: "ResourceAvailabilityBlocks",
                column: "ApprovedById");

            migrationBuilder.AddForeignKey(
                name: "FK_ResourceAvailabilityBlocks_AspNetUsers_ApprovedById",
                table: "ResourceAvailabilityBlocks",
                column: "ApprovedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ResourceAvailabilityBlocks_AspNetUsers_ApprovedById",
                table: "ResourceAvailabilityBlocks");

            migrationBuilder.DropIndex(
                name: "IX_ResourceAvailabilityBlocks_ApprovedById",
                table: "ResourceAvailabilityBlocks");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "ResourceAvailabilityBlocks");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "ResourceAvailabilityBlocks");

            migrationBuilder.DropColumn(
                name: "ApprovedById",
                table: "ResourceAvailabilityBlocks");

            migrationBuilder.DropColumn(
                name: "RequiredSkills",
                table: "Projects");
        }
    }
}
