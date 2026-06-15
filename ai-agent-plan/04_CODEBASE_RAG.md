# Codebase RAG — Git Repo Indexing & Semantic Retrieval
**OfficeTaskManagement · Language-Agnostic · Full Repository Scope**

---

## Scope: Entire Git Repository

The indexer targets the **entire Git repository root** — not just `src/`. This means:

| Directory/Pattern | Action | Reason |
|-------------------|--------|--------|
| `src/**/*.cs` | ✅ Index | Core C# application code |
| `src/**/*.csproj` | ✅ Index | Project dependencies = complexity signal |
| `Tests/**/*.cs` | ✅ Index | Test coverage = quality signal |
| `**/*.md` | ✅ Index | Docs, AGENTS.md, README — domain knowledge |
| `**/*.sql` | ✅ Index | Schema = data complexity |
| `**/*.json` (config) | ✅ Index | appsettings, models.json — integration knowledge |
| `**/*.yaml` / `**/*.yml` | ✅ Index | CI/CD complexity, Docker config |
| `**/*.js` / `**/*.ts` | ✅ Index | Frontend complexity |
| `**/*.py` | ✅ Index | Scripts, tools |
| `**/*.html` / `**/*.cshtml` | ✅ Index | View complexity |
| `bin/` / `obj/` | ❌ Skip | Build artifacts |
| `node_modules/` | ❌ Skip | Dependencies |
| `.git/` | ❌ Skip | Git internals |
| `*.min.js` | ❌ Skip | Minified = unreadable |
| Files > 500KB | ❌ Skip | Likely binary or generated |

---

## Language-Aware Chunking Strategy

Different file types need different chunking approaches to produce meaningful semantic units.

### Chunker Registry

```csharp
public static class ChunkerRegistry
{
    public static IChunker GetChunker(string fileExtension) => fileExtension.ToLower() switch
    {
        ".cs"    => new CSharpChunker(),      // Class + method level
        ".js"    => new JsChunker(),           // Function + class level
        ".ts"    => new TsChunker(),           // Same as JS + interface
        ".py"    => new PythonChunker(),       // Function + class level
        ".sql"   => new SqlChunker(),          // Statement + table level
        ".md"    => new MarkdownChunker(),     // H2/H3 section level
        ".json"  => new JsonChunker(),         // Top-level key groups
        ".cshtml"=> new CshtmlChunker(),       // Section/form level
        ".yaml"  => new YamlChunker(),         // Top-level key groups
        ".yml"   => new YamlChunker(),
        _        => new LineWindowChunker()    // Fallback: 50-line sliding window
    };
}
```

### C# Chunker Logic

```csharp
public class CSharpChunker : IChunker
{
    // Splits a .cs file into chunks at the class and method boundary
    // Each chunk = file header comment + class declaration + ONE method body
    // This keeps chunks small (~100-300 lines) while preserving context

    public IEnumerable<CodeChunk> Chunk(string filePath, string content)
    {
        // Parse using Roslyn (Microsoft.CodeAnalysis.CSharp)
        var tree = CSharpSyntaxTree.ParseText(content);
        var root = tree.GetRoot();
        
        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            // Class-level chunk: signature + fields + properties (no method bodies)
            yield return new CodeChunk
            {
                FilePath  = filePath,
                ChunkType = "class_header",
                StartLine = classDecl.GetLocation().GetLineSpan().StartLinePosition.Line,
                Text      = ExtractClassHeader(classDecl)
            };
            
            // Method-level chunks
            foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                yield return new CodeChunk
                {
                    FilePath  = filePath,
                    ChunkType = "method",
                    StartLine = method.GetLocation().GetLineSpan().StartLinePosition.Line,
                    Text      = $"// {filePath}\n{classDecl.Identifier} :: {method.Identifier}\n{method.ToFullString()}"
                };
            }
        }
    }
}
```

### Markdown Chunker Logic

