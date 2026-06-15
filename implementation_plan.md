# AI Agent Integration — OfficeTaskManagement PMP Tool
### Comprehensive Implementation Plan · Two-Version Roadmap

> **Perspective**: World-class Architect × PMP Expert × AI/LLM Engineer × Product Owner  
> **Target System**: .NET 10 / ASP.NET Core MVC · Gemini AI · EF Core (SQLite/PostgreSQL)  
> **Current Gemini surface**: `GeminiAnalyticsService` — 4 analytics endpoints (delay prediction, burnout, retrospective, tech debt)

---

## Executive Summary

Your system already has a **Gemini API foundation** (`GeminiAnalyticsService`) and a **sophisticated domain model** (Project → Epic → Feature → UserStory → TaskItem with full PERT/RACI/WorkflowEngine). The opportunity is to evolve Gemini from a *reporting tool* into a **context-aware estimation agent** that is present at the point of creation — not just after the fact.

The plan is split into two versions:

| | **v1 — Immediate-Useful** | **v2 — Final-Ultimate** |
|---|---|---|
| **Philosophy** | AI assistance at point of entry | Full AI agency with memory & tool-calling |
| **Effort** | ~3 sprints (6 weeks) | ~6-8 months |
| **Value** | Instant estimation, sub-item generation | Codebase-aware, autonomous PM copilot |
| **Risk** | Low — additive to existing code | Medium — new infrastructure required |
| **Gemini Model** | `gemini-2.5-flash` (existing) | `gemini-2.5-pro` + Function Calling |

---

# VERSION 1 — IMMEDIATE-USEFUL
## "AI Estimation & Sub-Item Intelligence at Point of Entry"

### V1 Goal

When a PM or developer opens the **Create/Edit form** for any Project, Epic, Feature, UserStory, or TaskItem, they get an **AI button** that:
1. Reads the existing sibling/parent context from the database (not the codebase)
2. Instantly estimates **effort** (PERT O/M/P hours), **priority**, **story points**, and **budget impact**
3. Suggests **sub-items** (Features from Epic, UserStories from Feature, Tasks from UserStory) with a **one-click create** workflow
4. Suggests **acceptance criteria** and **Definition of Done**

This is a **pure extension** of the existing `GeminiAnalyticsService` pattern — no new infrastructure required.

---

## V1 Architecture — What Changes

```
Browser (Razor View)
    │
    ▼ JS fetch()
[New] AI API Endpoints (AiEstimationController)
    │
    ▼ async
[Extended] IGeminiAiService  (replaces IGeminiAnalyticsService)
    │  ├─ EstimateProjectAsync(projectId, title, description)
    │  ├─ EstimateEpicAsync(epicId, title, description)
    │  ├─ EstimateFeatureAsync(featureId, title, description)
    │  ├─ EstimateUserStoryAsync(storyId, title, description)
    │  ├─ EstimateTaskAsync(taskId, title, description)
    │  ├─ SuggestSubItemsAsync(parentType, parentId, title, description)
    │  └─ GenerateAcceptanceCriteriaAsync(title, description)
    │
    ▼ reads
ApplicationDbContext — existing sibling/history data for context
```

---

## V1 Proposed Changes

### Component 1: Service Layer

#### [MODIFY] [GeminiAnalyticsService.cs](file:///d:/TGI/Products/OfficeTaskManagement/src/OfficeTaskManagement.Web/Services/GeminiAnalyticsService.cs)
- Rename `CallGeminiApiAsync` → `private` helper (no change needed, already private)
- Extend with a new structured response pattern using `System.Text.Json` deserialization

#### [NEW] `Services/IGeminiAiService.cs`
New unified interface separating **analytics** (existing) from **estimation** (new):

```csharp
public interface IGeminiAiService
{
    Task<EstimationResult> EstimateAsync(EstimationRequest request);
    Task<SubItemSuggestions> SuggestSubItemsAsync(SubItemRequest request);
    Task<string> GenerateAcceptanceCriteriaAsync(string title, string description);
}

public record EstimationResult(
    decimal OptimisticHours,
    decimal MostLikelyHours,
    decimal PessimisticHours,
    decimal PertHours,
    string Priority,           // Low / Medium / High / Critical
    int StoryPoints,           // Fibonacci: 1,2,3,5,8,13,21
    decimal EstimatedBudget,   // BDT, based on team avg rate
    string Rationale,          // AI explanation markdown
    string[] Risks             // Key risks identified
);

public record SubItemSuggestions(
    string ParentType,         // Epic | Feature | UserStory
    SubItemDto[] Items         // suggested children with titles + descriptions
);
```

