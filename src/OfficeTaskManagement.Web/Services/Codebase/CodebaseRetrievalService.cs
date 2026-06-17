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
        string query, int? projectId, int topK = 3, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<string>();

        float[] queryEmbedding;
        try
        {
            queryEmbedding = await _embedding.EmbedAsync(query, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding query failed");
            return Array.Empty<string>();
        }

        if (queryEmbedding.Length == 0)
            return Array.Empty<string>();

        var qVec = new Pgvector.Vector(queryEmbedding);
        var queryable = _db.CodeEmbeddings.AsNoTracking();
        if (projectId.HasValue)
        {
            queryable = queryable.Where(e => e.ProjectId == projectId.Value);
        }

        var chunks = await queryable
            .Where(e => e.Embedding != null)
            .OrderBy(e => e.Embedding.CosineDistance(qVec))
            .Take(topK)
            .Select(e => $"[{e.FilePath}:{e.StartLine}]\n{e.ChunkText}")
            .ToListAsync(ct);

        return chunks;
    }
}
