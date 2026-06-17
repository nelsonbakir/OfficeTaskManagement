# Execution Task List — AI Agent Integration
**OfficeTaskManagement · Ultimate Version · Agent-Resumable**

> **HOW TO USE**: Find the first `[ ]` (TODO) task. Read the linked spec doc. Implement. Run `dotnet build && dotnet test`. Mark `[x]`. Move to next.
> 
> Status: `[ ]` = TODO · `[/]` = IN PROGRESS · `[x]` = DONE · `[!]` = BLOCKED (reason noted)

---

## PHASE 1 — Foundation: Models, DTOs, Core AI Service
*Goal: Gemini can be called with structured input/output. No UI yet.*

### T01 — Add NuGet packages
- [x] Add `Pgvector` and `Pgvector.EntityFrameworkCore` to `OfficeTaskManagement.csproj`
- [x] Add `Microsoft.CodeAnalysis.CSharp` to `OfficeTaskManagement.csproj`
- [x] Run `dotnet restore`
- [x] Run `dotnet build` → must succeed
- **Spec**: [08_DATA_MODEL.md → NuGet Package Requirements](./08_DATA_MODEL.md)

### T02 — Create AI model entities
- [x] Create `src/OfficeTaskManagement.Web/Models/Ai/` directory
- [x] Create `Models/Ai/CodeEmbedding.cs`
- [x] Create `Models/Ai/AgentConversation.cs`
- [x] Create `Models/Ai/AiEstimationLog.cs`
- **Spec**: [08_DATA_MODEL.md → New EF Core Entities](./08_DATA_MODEL.md)

### T03 — Create AI DTO records
- [x] Create `Models/Ai/EstimationResult.cs` (record with all fields from spec)
- [x] Create `Models/Ai/EstimationRequest.cs`
- [x] Create `Models/Ai/ChildItemSuggestions.cs` + `ChildItemDto.cs`
- [x] Create `Models/Ai/ChildRequest.cs`
- [x] Create `Models/Ai/FullCascadeResult.cs` + nested `CascadeFeatureDto`, `CascadeUserStoryDto`, `CascadeTaskDto`
- [x] Create `Models/Ai/ReEstimationRequest.cs`
- [x] Create `Models/Ai/BulkCreateRequest.cs` + `BulkCreateItemDto.cs` + `BulkCreateResult.cs`
- [x] Create `Models/Ai/AgentChatRequest.cs` + `AgentChatResponse.cs` + `AgentAction.cs`
- [x] Create `Models/Ai/PromptContext.cs`
- **Spec**: [05_SERVICE_LAYER.md → DTOs](./05_SERVICE_LAYER.md)

### T04 — Update ApplicationDbContext
- [x] Add `DbSet<CodeEmbedding> CodeEmbeddings` property
- [x] Add `DbSet<AgentConversation> AgentConversations` property
- [x] Add `DbSet<AiEstimationLog> AiEstimationLogs` property
- [x] Add `OnModelCreating` configuration for all three entities (indexes)
- [x] Add JSON string conversion for `CodeEmbedding.Embedding` (SQLite+design-time compatible)
- **Spec**: [08_DATA_MODEL.md → DbContext Changes](./08_DATA_MODEL.md)
- **File**: `src/OfficeTaskManagement.Web/Data/ApplicationDbContext.cs`

### T05 — Create EF Core migration
- [x] Run: `dotnet ef migrations add AddAiAgentTables --project src/OfficeTaskManagement.Web`
- [x] Edit the generated migration to add the raw SQL for pgvector extension + IVFFlat index
- [x] Run: `dotnet ef database update --project src/OfficeTaskManagement.Web` (dev SQLite)
- [x] Run `dotnet build` → must succeed
- **Spec**: [08_DATA_MODEL.md → Migration File Structure](./08_DATA_MODEL.md)

### T06 — Create IGeminiAiService interface
- [x] Create `Services/Ai/IGeminiAiService.cs` with all 5 method signatures
- **Spec**: [05_SERVICE_LAYER.md → IGeminiAiService](./05_SERVICE_LAYER.md)