#### [NEW] `Services/GeminiAiService.cs`
Core implementation. Key design decisions:
- **Context injection**: Before calling Gemini, queries EF Core for sibling items to give the model "what already exists" so estimates are non-duplicative
- **Structured JSON output**: Uses Gemini's `response_mime_type: application/json` + `response_schema` for reliable parsing (no regex fragility)
- **Graceful degradation**: Falls back to a reasonable default if Gemini is unreachable
- **Prompt engineering**: System prompt includes the full hierarchy context (Project → Epic → Feature → UserStory → Task) and all PERT/RACI definitions from your domain

---

### Component 2: API Controller

#### [NEW] `Controllers/Api/AiEstimationController.cs`
A lightweight API controller (thin, per AGENTS.md rules):

```
POST /api/ai/estimate          → EstimationResult
POST /api/ai/suggest-subitems  → SubItemSuggestions
POST /api/ai/acceptance-criteria → string (markdown)
POST /api/ai/bulk-create-subitems → creates suggested items in DB, returns IDs
```

The `bulk-create-subitems` endpoint is the **one-click creation** feature — it calls the existing EF Core DbContext to insert Features/UserStories/Tasks in a single transaction.

---

### Component 3: Front-End UX (Razor + Vanilla JS)

#### [MODIFY] All Create/Edit Views (Projects, Epics, Features, UserStories, TaskItems)

Add a reusable **AI Estimation Panel** — a `_AiEstimationPanel.cshtml` partial view that injects into every create/edit form:

```
┌─────────────────────────────────────────────┐
│  ✨ AI Estimation Assistant                 │
│  ─────────────────────────────────────────  │
│  [Analyze with AI ▶]                        │
│                                             │
│  ○ Effort: O: 4h  M: 8h  P: 16h  PERT: 9h  │
│  ○ Priority: High  ○ Story Points: 8        │
│  ○ Est. Budget: ৳ 12,400                    │
│  ○ Risks: [Data migration complexity]       │
│                                             │
│  [Apply These Estimates ✓]                  │
│                                             │
│  ✨ Suggested Sub-Items (3)                 │
│  ─────────────────────────────────────────  │
│  ☑ Feature: Login UI Design                 │
│  ☑ Feature: OAuth2 Integration              │
│  ☑ Feature: Session Management              │
│                                             │
│  [Create Selected (2) ▶]                    │
└─────────────────────────────────────────────┘
```

**Implementation**: Pure `fetch()` + DOM manipulation. No new npm dependencies. The panel auto-populates form fields (O/M/P hours, priority) on "Apply", and the "Create Selected" button POSTs to `api/ai/bulk-create-subitems` then redirects.

---

### V1 Prompt Engineering Strategy

**The key differentiator** from your existing analytics service is *context injection*. Before every Gemini call, the service builds a **"project knowledge packet"**:

```json
{
  "projectName": "HR Management System",
  "projectDescription": "...",
  "existingEpics": ["Authentication", "Leave Management", "Payroll"],
  "existingFeatures": ["Login", "Logout", "Password Reset"],
  "recentTaskHistory": [
    { "title": "Build Login UI", "actualHours": 12, "estimatedHours": 8 }
  ],
  "teamVelocity": "avg 42 story points/sprint",
  "avgHourlyRate": 800
}
```

This gives Gemini **actual project knowledge**, not generic estimates. A "User Authentication" feature in *your specific project* will be estimated based on how similar features took in *your actual history*.

---

### V1 Verification Plan

- **Automated Tests**: Add `Tests/OfficeTaskManagement.Tests/Services/GeminiAiServiceTests.cs` — mock `HttpClient` to test prompt building, JSON parsing, fallback behavior
- **Manual**: Create a new Epic on a project with 3+ existing Epics → verify AI panel loads, estimates are contextual, one-click creates correct children
- **Build**: `dotnet build` + `dotnet test` must pass

### V1 Effort Estimate

| Sprint | Deliverable | Effort |
|---|---|---|
| Sprint 1 (Wk 1-2) | `IGeminiAiService` + `GeminiAiService` + API controller | 3 dev-days |
| Sprint 2 (Wk 3-4) | AI panel partial view + JS + Projects/Epics/Features forms | 4 dev-days |
| Sprint 3 (Wk 5-6) | UserStory + TaskItem forms + bulk-create + tests | 3 dev-days |
| **Total** | | **~10 dev-days** |

