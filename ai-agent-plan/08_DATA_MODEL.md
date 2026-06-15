# Data Model — DB Migrations & New EF Core Entities
**OfficeTaskManagement · AI Agent Tables · pgvector Setup**

---

## New EF Core Entities

### 1. CodeEmbedding

```csharp
// Models/Ai/CodeEmbedding.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OfficeTaskManagement.Models.Ai
{
    /// <summary>
    /// Stores a semantic embedding for a chunk of code or documentation
    /// from the Git repository. Used by CodebaseRetrievalService for
    /// vector similarity search (pgvector).
    /// </summary>
    public class CodeEmbedding
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Tenant scope — prevents cross-tenant code leakage.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Relative path from repo root, e.g. "src/Services/ResourceService.cs".</summary>
        [Required]
        [StringLength(500)]
        public string FilePath { get; set; } = string.Empty;

        /// <summary>Chunking type: class_header | method | function | section | statement | window</summary>
        [StringLength(50)]
        public string ChunkType { get; set; } = string.Empty;

        /// <summary>Line number in the file where this chunk starts (1-indexed).</summary>
        public int? StartLine { get; set; }

        /// <summary>The actual text that was embedded. Max 3000 chars.</summary>
        [Required]
        public string ChunkText { get; set; } = string.Empty;

        /// <summary>
        /// 768-dimensional float vector from Gemini text-embedding-004.
        /// Stored as PostgreSQL vector type via Pgvector extension.
        /// </summary>
        [Column(TypeName = "vector(768)")]
        public float[] Embedding { get; set; } = Array.Empty<float>();

        /// <summary>MD5 hash of the file at index time. Used to skip unchanged files.</summary>
        [StringLength(32)]
        public string FileHash { get; set; } = string.Empty;

        public DateTimeOffset IndexedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
```

---

### 2. AgentConversation

```csharp
// Models/Ai/AgentConversation.cs
using System;
using System.ComponentModel.DataAnnotations;

namespace OfficeTaskManagement.Models.Ai
{
    /// <summary>
    /// Persists multi-turn conversation history for the AI Copilot sidebar.
    /// Each conversation is scoped to a user + a PM entity (e.g., an Epic).
    /// </summary>
    public class AgentConversation
    {
        [Key]
        [StringLength(36)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string UserId { get; set; } = string.Empty;
        public User? User { get; set; }

        public string TenantId { get; set; } = string.Empty;

        /// <summary>PM entity context: "Epic" | "Feature" | "UserStory" | "Task" | "Project"</summary>
        [StringLength(50)]
        public string? EntityType { get; set; }

        /// <summary>The ID of the PM entity this conversation is about.</summary>
        public int? EntityId { get; set; }

        /// <summary>
        /// JSON array of conversation turns.
        /// Schema: [{ "role": "user"|"model", "text": "...", "timestamp": "..." }]
        /// Stored as JSONB in PostgreSQL.
        /// </summary>
        [Required]
        public string TurnsJson { get; set; } = "[]";

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>Conversations expire after 24h of inactivity to save storage.</summary>
        public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(24);
    }
}
```

---

### 3. AiEstimationLog

```csharp
// Models/Ai/AiEstimationLog.cs
using System;
using System.ComponentModel.DataAnnotations;

namespace OfficeTaskManagement.Models.Ai
{
    /// <summary>
    /// Audit log for all AI estimation calls.
    /// Used for cost monitoring (token usage) and quality analysis
    /// (compare AI estimates vs actual hours after task completion).
    /// </summary>
    public class AiEstimationLog
    {
        [Key]
        public int Id { get; set; }

        public string TenantId { get; set; } = string.Empty;

        /// <summary>Who triggered the estimation.</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>"Epic" | "Feature" | "UserStory" | "Task" | "Project"</summary>
        [StringLength(50)]
        public string EntityType { get; set; } = string.Empty;

        /// <summary>Null for new items (not yet saved), set for re-estimations.</summary>
        public int? EntityId { get; set; }

        [StringLength(100)]
        public string Model { get; set; } = string.Empty;

        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }

        /// <summary>AI-returned PERT estimate at the time of call.</summary>
        public decimal? AiPertHours { get; set; }

        /// <summary>
        /// Populated retroactively via a background job:
        /// actual hours from TaskHistory once the entity reaches Done.
        /// Enables "AI accuracy" analytics dashboard.
        /// </summary>
        public decimal? ActualHours { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
```

