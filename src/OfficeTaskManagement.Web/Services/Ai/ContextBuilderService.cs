using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Services.Codebase;

namespace OfficeTaskManagement.Services.Ai
{
    /// <summary>
    /// Builds compressed, token-budget-aware context snapshots for AI estimation prompts.
    /// Implements the context budget allocation from 03_PROMPT_STRATEGY.md:
    ///   - Parent context:   ~400 tokens
    ///   - Sibling list:     ~600 tokens
    ///   - Historical stats: ~500 tokens
    ///   - Code chunks:      ~1,500 tokens (Phase 3+ only)
    ///   - Total cap:        4,000 tokens
    /// </summary>
    public class ContextBuilderService
    {
        private readonly ApplicationDbContext _db;
        private readonly PmKnowledgeService _pmKnowledge;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ContextBuilderService> _logger;
        private readonly CodebaseRetrievalService? _codebaseRetrieval;

        private const int MaxTotalTokens = 4000;

        public ContextBuilderService(
            ApplicationDbContext db,
            PmKnowledgeService pmKnowledge,
            IMemoryCache cache,
            ILogger<ContextBuilderService> logger,
            CodebaseRetrievalService? codebaseRetrieval = null)
        {
            _db = db;
            _pmKnowledge = pmKnowledge;
            _cache = cache;
            _logger = logger;
            _codebaseRetrieval = codebaseRetrieval;
        }

        /// <summary>
        /// Builds a complete, token-budget-aware PromptContext for the given estimation request.
        /// Sections are injected in priority order and dropped if the budget is exhausted.
        /// </summary>
        public async Task<PromptContext> BuildContextAsync(
            EstimationRequest request, CancellationToken ct = default)
        {
            int tokenBudget = MaxTotalTokens;
            var ctx = new PromptContext();

            // 1. Parent context (~400 token budget)
            ctx.ParentContext = await BuildParentContextAsync(request, ct);
            tokenBudget -= EstimateTokens(ctx.ParentContext);

            // 2. Sibling list — names only (~600 token budget)
            ctx.SiblingList = await BuildSiblingListAsync(request, ct);
            tokenBudget -= EstimateTokens(ctx.SiblingList);

            // 3. Historical accuracy stats (~500 token budget)
            ctx.HistoryStats = await _pmKnowledge.GetHistoryStatsAsync(
                request.ProjectId, request.EntityType, ct);
            tokenBudget -= EstimateTokens(ctx.HistoryStats);

            // 4. Hourly rate (minimal token cost — just a number)
            ctx.HourlyRateBDT = request.ProjectId.HasValue
                ? await _pmKnowledge.GetAverageHourlyRateBdtAsync(request.ProjectId.Value)
                : 800m; // fallback BDT hourly rate

            // 5. Code context — inject if budget allows and RAG service is available (T35)
            if (tokenBudget > 600 && _codebaseRetrieval != null)
            {
                try
                {
                    var searchQuery = $"{request.EntityType}: {request.Title} {request.Description}";
                    ctx.CodeChunks = await _codebaseRetrieval.GetRelevantChunksAsync(
                        searchQuery, topK: 3, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Code chunk retrieval failed — proceeding without RAG context");
                    ctx.CodeChunks = null;
                }
            }
            else
            {
                ctx.CodeChunks = null;
            }

            return ctx;
        }

        /// <summary>
        /// Builds a compressed description of the parent entity (1 level up).
        /// E.g., for a Feature: loads the parent Epic's name + description (truncated).
        /// </summary>
        private async Task<string?> BuildParentContextAsync(
            EstimationRequest request, CancellationToken ct)
        {
            try
            {
                return request.EntityType switch
                {
                    "Epic" when request.ProjectId.HasValue => await BuildProjectContextAsync(request.ProjectId.Value, ct),
                    "Feature" when request.EpicId.HasValue => await BuildEpicContextAsync(request.EpicId.Value, ct),
                    "UserStory" when request.FeatureId.HasValue => await BuildFeatureContextAsync(request.FeatureId.Value, ct),
                    "Task" when request.UserStoryId.HasValue => await BuildUserStoryContextAsync(request.UserStoryId.Value, ct),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build parent context for {EntityType}", request.EntityType);
                return null;
            }
        }

        private async Task<string?> BuildProjectContextAsync(int projectId, CancellationToken ct)
        {
            var project = await _db.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == projectId, ct);
            if (project == null) return null;
            return $"Parent Project: {project.Name}\nDescription: {Truncate(project.Description)}";
        }

        private async Task<string?> BuildEpicContextAsync(int epicId, CancellationToken ct)
        {
            var epic = await _db.Epics
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == epicId, ct);
            if (epic == null) return null;
            return $"Parent Epic: {epic.Name}\nDescription: {Truncate(epic.Description)}";
        }