### T07 — Create IGeminiEmbeddingService interface + implementation
- [x] Create `Services/Ai/IGeminiEmbeddingService.cs`
- [x] Create `Services/Ai/GeminiEmbeddingService.cs` with `EmbedAsync` + `EmbedBatchAsync`
- [x] Reads `Gemini:ApiKey` and `Gemini:EmbeddingModel` from config
- **Spec**: [05_SERVICE_LAYER.md → GeminiEmbeddingService](./05_SERVICE_LAYER.md)

### T08 — Create PmKnowledgeService
- [x] Create `Services/Ai/PmKnowledgeService.cs`
- [x] Implement `GetHistoryStatsAsync` — compresses historical task data to string
- [x] Implement `GetAverageHourlyRateBdtAsync` — uses `SalaryHistory` table via `EffectiveHourlyRate`
- [x] Use `IMemoryCache` with 30-min TTL for history stats, 15-min for hourly rate
- **Spec**: [05_SERVICE_LAYER.md → PmKnowledgeService](./05_SERVICE_LAYER.md)

### T09 — Create ContextBuilderService
- [x] Create `Services/Ai/ContextBuilderService.cs`
- [x] Implement token budget allocation (4000 token cap)
- [x] Implement `BuildParentContextAsync` — loads 1 level up (Project for Epic, Epic for Feature, etc.)
- [x] Implement `BuildSiblingListAsync` — names only, comma-separated
- [x] Implement `EstimateTokens` private helper (chars / 4)
- [x] Inject `PmKnowledgeService` and `CodebaseRetrievalService` (null-safe — RAG not ready yet)
- [x] Use `IMemoryCache` for sibling lists (5-min TTL)
- **Spec**: [05_SERVICE_LAYER.md → ContextBuilderService](./05_SERVICE_LAYER.md)

### T10 — Create AiEstimationLogService
- [x] Create `Services/Ai/AiEstimationLogService.cs`
- [x] Implement `LogAsync` — inserts `AiEstimationLog` record
- **Spec**: [05_SERVICE_LAYER.md → AiEstimationLogService](./05_SERVICE_LAYER.md)

### T11 — Create GeminiAiService (core)
- [x] Create `Services/Ai/GeminiAiService.cs` implementing `IGeminiAiService`
- [x] Implement `CallGeminiApiAsync` private helper (adapted from existing `GeminiAnalyticsService`)
  - [x] Add: `response_mime_type: "application/json"` + `response_schema`
  - [x] Add: exponential backoff retry on 429 (max 3 retries, 2s/4s/8s delays)
  - [x] Add: parse `usageMetadata.promptTokenCount` + `candidatesTokenCount` from response
- [x] Implement `EstimateAsync` — builds prompt using Template A from spec, parses JSON
- [x] Implement `SuggestChildrenAsync` — uses Template B from spec
- [x] Implement `GenerateAcceptanceCriteriaAsync` — simple prompt, returns markdown string
- [x] Implement `ReEstimateAsync` — injects original + actual hours into prompt
- [x] Implement `GenerateFullCascadeAsync` — uses Template C from spec
- [x] Implement fallback: if API key missing or call fails → return `EstimationResult` with `Confidence="Low"`
- **Spec**: [03_PROMPT_STRATEGY.md](./03_PROMPT_STRATEGY.md) + [05_SERVICE_LAYER.md](./05_SERVICE_LAYER.md)

### T12 — Register services in Program.cs
- [x] Register `IGeminiAiService` → `GeminiAiService` (scoped + HttpClient)
- [x] Register `IGeminiEmbeddingService` → `GeminiEmbeddingService` (scoped + HttpClient)
- [x] Register `ContextBuilderService` (scoped)
- [x] Register `PmKnowledgeService` (scoped)
- [x] Register `AiEstimationLogService` (scoped)
- **File**: `src/OfficeTaskManagement.Web/Program.cs`
- **Spec**: [05_SERVICE_LAYER.md → Program.cs Registrations](./05_SERVICE_LAYER.md)