```csharp
public class MarkdownChunker : IChunker
{
    // Splits at H2 (##) headings — each section becomes one chunk
    // Min chunk size: 100 chars (skip empty sections)
    public IEnumerable<CodeChunk> Chunk(string filePath, string content)
    {
        var sections = Regex.Split(content, @"(?=^## )", RegexOptions.Multiline);
        foreach (var section in sections.Where(s => s.Length > 100))
        {
            yield return new CodeChunk { FilePath = filePath, Text = section[..Math.Min(section.Length, 2000)] };
        }
    }
}
```

---

## Database Schema for Embeddings

```sql
-- Requires: CREATE EXTENSION IF NOT EXISTS vector;
-- Migration: AddAiAgentTables

CREATE TABLE code_embeddings (
    id           SERIAL PRIMARY KEY,
    tenant_id    TEXT NOT NULL DEFAULT '',      -- multi-tenant safe
    file_path    TEXT NOT NULL,
    chunk_type   TEXT NOT NULL,                 -- class_header|method|section|statement
    start_line   INTEGER,
    chunk_text   TEXT NOT NULL,
    embedding    vector(768),                   -- Gemini text-embedding-004 dimension
    file_hash    TEXT NOT NULL,                 -- MD5 of file — skip re-indexing if unchanged
    indexed_at   TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX ON code_embeddings USING ivfflat (embedding vector_cosine_ops) 
WITH (lists = 100);

CREATE INDEX ON code_embeddings(file_path);
CREATE INDEX ON code_embeddings(file_hash);
```

### EF Core Entity

```csharp
// Models/Ai/CodeEmbedding.cs
public class CodeEmbedding
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ChunkType { get; set; } = string.Empty;
    public int? StartLine { get; set; }
    public string ChunkText { get; set; } = string.Empty;
    
    // pgvector stores as float[] in EF Core via Pgvector.EntityFrameworkCore
    public float[] Embedding { get; set; } = Array.Empty<float>();
    
    public string FileHash { get; set; } = string.Empty;
    public DateTimeOffset IndexedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

---

## CodebaseIndexingService

```csharp
// Services/Codebase/CodebaseIndexingService.cs
public class CodebaseIndexingService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<CodebaseIndexingService> _logger;

    // SKIP patterns
    private static readonly string[] SkipDirs = 
        { "bin", "obj", "node_modules", ".git", ".vs", "wwwroot/lib" };
    private static readonly string[] SkipExtensions = 
        { ".min.js", ".min.css", ".map", ".dll", ".exe", ".png", ".jpg", ".pdf" };
    private const long MaxFileSizeBytes = 500_000; // 500KB

    public async Task StartAsync(CancellationToken ct)
    {
        // Run indexing on startup in background (non-blocking)
        _ = Task.Run(() => IndexRepositoryAsync(ct), ct);
    }

    public async Task IndexRepositoryAsync(CancellationToken ct)
    {
        var repoRoot = _config["Codebase:RepositoryRoot"] 
                    ?? Path.GetFullPath("../../../../"); // relative to Web project
        
        _logger.LogInformation("Starting codebase indexing from: {root}", repoRoot);
        
        var files = DiscoverFiles(repoRoot);
        int indexed = 0, skipped = 0;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var embeddingApi = scope.ServiceProvider.GetRequiredService<IGeminiEmbeddingService>();

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var fileHash = ComputeMd5(filePath);
                // Skip if file hasn't changed
                if (await db.CodeEmbeddings.AnyAsync(e => e.FilePath == filePath && e.FileHash == fileHash, ct))
                { skipped++; continue; }

                // Remove old chunks for this file
                db.CodeEmbeddings.RemoveRange(
                    db.CodeEmbeddings.Where(e => e.FilePath == filePath));

                var content = await File.ReadAllTextAsync(filePath, ct);
                var ext = Path.GetExtension(filePath).ToLower();
                var chunker = ChunkerRegistry.GetChunker(ext);
                var chunks = chunker.Chunk(filePath, content).ToList();

                // Batch embed (Gemini allows 100 texts per call)
                foreach (var batch in chunks.Chunk(100))
                {
                    var embeddings = await embeddingApi.EmbedBatchAsync(
                        batch.Select(c => c.Text).ToArray(), ct);
                    
                    for (int i = 0; i < batch.Length; i++)
                    {
                        db.CodeEmbeddings.Add(new CodeEmbedding
                        {
                            FilePath  = GetRelativePath(repoRoot, filePath),
                            ChunkType = batch[i].ChunkType,
                            StartLine = batch[i].StartLine,
                            ChunkText = batch[i].Text[..Math.Min(batch[i].Text.Length, 3000)],
                            Embedding = embeddings[i],
                            FileHash  = fileHash
                        });
                    }
                    await db.SaveChangesAsync(ct);
                }
                indexed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to index {file}", filePath);
            }
        }
        _logger.LogInformation("Indexing complete: {indexed} files indexed, {skipped} unchanged", indexed, skipped);
    }

    private IEnumerable<string> DiscoverFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var parts = f.Replace('\\', '/').Split('/');
                return !parts.Any(p => SkipDirs.Contains(p, StringComparer.OrdinalIgnoreCase))
                    && !SkipExtensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                    && new FileInfo(f).Length < MaxFileSizeBytes;
            });
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

