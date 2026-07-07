using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Services.Ai;
using System.Security.Cryptography;
using System.Collections.Concurrent;

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

    private readonly ConcurrentDictionary<int, IndexingProgress> _progressMap = new();

    public class IndexingProgress
    {
        public int ProjectId { get; set; }
        public string Status { get; set; } = "NotStarted"; // InProgress, Completed, Failed
        public string? ErrorMessage { get; set; }
        public int TotalFiles { get; set; }
        public int ProcessedFiles { get; set; }
    }

    public IndexingProgress? GetProgress(int projectId)
    {
        return _progressMap.TryGetValue(projectId, out var progress) ? progress : null;
    }

    // Skip directories
    private static readonly string[] SkipDirs =
        ["bin", "obj", "node_modules", ".git", ".vs", "wwwroot/lib", "wwwroot\\lib", "Migrations"];

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

    /// <summary>Hosted service startup — no-op as indexing is project-specific and manual/lazy.</summary>
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Traverses a specific project's repository, chunks changed files, embeds using Gemini text-embedding-004,
    /// and persists to the database.
    /// </summary>
    public async Task IndexProjectAsync(int projectId, CancellationToken ct)
    {
        var progress = new IndexingProgress { ProjectId = projectId, Status = "InProgress" };
        _progressMap[projectId] = progress;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db           = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var embeddingApi = scope.ServiceProvider.GetRequiredService<IGeminiEmbeddingService>();

            var project = await db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == projectId, ct);
            if (project == null)
            {
                _logger.LogWarning("Project {ProjectId} not found for indexing.", projectId);
                progress.Status = "Failed";
                progress.ErrorMessage = "Project not found.";
                return;
            }

            var tenantProvider = scope.ServiceProvider.GetService<OfficeTaskManagement.Services.ITenantProvider>();
            if (tenantProvider != null)
            {
                tenantProvider.SetTenant(project.TenantId);
            }

            var repoRoot = project.RepositoryPath;
            if (string.IsNullOrEmpty(repoRoot))
            {
                repoRoot = ".";
            }

            var actualPath = repoRoot;
            if (!Path.IsPathRooted(actualPath))
            {
                actualPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), actualPath));
            }

            _logger.LogInformation("Starting codebase indexing for Project {ProjectId} ({ProjectName}) from: {Root}", 
                projectId, project.Name, actualPath);

            if (!Directory.Exists(actualPath))
            {
                _logger.LogWarning("Repository path '{Path}' does not exist for project {ProjectId}.", actualPath, projectId);
                progress.Status = "Failed";
                progress.ErrorMessage = "Repository directory does not exist.";
                return;
            }

            var files = DiscoverFiles(actualPath).ToList();
            _logger.LogInformation("Discovered {Count} files to consider for Project {ProjectId}", files.Count, projectId);
            progress.TotalFiles = files.Count;

            int indexed = 0, skipped = 0, failed = 0;
            int consecutiveRateLimits = 0;
            bool aborted = false;

            foreach (var filePath in files)
            {
                if (ct.IsCancellationRequested || aborted) break;
                try
                {
                    var fileHash = ComputeMd5(filePath);
                    var relative = GetRelativePath(actualPath, filePath);

                    // Skip if file hasn't changed since last indexing for this project
                    if (await db.CodeEmbeddings.AnyAsync(
                        e => e.ProjectId == projectId && e.FilePath == relative && e.FileHash == fileHash, ct))
                    {
                        skipped++;
                        progress.ProcessedFiles++;
                        continue;
                    }

                    // Remove old chunks for this file under this project
                    var old = db.CodeEmbeddings.Where(e => e.ProjectId == projectId && e.FilePath == relative);
                    db.CodeEmbeddings.RemoveRange(old);

                    var content = await File.ReadAllTextAsync(filePath, ct);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        progress.ProcessedFiles++;
                        continue;
                    }

                    var ext    = Path.GetExtension(filePath);
                    var chunks = ChunkerRegistry.GetChunker(ext).Chunk(filePath, content).ToList();
                    if (chunks.Count == 0)
                    {
                        progress.ProcessedFiles++;
                        continue;
                    }

                    // Batch-embed in groups of 20 to respect Gemini rate limits
                    foreach (var batch in chunks.Chunk(20))
                    {
                        if (ct.IsCancellationRequested) break;
                        float[][] embeddings;
                        try
                        {
                            embeddings = await embeddingApi.EmbedBatchAsync(
                                batch.Select(c => c.Text).ToArray(), ct);
                            
                            consecutiveRateLimits = 0; // reset on success
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Embedding batch failed for {File} — skipping batch", relative);
                            if (ex.Message.Contains("429") || (ex.InnerException != null && ex.InnerException.Message.Contains("429")))
                            {
                                consecutiveRateLimits++;
                                if (consecutiveRateLimits >= 3)
                                {
                                    _logger.LogError("Indexing aborted for Project {ProjectId}: Gemini API rate limit or quota exceeded (429) consecutively.", projectId);
                                    progress.Status = "Failed";
                                    progress.ErrorMessage = "Gemini API rate limit or quota exceeded (429).";
                                    aborted = true;
                                    break;
                                }
                            }
                            break; // break the batch loop for this file
                        }

                        for (int i = 0; i < batch.Length; i++)
                        {
                            db.CodeEmbeddings.Add(new CodeEmbedding
                            {
                                TenantId  = project.TenantId,
                                ProjectId = projectId,
                                FilePath  = relative,
                                ChunkType = batch[i].ChunkType,
                                StartLine = batch[i].StartLine,
                                ChunkText = batch[i].Text.Length <= 3000
                                            ? batch[i].Text
                                            : batch[i].Text[..3000],
                                Embedding = new Pgvector.Vector(embeddings[i]),
                                FileHash  = fileHash
                            });
                        }
                        await db.SaveChangesAsync(ct);
                        
                        // Space out requests to respect Gemini API rate limits
                        await Task.Delay(1500, ct);
                    }
                    
                    if (aborted) break;

                    indexed++;
                    progress.ProcessedFiles++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to index {File}", filePath);
                    failed++;
                    progress.ProcessedFiles++;
                }
            }

            if (aborted)
            {
                _logger.LogWarning("Codebase indexing aborted for Project {ProjectId} due to rate limits.", projectId);
            }
            else
            {
                progress.Status = "Completed";
                _logger.LogInformation(
                    "Codebase indexing complete for Project {ProjectId}: {Indexed} indexed, {Skipped} unchanged, {Failed} failed",
                    projectId, indexed, skipped, failed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IndexProjectAsync crashed for project {ProjectId}", projectId);
            progress.Status = "Failed";
            progress.ErrorMessage = ex.Message;
        }
    }

    /// <summary>
    /// Purges all codebase index entries for a specific project.
    /// </summary>
    public async Task PurgeProjectIndexAsync(int projectId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var old = db.CodeEmbeddings.Where(e => e.ProjectId == projectId);
        db.CodeEmbeddings.RemoveRange(old);
        await db.SaveChangesAsync();

        _logger.LogInformation("Purged all codebase index entries for Project {ProjectId}", projectId);
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