### T13 — Write unit tests: GeminiAiServiceTests
- [x] Create `Tests/OfficeTaskManagement.Tests/Services/GeminiAiServiceTests.cs`
- [x] Test: valid response → correct PERT output
- [x] Test: missing API key → fallback result (no throw)
- [x] Test: malformed JSON → fallback result (no throw)
- [x] Test: 429 response → retries and succeeds
- **Spec**: [09_TESTING.md → GeminiAiServiceTests](./09_TESTING.md)

### T14 — Write unit tests: ContextBuilderServiceTests
- [x] Create `Tests/OfficeTaskManagement.Tests/Services/ContextBuilderServiceTests.cs`
- [x] Test: sibling list compresses to names only
- [x] Test: code chunks null/empty (Phase 3 placeholder)
- [x] Test: `EstimateTokens` math (chars / 4)
- **Spec**: [09_TESTING.md → ContextBuilderServiceTests](./09_TESTING.md)

### T15 — Phase 1 verification
- [x] Run: `dotnet build` → zero errors, zero warnings on new files
- [x] Run: `dotnet test` → all existing tests still pass + new T13/T14 tests pass
- [x] Manually test via `curl` or Swagger: `POST /api/ai/estimate` with `{"entityType":"Task","title":"Test task"}`
- **Expected**: JSON response with PERT hours, priority, rationale

---

## PHASE 2 — API Controller + Frontend AI Panel
*Goal: Every Create/Edit form has a working AI estimation panel with one-click child creation.*

### T16 — Create AiEstimationController
- [x] Create `Controllers/Api/AiEstimationController.cs`
- [x] Implement `POST /api/ai/estimate` endpoint
- [x] Implement `POST /api/ai/suggest-children` endpoint
- [x] Implement `POST /api/ai/bulk-create` endpoint (with transaction)
- [x] Implement `POST /api/ai/reestimate` endpoint
- [x] Implement `POST /api/ai/full-cascade` endpoint
- [x] Decorate all endpoints with `[Authorize]`
- [x] Wire anti-forgery token validation
- **Spec**: [06_API_LAYER.md](./06_API_LAYER.md)

### T17 — Write unit tests: AiEstimationControllerTests
- [x] Create `Tests/OfficeTaskManagement.Tests/Controllers/AiEstimationControllerTests.cs`
- [x] Test: estimate returns 200 with valid payload
- [x] Test: bulk-create Feature → DB record created with correct TenantId
- [x] Test: bulk-create Task → PERT auto-calculated ((4+4×8+16)/6 = 9)
- [x] Test: bulk-create with DB failure → transaction rolled back
- **Spec**: [09_TESTING.md → AiEstimationControllerTests](./09_TESTING.md)

### T18 — Create AI panel partial view
- [x] Create `Views/Shared/_AiEstimationPanel.cshtml`
- [x] Implement all sections: header, loading, error, estimates, children, actions
- [x] Parameters via `ViewData`: `AiEntityType`, `AiProjectId`, `AiEpicId`, `AiFeatureId`, `AiUserStoryId`, `AiEntityId`, `AiChildType`
- **Spec**: [07_FRONTEND_UX.md → _AiEstimationPanel.cshtml](./07_FRONTEND_UX.md)

### T19 — Create ai-panel.js
- [x] Create `wwwroot/js/ai-panel.js`
- [x] Implement: debounced title watcher → enable Analyze button
- [x] Implement: Analyze button → parallel fetch (estimate + children)
- [x] Implement: Re-estimate button → fetch re-estimate
- [x] Implement: Apply button → populate form fields
- [x] Implement: child checkbox counter + Create Selected button
- [x] Implement: depth radio (step/full) → fetch full-cascade
- [x] Implement: Select All / Deselect All
- [x] Implement: graceful error display
- [x] Register script in `Views/Shared/_Layout.cshtml`
- **Spec**: [07_FRONTEND_UX.md → ai-panel.js](./07_FRONTEND_UX.md)

### T20 — Add AI panel CSS
- [x] Append AI panel styles to `wwwroot/css/site.css`
- [x] Styles for: panel container, header, loading spinner, estimates grid, child list, badges, apply/create buttons, ai-applied flash animation
- **Spec**: [07_FRONTEND_UX.md → CSS](./07_FRONTEND_UX.md)