---

## CodebaseRetrievalService

```csharp
// Services/Codebase/CodebaseRetrievalService.cs
public class CodebaseRetrievalService
{
    private readonly ApplicationDbContext _db;
    private readonly IGeminiEmbeddingService _embedding;

    // Returns top-K most relevant code chunks for a query
    // Used by ContextBuilderService to inject code context into prompts
    public async Task<IReadOnlyList<string>> GetRelevantChunksAsync(
        string query, int topK = 3, CancellationToken ct = default)
    {
        // 1. Embed the query
        var queryEmbedding = await _embedding.EmbedAsync(query, ct);
        
        // 2. Cosine similarity search via pgvector
        // EF Core + Pgvector extension: uses <=> operator
        var chunks = await _db.CodeEmbeddings
            .OrderBy(e => EF.Functions.VectorCosineDistance(e.Embedding, queryEmbedding))
            .Take(topK)
            .Select(e => $"[{e.FilePath}:{e.StartLine}]\n{e.ChunkText}")
            .ToListAsync(ct);
        
        return chunks;
    }
}
```

---

## Git Webhook — Live Re-indexing

### Endpoint

```csharp
// Controllers/Api/AgentController.cs
[HttpPost("/api/agent/reindex")]
[AllowAnonymous] // Secured via webhook secret header
public async Task<IActionResult> ReindexAsync(
    [FromHeader(Name = "X-Webhook-Secret")] string secret)
{
    if (secret != _config["Codebase:WebhookSecret"])
        return Unauthorized();
    
    // Fire-and-forget incremental re-index
    _ = Task.Run(() => _indexingService.IndexRepositoryAsync(CancellationToken.None));
    return Accepted("Re-indexing started.");
}
```

### GitHub Actions Webhook (`.github/workflows/reindex.yml`)

```yaml
name: Reindex Codebase on Push
on:
  push:
    branches: [main, develop]
jobs:
  reindex:
    runs-on: ubuntu-latest
    steps:
      - name: Trigger Re-index
        run: |
          curl -X POST ${{ secrets.PMP_REINDEX_URL }}/api/agent/reindex \
            -H "X-Webhook-Secret: ${{ secrets.REINDEX_WEBHOOK_SECRET }}"
```

---

## appsettings additions

```json
// Add to appsettings.json (values in User Secrets):
{
  "Codebase": {
    "RepositoryRoot": "d:\\TGI\\Products\\OfficeTaskManagement",
    "WebhookSecret": "<<IN_USER_SECRETS>>"
  },
  "Gemini": {
    "ApiKey": "<<IN_USER_SECRETS>>",
    "EmbeddingModel": "models/text-embedding-004",
    "GenerativeModel": "gemini-2.5-flash",
    "CopilotModel": "gemini-2.5-pro"
  }
}
```
