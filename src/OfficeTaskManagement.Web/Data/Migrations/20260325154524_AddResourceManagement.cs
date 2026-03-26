using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OfficeTaskManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PlannedCapacityHours",
                table: "Sprints",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TeamNotes",
                table: "Sprints",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ResourceProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SeniorityLevel = table.Column<int>(type: "integer", nullable: false),
                    DailyCapacityHours = table.Column<decimal>(type: "numeric", nullable: false),
                    HourlyRate = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectResourceAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    AllocationPercentage = table.Column<int>(type: "integer", nullable: false),
                    ProjectRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AllocatedById = table.Column<string>(type: "text", nullable: true),
                    AllocatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResourceProfileId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectResourceAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectResourceAllocations_AspNetUsers_AllocatedById",
                        column: x => x.AllocatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProjectResourceAllocations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectResourceAllocations_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectResourceAllocations_ResourceProfiles_ResourceProfile~",
                        column: x => x.ResourceProfileId,
                        principalTable: "ResourceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ResourceAvailabilityBlocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedById = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResourceProfileId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceAvailabilityBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceAvailabilityBlocks_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ResourceAvailabilityBlocks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceAvailabilityBlocks_ResourceProfiles_ResourceProfile~",
                        column: x => x.ResourceProfileId,
                        principalTable: "ResourceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ResourceSkills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ResourceProfileId = table.Column<int>(type: "integer", nullable: false),
                    SkillName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProficiencyLevel = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceSkills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceSkills_ResourceProfiles_ResourceProfileId",
                        column: x => x.ResourceProfileId,
                        principalTable: "ResourceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectResourceAllocations_AllocatedById",
                table: "ProjectResourceAllocations",
                column: "AllocatedById");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectResourceAllocations_ProjectId",
                table: "ProjectResourceAllocations",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectResourceAllocations_ResourceProfileId",
                table: "ProjectResourceAllocations",
                column: "ResourceProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectResourceAllocations_UserId",
                table: "ProjectResourceAllocations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceAvailabilityBlocks_CreatedById",
                table: "ResourceAvailabilityBlocks",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceAvailabilityBlocks_ResourceProfileId",
                table: "ResourceAvailabilityBlocks",
                column: "ResourceProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceAvailabilityBlocks_UserId",
                table: "ResourceAvailabilityBlocks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceProfiles_UserId",
                table: "ResourceProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceSkills_ResourceProfileId",
                table: "ResourceSkills",
                column: "ResourceProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectResourceAllocations");

            migrationBuilder.DropTable(
                name: "ResourceAvailabilityBlocks");

            migrationBuilder.DropTable(
                name: "ResourceSkills");

            migrationBuilder.DropTable(
                name: "ResourceProfiles");

            migrationBuilder.DropColumn(
                name: "PlannedCapacityHours",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "TeamNotes",
                table: "Sprints");
        }
    }
}