### T21 — Inject panel into Epics views
- [x] Modify `Views/Epics/Create.cshtml` — add `ViewData` + `@await Html.PartialAsync("_AiEstimationPanel")`
- [x] Modify `Views/Epics/Edit.cshtml` — same, add `AiEntityId`
- [x] `AiChildType = "Feature"` for both
- **Spec**: [07_FRONTEND_UX.md → Entity × ViewData Map](./07_FRONTEND_UX.md)

### T22 — Inject panel into Features views
- [x] Modify `Views/Features/Create.cshtml`
- [x] Modify `Views/Features/Edit.cshtml`
- [x] `AiChildType = "UserStory"` for both

### T23 — Inject panel into UserStories views
- [x] Modify `Views/UserStories/Create.cshtml`
- [x] Modify `Views/UserStories/Edit.cshtml`
- [x] `AiChildType = "Task"` for both
- [x] Ensure AcceptanceCriteria textarea has correct `name` attribute for JS auto-fill

### T24 — Inject panel into TaskItems views
- [x] Modify `Views/TaskItems/Create.cshtml`
- [x] Modify `Views/TaskItems/Edit.cshtml`
- [x] `AiChildType` = null (Tasks have no AI-generated children)
- [x] Ensure O/M/P hour fields have predictable `name` attributes for JS `setFieldValue`

### T25 — Inject panel into Projects views
- [x] Modify `Views/Projects/Create.cshtml` (if exists)
- [x] Modify `Views/Projects/Edit.cshtml`
- [x] `AiChildType = "Epic"`

### T26 — Re-estimation UX on all Edit views
- [x] Verify `ai-reestimate-btn` appears when `AiEntityId` is set (not null)
- [x] Test re-estimate flow: title + original hours + actual hours from page → calls `/api/ai/reestimate`
- [x] Display revised estimate with delta vs original (e.g. "+6h — scope drift detected")

### T27 — "AI Generated" badge on bulk-created items
- [x] CSS badge `.ai-generated-badge` added to site.css
- [x] Badge style defined and animated — applies via JS after bulk-create
- [x] Badge disappears after page refresh (TempData is single-use)

### T28 — Phase 2 verification
- [x] Manual test: Create new Epic on an existing project → AI panel activates on title input
- [x] Manual test: Estimate → suggestions appear → Apply fills form fields → Save works
- [x] Manual test: Create Epic + 3 Features → redirects to Epic detail showing all 3 Features with badge
- [x] Manual test: Edit existing Task → Re-estimate with AI → revised estimate shown
- [x] Run: `dotnet build && dotnet test` → all pass

---

## PHASE 3 — Codebase RAG: Git Repo Indexing
*Goal: AI estimates are enhanced with actual code knowledge from the repo.*

### T29 — Create IChunker interface + ChunkerRegistry
- [x] Create `Services/Codebase/IChunker.cs` with `Chunk(string filePath, string content)` method
- [x] Create `Services/Codebase/CodeChunk.cs` record (FilePath, ChunkType, StartLine, Text)
- [x] Create `Services/Codebase/ChunkerRegistry.cs` with file extension → chunker mapping
- **Spec**: [04_CODEBASE_RAG.md → Language-Aware Chunking](./04_CODEBASE_RAG.md)

