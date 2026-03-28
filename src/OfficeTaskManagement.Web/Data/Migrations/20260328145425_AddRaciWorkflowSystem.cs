using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OfficeTaskManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRaciWorkflowSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActualResult",
                table: "TestCases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPassed",
                table: "TestCases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AccountableUserId",
                table: "Tasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualHours",
                table: "Tasks",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedMostLikelyHours",
                table: "Tasks",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedOptimisticHours",
                table: "Tasks",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedPessimisticHours",
                table: "Tasks",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PertEstimatedHours",
                table: "Tasks",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RaciRole",
                table: "Tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowStageId",
                table: "Tasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FieldChanged",
                table: "TaskHistories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewValue",
                table: "TaskHistories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OldValue",
                table: "TaskHistories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RaciRoleAtTime",
                table: "TaskHistories",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkflowTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ProjectId = table.Column<int>(type: "integer", nullable: true),
                    ApplicableTaskType = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowTemplates_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowStages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorkflowTemplateId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    RaciRole = table.Column<int>(type: "integer", nullable: false),
                    DefaultRoleTitle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DependencyType = table.Column<int>(type: "integer", nullable: false),
                    LagHours = table.Column<decimal>(type: "numeric", nullable: false),
                    DefinitionOfDone = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowStages_WorkflowTemplates_WorkflowTemplateId",
                        column: x => x.WorkflowTemplateId,
                        principalTable: "WorkflowTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_AccountableUserId",
                table: "Tasks",
                column: "AccountableUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_WorkflowStageId",
                table: "Tasks",
                column: "WorkflowStageId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStages_WorkflowTemplateId",
                table: "WorkflowStages",
                column: "WorkflowTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTemplates_ProjectId",
                table: "WorkflowTemplates",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_AspNetUsers_AccountableUserId",
                table: "Tasks",
                column: "AccountableUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_WorkflowStages_WorkflowStageId",
                table: "Tasks",
                column: "WorkflowStageId",
                principalTable: "WorkflowStages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_AspNetUsers_AccountableUserId",
                table: "Tasks");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_WorkflowStages_WorkflowStageId",
                table: "Tasks");

            migrationBuilder.DropTable(
                name: "WorkflowStages");

            migrationBuilder.DropTable(
                name: "WorkflowTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_AccountableUserId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_WorkflowStageId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ActualResult",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "IsPassed",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "AccountableUserId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ActualHours",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "EstimatedMostLikelyHours",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "EstimatedOptimisticHours",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "EstimatedPessimisticHours",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "PertEstimatedHours",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "RaciRole",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "WorkflowStageId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "FieldChanged",
                table: "TaskHistories");

            migrationBuilder.DropColumn(
                name: "NewValue",
                table: "TaskHistories");

            migrationBuilder.DropColumn(
                name: "OldValue",
                table: "TaskHistories");

            migrationBuilder.DropColumn(
                name: "RaciRoleAtTime",
                table: "TaskHistories");
        }
    }
}
