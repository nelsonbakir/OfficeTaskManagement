using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Services.Ai;

namespace OfficeTaskManagement.Services.Codebase;

/// <summary>
/// Retrieves semantically relevant code chunks from the indexed repository.
/// Used by ContextBuilderService to inject codebase knowledge into estimation prompts.
/// Spec: ai-agent-plan/04_CODEBASE_RAG.md → CodebaseRetrievalService
/// </summary>
public class CodebaseRetrievalService
{
    private readonly ApplicationDbContext _db;
    private readonly IGeminiEmbeddingService _embedding;
    private readonly ILogger<CodebaseRetrievalService> _logger;

    public CodebaseRetrievalService(
        ApplicationDbContext db,
        IGeminiEmbeddingService embedding,
        ILogger<CodebaseRetrievalService> logger)
    {
        _db = db;
        _embedding = embedding;
        _logger = logger;
    }

    /// <summary>
    /// Returns top-K most semantically relevant code chunks for the given query.
    /// Uses pgvector cosine similarity in production (PostgreSQL).
    /// Falls back to LIKE keyword search in development (SQLite / InMemory).
    /// </summary>
    /// <param name="query">Free-text search query, e.g. task title or description</param>
    /// <param name="topK">Maximum number of chunks to return</param>
    /// <param name="ct">Cancellation token</param>
    public async Task<IReadOnlyList<string>> GetRelevantChunksAsync(
        string query, int topK = 3, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<string>();

        // Determine DB provider at runtime
        var providerName = _db.Database.ProviderName ?? "";
        bool isPostgres  = providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);

        if (isPostgres)
        {
            return await GetChunksViaVectorSearchAsync(query, topK, ct);
        }
        else
        {
            // Dev/test fallback: keyword search
            return await GetChunksViaKeywordSearchAsync(query, topK, ct);
        }
    }

    // ── PostgreSQL path: pgvector cosine similarity ────────────────────────────
    private async Task<IReadOnlyList<string>> GetChunksViaVectorSearchAsync(
        string query, int topK, CancellationToken ct)
    {
        float[] queryEmbedding;
        try
        {
            queryEmbedding = await _embedding.EmbedAsync(query, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding query failed — falling back to keyword search");
            return await GetChunksViaKeywordSearchAsync(query, topK, ct);
        }

        if (queryEmbedding.Length == 0)
            return await GetChunksViaKeywordSearchAsync(query, topK, ct);

        // pgvector <=> operator via Pgvector.EntityFrameworkCore
        try
        {
            var qVec = new Pgvector.Vector(queryEmbedding);
            var chunks = await _db.CodeEmbeddings
                .Where(e => e.Embedding != null && e.Embedding.Length > 0)
                .OrderBy(e => e.Embedding.CosineDistance(qVec.ToArray()))
                .Take(topK)
                .Select(e => $"[{e.FilePath}:{e.StartLine}]\n{e.ChunkText}")
                .ToListAsync(ct);

            return chunks;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector search failed — falling back to keyword search");
            return await GetChunksViaKeywordSearchAsync(query, topK, ct);
        }
    }

    // ── SQLite / InMemory fallback: simple keyword search ─────────────────────
    private async Task<IReadOnlyList<string>> GetChunksViaKeywordSearchAsync(
        string query, int topK, CancellationToken ct)
    {
        if (await _db.CodeEmbeddings.AnyAsync(ct) == false)
            return Array.Empty<string>();

        // Extract significant keywords (words > 3 chars)
        var keywords = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .Select(w => w.ToLower())
            .Distinct()
            .Take(5)
            .ToArray();

        if (keywords.Length == 0) return Array.Empty<string>();

        // Load all rows then filter in C# — InMemory provider cannot translate
        // arbitrary .Any() expressions over captured arrays.
        // Acceptable for dev/test with a small index.
        var all = await _db.CodeEmbeddings
            .Select(e => new { e.FilePath, e.StartLine, e.ChunkText })
            .ToListAsync(ct);

        // Score by keyword hit count, return top-K
        var scored = all
            .Select(e => new
            {
                Chunk = $"[{e.FilePath}:{e.StartLine}]\n{e.ChunkText}",
                Score = keywords.Count(k =>
                    e.ChunkText.Contains(k, StringComparison.OrdinalIgnoreCase))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Chunk)
            .ToList();

        return scored;
    }
}
