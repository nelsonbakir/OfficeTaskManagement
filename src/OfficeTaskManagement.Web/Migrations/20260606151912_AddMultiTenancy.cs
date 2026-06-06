using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OfficeTaskManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "UserNameIndex",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "WorkflowTemplates",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "WorkflowStages",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "UserStories",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "TestCases",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Tasks",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "TaskHistories",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "TaskComments",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Sprints",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "SalaryHistories",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "ResourceSkills",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "ResourceProfiles",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "ResourceAvailabilityBlocks",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "PublicHolidays",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Projects",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "ProjectResourceAllocations",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "PortfolioDecisions",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "PermissionGroups",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "PermissionGroupKeys",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Notifications",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Features",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Epics",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Attachments",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AspNetUsers",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AspNetRoles",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Areas",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AppRolePermissionGroups",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id");

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Identifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Tenants",
                columns: new[] { "Id", "Name", "Identifier", "CreatedAt" },
                values: new object[] { "default-tenant-id", "TaskFlow Corp", "taskflow", DateTime.UtcNow });

            migrationBuilder.InsertData(
                table: "Tenants",
                columns: new[] { "Id", "Name", "Identifier", "CreatedAt" },
                values: new object[] { "acme-tenant-id", "Acme Inc", "acme", DateTime.UtcNow });

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                columns: new[] { "NormalizedEmail", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                columns: new[] { "NormalizedUserName", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                columns: new[] { "NormalizedName", "TenantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "UserNameIndex",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "WorkflowTemplates");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "WorkflowStages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "UserStories");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TaskHistories");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TaskComments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SalaryHistories");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ResourceSkills");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ResourceProfiles");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ResourceAvailabilityBlocks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PublicHolidays");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ProjectResourceAllocations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PortfolioDecisions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PermissionGroups");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PermissionGroupKeys");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Features");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Epics");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AspNetRoles");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Areas");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AppRolePermissionGroups");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);
        }
    }
}
