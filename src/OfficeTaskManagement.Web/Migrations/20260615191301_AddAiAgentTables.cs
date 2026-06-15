using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OfficeTaskManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAgentTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentConversations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    EntityId = table.Column<int>(type: "integer", nullable: true),
                    TurnsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentConversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentConversations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiEstimationLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    AiPertHours = table.Column<decimal>(type: "numeric", nullable: true),
                    ActualHours = table.Column<decimal>(type: "numeric", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiEstimationLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CodeEmbeddings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    FilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ChunkType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartLine = table.Column<int>(type: "integer", nullable: true),
                    ChunkText = table.Column<string>(type: "text", nullable: false),
                    Embedding = table.Column<string>(type: "TEXT", nullable: false),
                    FileHash = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IndexedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeEmbeddings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentConversations_ExpiresAt",
                table: "AgentConversations",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AgentConversations_UserId_EntityType_EntityId",
                table: "AgentConversations",
                columns: new[] { "UserId", "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiEstimationLogs_CreatedAt",
                table: "AiEstimationLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiEstimationLogs_TenantId_EntityType_EntityId",
                table: "AiEstimationLogs",
                columns: new[] { "TenantId", "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_CodeEmbeddings_FileHash",
                table: "CodeEmbeddings",
                column: "FileHash");

            migrationBuilder.CreateIndex(
                name: "IX_CodeEmbeddings_FilePath",
                table: "CodeEmbeddings",
                column: "FilePath");

            migrationBuilder.CreateIndex(
                name: "IX_CodeEmbeddings_TenantId",
                table: "CodeEmbeddings",
                column: "TenantId");
            // T05: Enable pgvector extension (PostgreSQL only — silently skipped on SQLite dev)
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    CREATE EXTENSION IF NOT EXISTS vector;
                EXCEPTION WHEN OTHERS THEN
                    NULL; -- SQLite dev environment: skip silently
                END $$;
            ");

            // T05: On PostgreSQL, alter the Embedding column to vector(768) type
            // (EF Core generates TEXT due to the JSON string conversion — override here)
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    ALTER TABLE ""CodeEmbeddings"" 
                    ALTER COLUMN ""Embedding"" TYPE vector(768) 
                    USING ""Embedding""::vector(768);
                EXCEPTION WHEN OTHERS THEN
                    NULL; -- SQLite or pgvector not installed: keep TEXT type
                END $$;
            ");

            // T05: IVFFlat cosine similarity index for semantic search (PostgreSQL + pgvector only)
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    CREATE INDEX IF NOT EXISTS ix_code_embeddings_embedding 
                    ON ""CodeEmbeddings"" USING ivfflat (""Embedding"" vector_cosine_ops) 
                    WITH (lists = 100);
                EXCEPTION WHEN OTHERS THEN
                    NULL; -- pgvector not available: skip index creation
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentConversations");

            migrationBuilder.DropTable(
                name: "AiEstimationLogs");

            migrationBuilder.DropTable(
                name: "CodeEmbeddings");
        }
    }
}
