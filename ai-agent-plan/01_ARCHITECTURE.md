# Architecture — AI Agent Integration
**OfficeTaskManagement · Gemini Agentic Copilot**

---

## System Overview

The AI integration follows a **layered agentic architecture** where Gemini acts as the reasoning core, backed by two knowledge sources: the PM domain (EF Core database) and the codebase (Git repo semantic index). The user interacts at their natural entry points — forms, detail pages — and the AI is *always present*, not hidden in a separate analytics page.

```
┌──────────────────────────────────────────────────────────────────────────┐
│                          USER INTERFACE LAYER                            │
│                                                                          │
│  Create/Edit forms (Epics, Features, UserStories, Tasks, Projects)       │
│       │                                                                  │
│       │  JS fetch() — non-blocking, progressive enhancement              │
│       ▼                                                                  │
│  ┌────────────────────────┐   ┌─────────────────────────────────────┐   │
│  │  AI Estimation Panel   │   │  AI Copilot Sidebar (multi-turn)    │   │
│  │  (inline, per form)    │   │  (persistent, context-aware)        │   │
│  └────────────┬───────────┘   └──────────────┬──────────────────────┘   │
└───────────────┼──────────────────────────────┼──────────────────────────┘
                │                              │
                ▼                              ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                          API LAYER                                       │
│                                                                          │
│  POST /api/ai/estimate          → EstimationResult                      │
│  POST /api/ai/suggest-children  → ChildItemSuggestions                  │
│  POST /api/ai/bulk-create       → BulkCreateResult                      │
│  POST /api/ai/reestimate        → EstimationResult                      │
│  POST /api/agent/chat           → AgentChatResponse (streaming)         │
│  POST /api/agent/reindex        → (webhook: git push trigger)           │
└──────────────────────────┬───────────────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                          SERVICE LAYER                                   │
│                                                                          │
│  ┌─────────────────────┐   ┌───────────────────────────────────────┐    │
│  │  GeminiAiService    │   │  AgentService (Phase 4)               │    │
│  │  (core LLM calls)   │   │  Multi-turn orchestration             │    │
│  │                     │   │  + Function Calling dispatch          │    │
│  │  · EstimateAsync    │   │                                       │    │
│  │  · SuggestChildren  │   │  AgentToolDispatcher                  │    │
│  │  · BulkCreate       │   │  Maps Gemini fn calls → .NET actions  │    │
│  │  · ReEstimate       │   └───────────────────────────────────────┘    │
│  └──────────┬──────────┘                                                │
│             │                                                            │
│             ▼                                                            │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │  ContextBuilderService — THE TOKEN BUDGET ENGINE                 │   │
│  │                                                                  │   │
│  │  Assembles a "context packet" for Gemini calls:                  │   │
│  │  · PM snapshot (from EF Core — compressed stats, not raw rows)   │   │
│  │  · Code chunks (from CodebaseRetrievalService — top-K only)      │   │
│  │  · Budget enforcer: caps total prompt tokens at 8,000            │   │
│  └────────────────────┬─────────────────────────────────────────────┘   │
│                       │                                                  │
│            ┌──────────┴──────────┐                                      │
│            ▼                     ▼                                       │
│  ┌──────────────────┐  ┌─────────────────────────────────────────────┐  │
│  │  PmKnowledge     │  │  CodebaseRetrievalService                   │  │
│  │  Service         │  │                                             │  │
│  │  · Project stats │  │  · Semantic search over pgvector            │  │
│  │  · Team velocity │  │  · Top-K chunk retrieval                    │  │
│  │  · PERT history  │  │  · Language-aware chunking                  │  │
│  │  · Salary/BDT    │  └────────────────────┬────────────────────────┘  │
│  └──────────────────┘                       │                           │
│                                             ▼                           │
│                               ┌─────────────────────────────────────┐   │
│                               │  CodebaseIndexingService            │   │
│                               │  (IHostedService + Git webhook)     │   │
│                               │                                     │   │
│                               │  Scans entire Git repo              │   │
│                               │  Detects language by extension      │   │
│                               │  Chunks by semantic unit            │   │
│                               │  Embeds via Gemini text-embedding   │   │
│                               │  Stores in pgvector (PostgreSQL)    │   │
│                               └─────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                       DATA LAYER                                         │
│                                                                          │
│  Existing: ApplicationDbContext (Projects, Epics, Features,              │
│            UserStories, TaskItems, TaskHistory, SalaryHistory...)        │
│                                                                          │
│  New tables:                                                             │
│  · code_embeddings   (file_path, chunk_text, embedding vector(768))      │
│  · agent_conversations (user_id, entity_type, entity_id, turns JSON)    │
│  · ai_estimation_log  (entity_type, entity_id, result JSON, tokens used) │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## Gemini Model Selection Strategy

| Use Case | Model | Reason |
|----------|-------|--------|
| Inline estimation (fast) | `gemini-2.5-flash` | Low latency, existing pattern |
| Sub-item generation (step/full) | `gemini-2.5-flash` | Structured JSON output, cheap |
| Multi-turn Copilot chat | `gemini-2.5-pro` | Complex reasoning, function calling |
| Code embeddings | `text-embedding-004` | Best for code semantic search |

## Gemini API Call Pattern

All calls use the existing `CallGeminiApiAsync` private method pattern in `GeminiAnalyticsService`. The new `GeminiAiService` will:
1. Use `response_mime_type: "application/json"` + `response_schema` for structured output (no regex parsing)
2. Apply server-side retry with exponential backoff (429 handling)
3. Log token usage to `ai_estimation_log` for cost monitoring

## File Map — New Files to Create

```
src/OfficeTaskManagement.Web/
├── Services/
│   ├── Ai/
│   │   ├── IGeminiAiService.cs
│   │   ├── GeminiAiService.cs
│   │   ├── ContextBuilderService.cs
│   │   ├── PmKnowledgeService.cs
│   │   └── AiEstimationLogService.cs
│   ├── Agent/
│   │   ├── IAgentService.cs
│   │   ├── AgentService.cs
│   │   ├── AgentConversationService.cs
│   │   └── AgentToolDispatcher.cs
│   └── Codebase/
│       ├── CodebaseIndexingService.cs
│       └── CodebaseRetrievalService.cs
├── Controllers/Api/
│   ├── AiEstimationController.cs
│   └── AgentController.cs
├── Models/
│   └── Ai/
│       ├── EstimationResult.cs
│       ├── ChildItemSuggestions.cs
│       ├── AgentConversation.cs
│       ├── CodeEmbedding.cs
│       └── AiEstimationLog.cs
├── Migrations/
│   └── [timestamp]_AddAiAgentTables.cs
└── Views/Shared/
    ├── _AiEstimationPanel.cshtml
    └── _AiCopilotSidebar.cshtml
```