---

## DbContext Changes

### Add to `Data/ApplicationDbContext.cs`

```csharp
// Add these DbSet properties:
public DbSet<CodeEmbedding>       CodeEmbeddings    { get; set; }
public DbSet<AgentConversation>   AgentConversations { get; set; }
public DbSet<AiEstimationLog>     AiEstimationLogs  { get; set; }

// Add to OnModelCreating:
protected override void OnModelCreating(ModelBuilder builder)
{
    base.OnModelCreating(builder);

    // ... existing configurations ...

    // CodeEmbedding — pgvector index
    builder.Entity<CodeEmbedding>(e =>
    {
        e.HasIndex(x => x.FilePath);
        e.HasIndex(x => x.FileHash);
        e.HasIndex(x => x.TenantId);
        // IVFFlat index defined in raw SQL migration (see below)
    });

    // AgentConversation — expire index
    builder.Entity<AgentConversation>(e =>
    {
        e.HasIndex(x => new { x.UserId, x.EntityType, x.EntityId });
        e.HasIndex(x => x.ExpiresAt); // For cleanup job
    });

    // AiEstimationLog
    builder.Entity<AiEstimationLog>(e =>
    {
        e.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId });
        e.HasIndex(x => x.CreatedAt);
    });
}
```

---

## Migration: AddAiAgentTables

### Migration File Structure

```csharp
// Migrations/[timestamp]_AddAiAgentTables.cs
public partial class AddAiAgentTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1. Enable pgvector extension (PostgreSQL only; SQLite dev will skip)
        migrationBuilder.Sql(@"
            DO $$ BEGIN
                CREATE EXTENSION IF NOT EXISTS vector;
            EXCEPTION WHEN OTHERS THEN
                -- SQLite dev environment: skip silently
            END $$;
        ");

        // 2. CodeEmbeddings table
        migrationBuilder.CreateTable(
            name: "CodeEmbeddings",
            columns: table => new
            {
                Id        = table.Column<int>(nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                TenantId  = table.Column<string>(maxLength: 450, nullable: false, defaultValue: ""),
                FilePath  = table.Column<string>(maxLength: 500, nullable: false),
                ChunkType = table.Column<string>(maxLength: 50, nullable: false, defaultValue: ""),
                StartLine = table.Column<int>(nullable: true),
                ChunkText = table.Column<string>(nullable: false),
                Embedding = table.Column<float[]>(type: "vector(768)", nullable: false),
                FileHash  = table.Column<string>(maxLength: 32, nullable: false, defaultValue: ""),
                IndexedAt = table.Column<DateTimeOffset>(nullable: false, defaultValueSql: "now()")
            },
            constraints: table => table.PrimaryKey("PK_CodeEmbeddings", x => x.Id));

        // IVFFlat index for cosine similarity (must be raw SQL — EF doesn't know pgvector)
        migrationBuilder.Sql(@"
            CREATE INDEX IF NOT EXISTS ix_code_embeddings_embedding 
            ON ""CodeEmbeddings"" USING ivfflat (""Embedding"" vector_cosine_ops) 
            WITH (lists = 100);
        ");

        migrationBuilder.CreateIndex("IX_CodeEmbeddings_FilePath",  "CodeEmbeddings", "FilePath");
        migrationBuilder.CreateIndex("IX_CodeEmbeddings_FileHash",  "CodeEmbeddings", "FileHash");
        migrationBuilder.CreateIndex("IX_CodeEmbeddings_TenantId",  "CodeEmbeddings", "TenantId");

        // 3. AgentConversations table
        migrationBuilder.CreateTable(
            name: "AgentConversations",
            columns: table => new
            {
                Id         = table.Column<string>(maxLength: 36, nullable: false),
                UserId     = table.Column<string>(nullable: false),
                TenantId   = table.Column<string>(nullable: false, defaultValue: ""),
                EntityType = table.Column<string>(maxLength: 50, nullable: true),
                EntityId   = table.Column<int>(nullable: true),
                TurnsJson  = table.Column<string>(nullable: false, defaultValue: "[]"),
                CreatedAt  = table.Column<DateTimeOffset>(nullable: false, defaultValueSql: "now()"),
                UpdatedAt  = table.Column<DateTimeOffset>(nullable: false, defaultValueSql: "now()"),
                ExpiresAt  = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AgentConversations", x => x.Id);
                table.ForeignKey("FK_AgentConversations_AspNetUsers_UserId",
                    x => x.UserId, principalTable: "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_AgentConversations_User_Entity", "AgentConversations",
            new[] { "UserId", "EntityType", "EntityId" });
        migrationBuilder.CreateIndex("IX_AgentConversations_ExpiresAt", "AgentConversations", "ExpiresAt");

        // 4. AiEstimationLogs table
        migrationBuilder.CreateTable(
            name: "AiEstimationLogs",
            columns: table => new
            {
                Id          = table.Column<int>(nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                TenantId    = table.Column<string>(nullable: false, defaultValue: ""),
                UserId      = table.Column<string>(nullable: false),
                EntityType  = table.Column<string>(maxLength: 50, nullable: false),
                EntityId    = table.Column<int>(nullable: true),
                Model       = table.Column<string>(maxLength: 100, nullable: false),
                InputTokens = table.Column<int>(nullable: false),
                OutputTokens= table.Column<int>(nullable: false),
                AiPertHours = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                ActualHours = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                CreatedAt   = table.Column<DateTimeOffset>(nullable: false, defaultValueSql: "now()")
            },
            constraints: table => table.PrimaryKey("PK_AiEstimationLogs", x => x.Id));

        migrationBuilder.CreateIndex("IX_AiEstimationLogs_Entity", "AiEstimationLogs",
            new[] { "TenantId", "EntityType", "EntityId" });
        migrationBuilder.CreateIndex("IX_AiEstimationLogs_CreatedAt", "AiEstimationLogs", "CreatedAt");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AiEstimationLogs");
        migrationBuilder.DropTable("AgentConversations");
        migrationBuilder.Sql(@"DROP INDEX IF EXISTS ix_code_embeddings_embedding;");
        migrationBuilder.DropTable("CodeEmbeddings");
    }
}
```

