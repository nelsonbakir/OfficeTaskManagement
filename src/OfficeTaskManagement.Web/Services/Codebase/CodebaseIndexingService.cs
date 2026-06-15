using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Services.Ai;
using System.Security.Cryptography;

namespace OfficeTaskManagement.Services.Codebase;

/// <summary>
/// Background service that indexes the entire repository into vector embeddings.
/// Runs once on startup; can also be triggered via webhook (AgentController /api/agent/reindex).
/// Spec: ai-agent-plan/04_CODEBASE_RAG.md → CodebaseIndexingService
/// </summary>
public class CodebaseIndexingService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<CodebaseIndexingService> _logger;

    // Skip directories
    private static readonly string[] SkipDirs =
        ["bin", "obj", "node_modules", ".git", ".vs", "wwwroot/lib", "wwwroot\\lib", "Migrations", "ai-agent-plan"];

    // Skip file extensions
    private static readonly string[] SkipExtensions =
        [".min.js", ".min.css", ".map", ".dll", ".exe", ".pdb", ".png", ".jpg", ".jpeg",
         ".gif", ".ico", ".svg", ".pdf", ".zip", ".7z", ".lock", ".user", ".db", ".db-shm", ".db-wal"];

    private const long MaxFileSizeBytes = 500_000; // 500 KB

    public CodebaseIndexingService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<CodebaseIndexingService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    /// <summary>Fires background indexing on startup — non-blocking.</summary>
    public Task StartAsync(CancellationToken ct)
    {
        _ = Task.Run(() => IndexRepositoryAsync(CancellationToken.None), CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Traverses the repository, chunks every file, embeds changed files, and persists to DB.
    /// Safe to call multiple times — unchanged files (same MD5 hash) are skipped.
    /// </summary>
    public async Task IndexRepositoryAsync(CancellationToken ct)
    {
        var repoRoot = _config["Codebase:RepositoryRoot"];
        if (string.IsNullOrEmpty(repoRoot) || !Directory.Exists(repoRoot))
        {
            // Attempt to resolve relative to current working directory
            var cwd = Directory.GetCurrentDirectory();
            repoRoot = Path.GetFullPath(Path.Combine(cwd, "../../../../"));
        }

        _logger.LogInformation("Starting codebase indexing from: {Root}", repoRoot);

        var files = DiscoverFiles(repoRoot).ToList();
        _logger.LogInformation("Discovered {Count} files to consider", files.Count);

        int indexed = 0, skipped = 0, failed = 0;

        using var scope = _scopeFactory.CreateScope();
        var db           = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var embeddingApi = scope.ServiceProvider.GetRequiredService<IGeminiEmbeddingService>();

        foreach (var filePath in files)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var fileHash = ComputeMd5(filePath);
                var relative = GetRelativePath(repoRoot, filePath);

                // Skip if file hasn't changed since last indexing
                if (await db.CodeEmbeddings.AnyAsync(
                    e => e.FilePath == relative && e.FileHash == fileHash, ct))
                {
                    skipped++;
                    continue;
                }

                // Remove old chunks for this file
                var old = db.CodeEmbeddings.Where(e => e.FilePath == relative);
                db.CodeEmbeddings.RemoveRange(old);

                var content = await File.ReadAllTextAsync(filePath, ct);
                if (string.IsNullOrWhiteSpace(content)) continue;

                var ext    = Path.GetExtension(filePath);
                var chunks = ChunkerRegistry.GetChunker(ext).Chunk(filePath, content).ToList();
                if (chunks.Count == 0) continue;

                // Batch-embed in groups of 100 to respect Gemini rate limits
                foreach (var batch in chunks.Chunk(100))
                {
                    float[][] embeddings;
                    try
                    {
                        embeddings = await embeddingApi.EmbedBatchAsync(
                            batch.Select(c => c.Text).ToArray(), ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Embedding batch failed for {File} — skipping batch", relative);
                        break;
                    }

                    for (int i = 0; i < batch.Length; i++)
                    {
                        db.CodeEmbeddings.Add(new CodeEmbedding
                        {
                            FilePath  = relative,
                            ChunkType = batch[i].ChunkType,
                            StartLine = batch[i].StartLine,
                            ChunkText = batch[i].Text.Length <= 3000
                                        ? batch[i].Text
                                        : batch[i].Text[..3000],
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
                _logger.LogWarning(ex, "Failed to index {File}", filePath);
                failed++;
            }
        }

        _logger.LogInformation(
            "Codebase indexing complete: {Indexed} indexed, {Skipped} unchanged, {Failed} failed",
            indexed, skipped, failed);
    }

    private IEnumerable<string> DiscoverFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;

        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');
            var parts = normalized.Split('/');

            // Skip blacklisted directories
            if (parts.Any(p => SkipDirs.Contains(p, StringComparer.OrdinalIgnoreCase)))
                continue;

            // Skip blacklisted extensions
            var ext = Path.GetExtension(file);
            if (SkipExtensions.Any(e => string.Equals(ext, e, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Skip minified files by name pattern
            var name = Path.GetFileName(file);
            if (name.Contains(".min.", StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip oversized files
            try
            {
                if (new FileInfo(file).Length > MaxFileSizeBytes) continue;
            }
            catch { continue; }

            yield return file;
        }
    }

    private static string ComputeMd5(string filePath)
    {
        using var md5    = MD5.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(md5.ComputeHash(stream));
    }

    private static string GetRelativePath(string root, string filePath)
    {
        try { return Path.GetRelativePath(root, filePath).Replace('\\', '/'); }
        catch { return filePath; }
    }
}