        private async Task<string?> BuildFeatureContextAsync(int featureId, CancellationToken ct)
        {
            var feature = await _db.Features
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == featureId, ct);
            if (feature == null) return null;
            return $"Parent Feature: {feature.Name}\nDescription: {Truncate(feature.Description)}";
        }

        private async Task<string?> BuildUserStoryContextAsync(int userStoryId, CancellationToken ct)
        {
            var story = await _db.UserStories
                .AsNoTracking()
                .FirstOrDefaultAsync(us => us.Id == userStoryId, ct);
            if (story == null) return null;
            return $"Parent User Story: {story.Title}\nDescription: {Truncate(story.Description)}";
        }

        /// <summary>
        /// Returns a comma-separated list of existing sibling entity names.
        /// No descriptions — compression rule from 03_PROMPT_STRATEGY.md.
        /// Cached for 5 minutes per parent entity.
        /// </summary>
        private async Task<string?> BuildSiblingListAsync(
            EstimationRequest request, CancellationToken ct)
        {
            try
            {
                (string label, string siblings) = request.EntityType switch
                {
                    "Epic" when request.ProjectId.HasValue =>
                        ("Existing epics in this project",
                         await GetSiblingNamesAsync("epics", request.ProjectId.Value, ct)),

                    "Feature" when request.EpicId.HasValue =>
                        ("Existing features in this epic",
                         await GetSiblingNamesAsync("features", request.EpicId.Value, ct)),

                    "UserStory" when request.FeatureId.HasValue =>
                        ("Existing user stories in this feature",
                         await GetSiblingNamesAsync("userstories", request.FeatureId.Value, ct)),

                    "Task" when request.UserStoryId.HasValue =>
                        ("Existing tasks in this user story",
                         await GetSiblingNamesAsync("tasks", request.UserStoryId.Value, ct)),

                    _ => (string.Empty, string.Empty)
                };

                return string.IsNullOrEmpty(siblings) ? null : $"{label}: {siblings}";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build sibling list for {EntityType}", request.EntityType);
                return null;
            }
        }

        private async Task<string> GetSiblingNamesAsync(
            string entityKind, int parentId, CancellationToken ct)
        {
            var cacheKey = $"siblings:{parentId}:{entityKind}";
            if (_cache.TryGetValue(cacheKey, out string? cached) && cached != null)
                return cached;

            string result = entityKind switch
            {
                "epics" => string.Join(", ", await _db.Epics
                    .Where(e => e.ProjectId == parentId)
                    .Select(e => e.Name)
                    .Take(20)
                    .ToListAsync(ct)),

                "features" => string.Join(", ", await _db.Features
                    .Where(f => f.EpicId == parentId)
                    .Select(f => f.Name)
                    .Take(20)
                    .ToListAsync(ct)),

                "userstories" => string.Join(", ", await _db.UserStories
                    .Where(us => us.FeatureId == parentId)
                    .Select(us => us.Title)
                    .Take(20)
                    .ToListAsync(ct)),

                "tasks" => string.Join(", ", await _db.Tasks
                    .Where(t => t.UserStoryId == parentId)
                    .Select(t => t.Title)
                    .Take(20)
                    .ToListAsync(ct)),

                _ => string.Empty
            };

            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
            return result;
        }

        /// <summary>
        /// Estimates the token count for a text string.
        /// Approximation: 1 token ≈ 4 characters (Gemini tokenization).
        /// </summary>
        internal static int EstimateTokens(string? text)
            => string.IsNullOrEmpty(text) ? 0 : text.Length / 4;

        /// <summary>
        /// Truncates a description to the specified max character length.
        /// Appends "..." when truncated.
        /// </summary>
        private static string Truncate(string? text, int maxChars = 400)
            => string.IsNullOrEmpty(text) ? ""
             : text.Length <= maxChars ? text
             : text[..maxChars] + "...";
    }
}