---

## SQLite Dev Compatibility Note

The `CodeEmbedding.Embedding` field uses `vector(768)` which is a PostgreSQL-only type. For SQLite (dev):

```csharp
// In ApplicationDbContext.OnModelCreating:
if (Database.IsSqlite())
{
    // Store embedding as TEXT in SQLite (serialized JSON float array)
    // Real cosine search won't work in dev — use fallback keyword search
    modelBuilder.Entity<CodeEmbedding>()
        .Property(e => e.Embedding)
        .HasConversion(
            v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
            v => System.Text.Json.JsonSerializer.Deserialize<float[]>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? Array.Empty<float>()
        );
}
```

---

## NuGet Package Requirements

Add to `OfficeTaskManagement.csproj`:

```xml
<!-- pgvector EF Core integration -->
<PackageReference Include="Pgvector"                        Version="0.3.*" />
<PackageReference Include="Pgvector.EntityFrameworkCore"    Version="0.3.*" />

<!-- Roslyn for C# chunking (Phase 3) -->
<PackageReference Include="Microsoft.CodeAnalysis.CSharp"   Version="4.*" />

<!-- Already present — confirm these exist -->
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.*" />
<PackageReference Include="Microsoft.Extensions.Caching.Memory"   Version="9.*" />
```

---

## Cleanup: Expired Conversations (Background Job)

Add to `CodebaseIndexingService.StartAsync` or a separate `IHostedService`:

```csharp
// Clean up expired agent conversations daily
public async Task CleanupExpiredConversationsAsync(CancellationToken ct)
{
    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var expired = await db.AgentConversations
        .Where(c => c.ExpiresAt < DateTimeOffset.UtcNow)
        .ToListAsync(ct);
    db.AgentConversations.RemoveRange(expired);
    await db.SaveChangesAsync(ct);
}
```
