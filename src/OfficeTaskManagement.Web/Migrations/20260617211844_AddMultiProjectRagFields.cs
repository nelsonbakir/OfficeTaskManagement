using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace OfficeTaskManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiProjectRagFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM \"CodeEmbeddings\";");

            migrationBuilder.AddColumn<string>(
                name: "RepositoryPath",
                table: "Projects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepositoryUrl",
                table: "Projects",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "CodeEmbeddings",
                type: "text",
                nullable: false,
                defaultValue: "default-tenant-id",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<Vector>(
                name: "Embedding",
                table: "CodeEmbeddings",
                type: "vector(768)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "CodeEmbeddings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_CodeEmbeddings_ProjectId",
                table: "CodeEmbeddings",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_CodeEmbeddings_Projects_ProjectId",
                table: "CodeEmbeddings",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CodeEmbeddings_Projects_ProjectId",
                table: "CodeEmbeddings");

            migrationBuilder.DropIndex(
                name: "IX_CodeEmbeddings_ProjectId",
                table: "CodeEmbeddings");

            migrationBuilder.DropColumn(
                name: "RepositoryPath",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "RepositoryUrl",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "CodeEmbeddings");

            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "CodeEmbeddings",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "default-tenant-id");

            migrationBuilder.AlterColumn<string>(
                name: "Embedding",
                table: "CodeEmbeddings",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Vector),
                oldType: "vector(768)");
        }
    }
}