---
---

# VERSION 2 — FINAL-ULTIMATE
## "Agentic PMP Copilot with Codebase Awareness, RAG & Tool Calling"

### V2 Vision

> "I want my PMP tool to be aware of the codebase and estimate properly by analyzing the present state."

V2 turns the system into a **genuine AI agent** — one that can *perceive* your codebase (not just the PM data), *reason* about technical complexity, and *act* across your entire workflow autonomously. This is the Augment Code / GitHub Copilot for project managers.

---

## V2 Architecture — The Four Pillars

```
┌────────────────────────────────────────────────────────────────────┐
│                        AGENT ORCHESTRATION LAYER                   │
│                     (Gemini 2.5 Pro + Function Calling)            │
│                                                                    │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────────┐ │
│  │   PILLAR 1   │  │   PILLAR 2   │  │       PILLAR 3           │ │
│  │  Codebase    │  │  PM Domain   │  │  Execution / Action      │ │
│  │  RAG Index   │  │  Knowledge   │  │  (Tool Calling)          │ │
│  │              │  │              │  │                          │ │
│  │ Vector DB    │  │ EF Core data │  │ CreateTask()             │ │
│  │ (pgvector)   │  │ + history    │  │ CreateSubItems()         │ │
│  │              │  │ + velocity   │  │ AssignResource()         │ │
│  │ Code chunks  │  │ + PERT data  │  │ UpdateEstimate()         │ │
│  │ embeddings   │  │ + burnout    │  │ GeneratePDF()            │ │
│  └──────────────┘  └──────────────┘  └──────────────────────────┘ │
│                                                                    │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │                        PILLAR 4                              │  │
│  │               Multi-Turn Conversation Memory                 │  │
│  │  (Session-scoped context: PM is mid-planning a feature)      │  │
│  └──────────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────┘
```

---

## V2 Components — Detailed Breakdown

### Pillar 1: Codebase RAG (Retrieval-Augmented Generation)

**Purpose**: The agent reads your source code to understand technical complexity.

#### [NEW] `Services/CodebaseIndexingService.cs`
A background service (`IHostedService`) that:
1. **Scans** `src/` for `.cs` files on startup and on Git push webhook
2. **Chunks** code into logical units: class-level, method-level
3. **Embeds** chunks using `models/text-embedding-004` (Gemini Embeddings API)
4. **Stores** embeddings in PostgreSQL with `pgvector` extension (zero new infrastructure — already using PostgreSQL in prod)

```sql
-- New migration
CREATE TABLE code_embeddings (
    id          SERIAL PRIMARY KEY,
    file_path   TEXT NOT NULL,
    chunk_text  TEXT NOT NULL,
    embedding   vector(768),
    updated_at  TIMESTAMPTZ DEFAULT now()
);
CREATE INDEX ON code_embeddings USING ivfflat (embedding vector_cosine_ops);
```

#### [NEW] `Services/CodebaseRetrievalService.cs`
Given a natural language query (e.g., "How complex is adding a new approval workflow?"), returns the top-K most semantically relevant code chunks. These are injected into the Gemini prompt as grounding context.

**Why pgvector over Qdrant/ChromaDB?** You already have PostgreSQL in production. Zero operational overhead. pgvector is production-grade for this scale.

---

### Pillar 2: PM Domain Knowledge Engine

#### [NEW] `Services/PmKnowledgeService.cs`
Aggregates *all* PM context for a given entity into a structured snapshot:

```csharp
public record PmKnowledgeSnapshot(
    ProjectSnapshot Project,
    IReadOnlyList<EpicSnapshot> Epics,
    TeamVelocityProfile TeamVelocity,    // last 6 sprints avg velocity
    EstimationAccuracyProfile Accuracy,  // historical estimate vs actual
    ResourceAvailabilityMap Resources,   // who is available & at what %
    BudgetStatus Budget                  // current burn rate + remaining
);
```

This snapshot is **always injected** as context so Gemini understands the current project state before making any estimation or planning decision.

---

### Pillar 3: Tool Calling (The Action Layer)

**Purpose**: Gemini doesn't just *suggest* — it *acts*.

Using the **Gemini Function Calling API**, define a set of PM tools the agent can invoke:

```json
{
  "tools": [
    {
      "name": "create_user_story",
      "description": "Creates a new UserStory under a Feature",
      "parameters": { "feature_id": "int", "title": "string", "description": "string", "priority": "string" }
    },
    {
      "name": "create_task",
      "description": "Creates a TaskItem with PERT estimates",
      "parameters": { "user_story_id": "int", "title": "string", "o_hours": "decimal", "m_hours": "decimal", "p_hours": "decimal" }
    },
    {
      "name": "assign_workflow_template",
      "description": "Applies a RACI workflow template to a task",
      "parameters": { "task_id": "int", "template_id": "int" }
    },
    {
      "name": "query_resource_availability",
      "description": "Checks who is available for a given date range",
      "parameters": { "start_date": "string", "end_date": "string", "required_skills": "array" }
    },
    {
      "name": "update_budget_estimate",
      "description": "Updates a project's estimated budget based on effort data",
      "parameters": { "project_id": "int", "estimated_hours": "decimal", "hourly_rate": "decimal" }
    }
  ]
}
```

The .NET side implements a **Tool Dispatch Router** — a service that maps Gemini's function call responses to actual EF Core / service operations:

```csharp
public class AgentToolDispatcher
{
    // Gemini returns: { "name": "create_user_story", "args": {...} }
    // This router calls the correct service and returns the result back to Gemini
    public async Task<string> DispatchAsync(FunctionCall call) { ... }
}
```

---

### Pillar 4: Multi-Turn Conversation Memory

#### [NEW] `Services/AgentConversationService.cs`
Maintains conversation state per user/session so the PM can have a dialogue:

```
PM: "I need to plan the authentication epic for our HR project"
AI: "I found 3 existing auth-related features. Based on your team's velocity (42 pts/sprint) 
     and the codebase complexity I analyzed (the Identity service has 847 lines), 
     I estimate 3 features and 12 user stories. Shall I break these down?"
PM: "Yes, but skip OAuth — we're doing LDAP only"
AI: "Understood. Creating 11 user stories (OAuth excluded)... Done. 
     Total PERT estimate: 184 hours. Recommended sprint allocation: 3 sprints."
```

Implemented as:
- **Session storage** for short-term context (Redis or in-memory)
- **Conversation history** injected into each Gemini API call as `previous_turns`
- **Entity resolution**: AI references "the HR project" → resolved to `projectId=7`

---

### Pillar 5: Agent UI — The PM Copilot Panel

A dedicated **AI Copilot sidebar** available on every major page:

```
┌────────────────────────────────────────────────────┐
│  🤖 PM Copilot                            [━ ╳]    │
│  ─────────────────────────────────────────────      │
│  Context: Authentication Epic · HR System           │
│                                                     │
│  ┌─────────────────────────────────────────────┐   │
│  │ What would you like to plan?                │   │
│  │ > Plan the full feature breakdown for       │   │
│  │   this epic using our existing patterns...  │   │
│  └─────────────────────────────────────────────┘   │
│                                             [Send]  │
│                                                     │
│  AI: Based on your codebase, I found that your     │
│  current auth module (IdentityService, 3 files)     │
│  handles basic JWT. For LDAP, I estimate:           │
│                                                     │
│  📦 3 Features (tap to expand)                      │
│  ├─ LDAP Provider Integration — 40h PERT            │
│  ├─ Single Sign-On UI — 24h PERT                    │
│  └─ Group-based Authorization — 32h PERT            │
│                                                     │
│  💡 Risks: LdapConnection library compatibility     │
│     with .NET 10 needs validation (1 day spike)     │
│                                                     │
│  [✓ Create All Items]  [✎ Modify Plan]  [⬇ Export] │
└────────────────────────────────────────────────────┘
```

---

### V2 Infrastructure Requirements

| Component | Technology | Notes |
|---|---|---|
| Vector Storage | **pgvector** (PostgreSQL extension) | Already using PostgreSQL in prod |
| Embeddings | **Gemini `text-embedding-004`** | Same API key |
| LLM | **Gemini 2.5 Pro** | Upgrade from Flash for complex reasoning |
| Session/Cache | **Redis** (optional) | In-memory OK for v2.0 |
| Code Indexer | **Background `IHostedService`** | Runs on startup + Git webhook |
| Git Webhook | **GitHub Actions webhook → `/api/agent/reindex`** | Triggers re-indexing on push |

> [!IMPORTANT]
> pgvector must be installed as a PostgreSQL extension: `CREATE EXTENSION vector;`  
> Requires PostgreSQL 12+ (already in use). No new servers needed.

---

### V2 New Files Summary