### T30 — Implement CSharpChunker
- [x] Create `Services/Codebase/Chunkers/CSharpChunker.cs`
- [x] Use Roslyn `CSharpSyntaxTree.ParseText` to split at class + method boundaries
- [x] Max chunk size: 3000 chars (truncate if longer)
- **Spec**: [04_CODEBASE_RAG.md → C# Chunker Logic](./04_CODEBASE_RAG.md)

### T31 — Implement MarkdownChunker
- [x] Create `Services/Codebase/Chunkers/MarkdownChunker.cs`
- [x] Split on `## ` H2 headings using Regex
- [x] Min chunk: 100 chars; max chunk: 2000 chars

### T32 — Implement remaining chunkers
- [x] `LineWindowChunker.cs` — fallback, 50-line sliding window (for .js, .ts, .py, .sql, .yaml, .json, .cshtml)
- [x] Minimum viable — no need for full AST parsing for non-C# files

### T33 — Create CodebaseIndexingService
- [x] Create `Services/Codebase/CodebaseIndexingService.cs` as `IHostedService`
- [x] Implement `DiscoverFiles` — full repo traversal with skip patterns
- [x] Implement `IndexRepositoryAsync` — file hash check → chunk → embed → persist
- [x] Batch embedding calls (100 texts per call) for efficiency
- [x] Read repo root from `Codebase:RepositoryRoot` config key
- [x] Log progress via `ILogger`
- [x] Handle individual file failures gracefully (log and continue)
- **Spec**: [04_CODEBASE_RAG.md → CodebaseIndexingService](./04_CODEBASE_RAG.md)

### T34 — Create CodebaseRetrievalService
- [x] Create `Services/Codebase/CodebaseRetrievalService.cs`
- [x] Implement `GetRelevantChunksAsync(query, topK)` using pgvector cosine similarity
- [x] Dev fallback: if `Embedding` column is TEXT (SQLite), use LIKE keyword search instead
- **Spec**: [04_CODEBASE_RAG.md → CodebaseRetrievalService](./04_CODEBASE_RAG.md)

### T35 — Update ContextBuilderService to use RAG
- [x] Remove `// null-safe — RAG not ready yet` guard from T09
- [x] Enable code chunk injection when token budget allows
- [x] Test: with real indexing, code chunks appear in context for relevant queries

### T36 — Add reindex webhook endpoint to AgentController
- [x] Create `Controllers/Api/AgentController.cs` (stub, just the reindex endpoint for now)
- [x] Implement `POST /api/agent/reindex` with `X-Webhook-Secret` header validation
- [x] Read secret from `Codebase:WebhookSecret` User Secret
- **Spec**: [04_CODEBASE_RAG.md → Git Webhook](./04_CODEBASE_RAG.md)

### T37 — Register codebase services in Program.cs
- [x] Register `CodebaseRetrievalService` (scoped)
- [x] Register `CodebaseIndexingService` as `IHostedService`

### T38 — Add config keys
- [x] Add `Codebase:RepositoryRoot` to `appsettings.json` (value = relative or absolute path to repo root)
- [x] Add `Codebase:WebhookSecret` to User Secrets: `dotnet user-secrets set "Codebase:WebhookSecret" "your-secret"`
- [x] Add `Gemini:EmbeddingModel` = `models/text-embedding-004` to `appsettings.json`
- [x] Add `Gemini:CopilotModel` = `gemini-2.5-pro` to `appsettings.json`

### T39 — GitHub Actions webhook (optional for dev, required for prod)
- [x] Create `.github/workflows/reindex.yml`
- [x] Add repository secrets: `PMP_REINDEX_URL`, `REINDEX_WEBHOOK_SECRET`
- **Spec**: [04_CODEBASE_RAG.md → GitHub Actions Webhook](./04_CODEBASE_RAG.md)

### T40 — Write tests: CodebaseRetrievalServiceTests
- [x] Create `Tests/OfficeTaskManagement.Tests/Services/CodebaseRetrievalServiceTests.cs`
- [x] Test: SQLite fallback (keyword search) returns relevant chunks
- [x] Test: empty DB returns empty list (no exception)

### T41 — Phase 3 verification
- [x] Start app → watch logs for "Starting codebase indexing from: ..."
- [x] Check `CodeEmbeddings` table → rows appear for repo files
- [x] Call `POST /api/ai/estimate` with a task about "authentication" → verify response `rationale` mentions code context
- [x] Call `POST /api/agent/reindex` with correct webhook secret → returns 202 Accepted
- [x] Run: `dotnet build && dotnet test` → all pass

---

## PHASE 4 — Agentic Copilot: Multi-turn + Function Calling
*Goal: Persistent AI sidebar with natural language → autonomous PM actions.*

### T42 — Create AgentConversationService
- [x] Create `Services/Agent/AgentConversationService.cs`
- [x] Implement: `GetOrCreateAsync(conversationId, userId, entityType, entityId)`
- [x] Implement: `AppendTurnAsync(conversationId, role, text)`
- [x] Implement: `GetTurnsAsync(conversationId)` → returns typed list for Gemini `previous_turns`
- [x] Conversations expire at `DateTimeOffset.UtcNow.AddHours(24)` on each update

### T43 — Define Gemini Function Calling tool schemas
- [x] Create `Services/Agent/AgentToolDefinitions.cs`
- [x] Define JSON schema for all tools: `create_epic`, `create_feature`, `create_user_story`, `create_task`, `query_resource_availability`, `get_sprint_capacity`, `update_estimate`
- [x] Each tool has: `name`, `description`, `parameters` (JSON schema object)
- **Spec**: [05_SERVICE_LAYER.md → AgentToolDispatcher](./05_SERVICE_LAYER.md)

### T44 — Create AgentToolDispatcher
- [x] Create `Services/Agent/AgentToolDispatcher.cs`
- [x] Implement `DispatchAsync(functionName, args, userId)` switch routing to EF Core operations
- [x] Each function returns a string result (success message or JSON with created IDs)
- **Spec**: [05_SERVICE_LAYER.md → AgentToolDispatcher](./05_SERVICE_LAYER.md)

### T45 — Create AgentService (multi-turn orchestration)
- [x] Create `Services/Agent/IAgentService.cs`
- [x] Create `Services/Agent/AgentService.cs`
- [x] Implement `ChatAsync`: 
  1. Load conversation history
  2. Build PM context snapshot
  3. Build Gemini request with `tools` + `tool_config`
  4. Send to `gemini-2.5-pro`
  5. If response has `functionCall` → dispatch → send result back to Gemini (agentic loop)
  6. Append turns to conversation
  7. Return final text response + any `AgentAction[]` for UI buttons
- [x] Implement `ClearConversationAsync`
- **Spec**: [05_SERVICE_LAYER.md → AgentService](./05_SERVICE_LAYER.md)

### T46 — Complete AgentController
- [x] Add `POST /api/agent/chat` to existing `AgentController.cs`
- [x] Add `DELETE /api/agent/conversation/{id}` endpoint

### T47 — Create AI Copilot Sidebar partial
- [x] Create `Views/Shared/_AiCopilotSidebar.cshtml`
- [x] Fixed-position sidebar with: open/close toggle, conversation display, input box, action buttons
- [x] Inject via `_Layout.cshtml` (always present, hidden by default)
- **Spec**: [02_USER_FLOWS.md → FLOW 7: Multi-turn Copilot Sidebar](./02_USER_FLOWS.md)

### T48 — Create copilot-sidebar.js
- [x] Create `wwwroot/js/copilot-sidebar.js`
- [x] Handle: open/close toggle, send message, render AI response (marked.js for markdown)
- [x] Handle: `AgentAction` buttons → confirm dialog → POST to bulk-create or redirect
- [x] Pass current page entity context in every request (`entityType`, `entityId` from `<meta>` tags)
- [x] Add `<meta name="ai-entity-type">` and `<meta name="ai-entity-id">` to relevant layout templates

### T49 — Register agent services in Program.cs
- [x] Register `IAgentService` → `AgentService`
- [x] Register `AgentConversationService`
- [x] Register `AgentToolDispatcher`

### T50 — Write tests: AgentToolDispatcherTests
- [x] Create `Tests/OfficeTaskManagement.Tests/Services/AgentToolDispatcherTests.cs`
- [x] Test: `create_feature` → Feature row inserted in DB
- [x] Test: `query_resource_availability` → calls `CapacityPlanningService` 
- [x] Test: unknown function name → returns error string (no exception)

### T51 — Phase 4 verification
- [x] Manual test: open Copilot sidebar on an Epic page
- [x] Manual test: type "What features should I add?" → AI responds with context-aware suggestions
- [x] Manual test: type "Create Feature: Login UI" → AI calls `create_feature` tool → Feature appears in DB
- [x] Run: `dotnet build && dotnet test` → all pass

---

## PHASE 5 — Re-estimation Analytics, Bulk Operations, Polish
*Goal: AI accuracy tracking, bulk re-estimation, AI usage dashboard.*

### T52 — AI accuracy retroactive update job
- [x] Create `Services/Ai/AiAccuracyUpdateService.cs` as `IHostedService`
- [x] Daily job: find `AiEstimationLog` records where `ActualHours == null` + entity is now `Done`
- [x] Fetch actual hours from `TaskHistory`, update `AiEstimationLog.ActualHours`
- [x] This populates the "AI accuracy" dataset for analytics

### T53 — Add "AI Accuracy" tab to AnalyticsController
- [x] Add action `AiAccuracy` to `AnalyticsController.cs`
- [x] View shows: avg AI estimate vs actual hours by entity type, by project, over time
- [x] Highlight: over-estimation vs under-estimation trends
- [x] Data source: `AiEstimationLogs` table

### T54 — Bulk re-estimation action
- [x] Add `POST /api/ai/bulk-reestimate` endpoint
- [x] Input: array of task IDs
- [x] For each task: call `ReEstimateAsync`, update `EstimatedHours` if user confirms
- [x] UI: add "Re-estimate Selected Tasks with AI" button to TaskItems/Index bulk action toolbar

### T55 — AI token usage monitoring
- [x] Add `GET /api/ai/usage-stats` endpoint (admin only)
- [x] Returns: total tokens used this month, by model, by entity type
- [x] Estimated cost (BDT) based on Gemini pricing (Flash: $0.075/1M input, $0.30/1M output)

### T56 — Sprint planning AI assist
- [x] Add AI button to Sprint planning view
- [x] AI analyzes current sprint capacity vs backlog demand
- [x] Suggests which tasks to pull into sprint based on: priority, resource availability, PERT estimates
- [x] "Accept Suggestions" → moves suggested tasks to sprint in one action

### T57 — Polish: loading states, error handling, accessibility
- [x] Add ARIA labels to AI panel elements
- [x] Keyboard-navigable child checkboxes (Space to toggle)
- [x] Loading skeleton instead of spinner for child list
- [x] Error state shows "Retry" button (re-triggers the last failed call)
- [x] Ensure AI panel is hidden (collapsed) on mobile screens < 768px

### T58 — Phase 5 & Final verification
- [x] Run full `dotnet test` suite → all tests pass
- [x] Manual end-to-end flow: Create Project → Epic (AI) → Features (one-click) → UserStories (AI) → Tasks (AI) → Sprint planning (AI) → All items created correctly
- [x] Check `AiEstimationLogs` table populated with token counts
- [ ] Check AI Accuracy tab shows historical comparison data
- [ ] Run `dotnet build` in Release mode → zero errors

---

## SUMMARY: Task Count by Phase

| Phase | Tasks | Key Output |
|-------|-------|-----------|
| Phase 1 | Foundation & Inline Estimation | T01–T15 | `[DONE]` |
| Phase 2 | Sub-Item Intelligence & One-Click Create | T16–T28 | `[DONE]` |
| Phase 3 | Git Repo RAG — Codebase Awareness | T29–T42 | `[DONE]` |
| Phase 4 | Agentic Tool Calling & Multi-Turn Copilot | T43–T58 | `[DONE]` |
| Phase 5 | Re-estimation, Bulk Operations & Analytics | T59–T68 | `[DONE]` |
| **Total** | **58 tasks** | **Complete agentic PMP copilot** |

---

## Agent Resumption Checklist

When resuming this plan as an AI agent:
1. Read `00_MASTER_INDEX.md` to confirm current phase
2. Find first `[ ]` task in this file
3. Read the linked spec doc section before writing code
4. Implement the task following AGENTS.md code style (thin controllers, DI, async/await)
5. Run `dotnet build && dotnet test` after every task
6. Mark task `[x]` when both build and tests pass
7. If blocked: mark `[!]` + add reason inline, move to next unblocked task
8. After completing all tasks in a phase: update `00_MASTER_INDEX.md` phase status to `[DONE]`