```
src/OfficeTaskManagement.Web/
├── Services/
│   ├── Agent/
│   │   ├── IAgentService.cs              [NEW] Multi-turn agent interface
│   │   ├── AgentService.cs               [NEW] Orchestration: RAG + Tool Calling
│   │   ├── AgentConversationService.cs   [NEW] Session memory management
│   │   ├── AgentToolDispatcher.cs        [NEW] Function call → .NET action router
│   │   ├── AgentToolDefinitions.cs       [NEW] Gemini tool schema definitions
│   │   └── PmKnowledgeService.cs         [NEW] PM domain snapshot builder
│   ├── Codebase/
│   │   ├── CodebaseIndexingService.cs    [NEW] IHostedService, scans + embeds code
│   │   └── CodebaseRetrievalService.cs   [NEW] Semantic code search
│   └── GeminiAiService.cs               [MODIFY] Add embedding + function calling
├── Controllers/
│   └── Api/
│       ├── AiEstimationController.cs     [NEW - V1]
│       └── AgentController.cs            [NEW - V2] Multi-turn agent endpoint
├── Models/
│   └── Agent/
│       ├── AgentConversation.cs          [NEW] DB entity for conversation history
│       ├── CodeEmbedding.cs              [NEW] DB entity for code vectors
│       └── AgentRequest/Response DTOs    [NEW]
├── Migrations/
│   └── [timestamp]_AddAgentTables.cs     [NEW] pgvector + conversation tables
└── Views/
    └── Shared/
        ├── _AiEstimationPanel.cshtml     [NEW - V1] Inline estimation panel
        └── _AiCopilotSidebar.cshtml      [NEW - V2] Full copilot sidebar
```

---

### V2 Effort & Phasing

| Phase | Duration | Deliverable |
|---|---|---|
| **Phase 1** (V1 complete) | 6 weeks | Inline AI estimation panels, sub-item generation, one-click create |
| **Phase 2** | 4 weeks | pgvector migration, CodebaseIndexingService, embeddings |
| **Phase 3** | 4 weeks | Function Calling integration, AgentToolDispatcher |
| **Phase 4** | 4 weeks | Multi-turn conversation, AgentCopilot sidebar UI |
| **Phase 5** | 4 weeks | Git webhook integration, real-time re-indexing, polish |
| **Total** | ~5 months | Full V2 agentic copilot |

---

## Open Questions for Your Review

> [!IMPORTANT]
> **Q1 — Gemini API Key**: The `appsettings.json` shows the key is commented out. Is the key stored in User Secrets (`dotnet user-secrets`)? We need to confirm this is live before V1 can be tested.

> [!IMPORTANT]  
> **Q2 — Codebase Scope for RAG**: Should the code indexer scan **only** `src/OfficeTaskManagement.Web/` or also include any other repositories? This affects estimation quality significantly.

> [!NOTE]
> **Q3 — Currency / Hourly Rate**: The estimation panel will show BDT budget estimates. Where is the team's average hourly rate stored? In `ResourceProfile`? We should use actual salary data from `SalaryHistory` for real accuracy.

> [!NOTE]
> **Q4 — V1 Priority Order**: Which entity form should get AI estimation first for maximum immediate value?
> - Option A: **TaskItem** (most granular, most used)
> - Option B: **UserStory** (drives decomposition)
> - Option C: **Epic** (highest strategic impact)

> [!NOTE]
> **Q5 — Sub-Item Generation Depth**: When AI suggests sub-items for an Epic, should it go one level deep (just Features) or cascade (Features + UserStories + Tasks) in a single call? Cascading is more powerful but slower (~10-15 seconds for Gemini).

---

## Security Considerations

- Gemini API key via `dotnet user-secrets` (dev) / environment variable (prod) — **never** in `appsettings.json`
- All AI API endpoints protected by `[Authorize]` + existing `HasPermissionAttribute`
- Prompt injection protection: user input sanitized before inclusion in prompts
- Code embeddings are tenant-scoped (no cross-tenant data leakage via RAG)

---

## Summary: Why This Approach

1. **V1 is deployable in weeks** — it builds on your existing `GeminiAnalyticsService` pattern exactly. No new infrastructure. Same API key. Same calling conventions.
2. **V1 pays off immediately** — every new Feature/Story/Task created gets AI-powered PERT estimates, reducing the #1 PM pain point: blank-slate estimation.
3. **V2 is a genuine competitive differentiator** — pgvector + Gemini tool calling is the same stack used by production AI coding assistants. It makes your PM tool as smart as your IDE.
4. **The architecture respects your existing code standards** — thin controllers, DI-registered services, async/await throughout, InMemory-testable service layer.
