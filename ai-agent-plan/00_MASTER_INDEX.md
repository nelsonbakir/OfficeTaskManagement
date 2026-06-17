# AI Agent Integration — Master Plan Index
**Project**: OfficeTaskManagement PMP Tool  
**Goal**: Gemini-powered agentic copilot — codebase-aware, context-driven, estimation & sub-item creation at every entry point  
**Version**: Ultimate (single unified roadmap)  
**Status Tracking**: Each document uses `[TODO]` / `[IN_PROGRESS]` / `[DONE]` markers  

---

## How to Resume This Plan (For Any AI Agent)

1. Read `00_MASTER_INDEX.md` (this file) to understand the overall state
2. Check `10_EXECUTION_TASKS.md` to find the first `[TODO]` or `[IN_PROGRESS]` task
3. Read the linked specification document for that task group
4. Implement the task, then mark it `[DONE]` in `10_EXECUTION_TASKS.md`
5. Update this index if a phase completes

**Key constraint**: Always run `dotnet build` and `dotnet test` after each task. Never leave the codebase in a broken state.

---

## Plan Documents

| File | Contents | Status |
|------|----------|--------|
| [01_ARCHITECTURE.md](./01_ARCHITECTURE.md) | Full system architecture, data flow, component map | Reference |
| [02_USER_FLOWS.md](./02_USER_FLOWS.md) | User-centric journeys for every entity (Create/Edit/Re-estimate) | Reference |
| [03_PROMPT_STRATEGY.md](./03_PROMPT_STRATEGY.md) | Token-efficient prompt engineering — templates, context budget, caching | Reference |
| [04_CODEBASE_RAG.md](./04_CODEBASE_RAG.md) | Git repo indexing, multi-language chunking, pgvector, retrieval | Reference |
| [05_SERVICE_LAYER.md](./05_SERVICE_LAYER.md) | All new C# services with full specs and method signatures | Reference |
| [06_API_LAYER.md](./06_API_LAYER.md) | All new API endpoints, DTOs, request/response contracts | Reference |
| [07_FRONTEND_UX.md](./07_FRONTEND_UX.md) | Razor partials, JS patterns, UI components per entity | Reference |
| [08_DATA_MODEL.md](./08_DATA_MODEL.md) | DB migrations, new EF Core entities, pgvector setup | Reference |
| [09_TESTING.md](./09_TESTING.md) | Unit tests, integration tests, mocking strategy | Reference |
| [10_EXECUTION_TASKS.md](./10_EXECUTION_TASKS.md) | **THE TASK LIST** — resumable, ordered, checkboxed | Reference |
| [11_MULTI_PROJECT_RAG.md](./11_MULTI_PROJECT_RAG.md) | **THE MULTI-PROJECT ENHANCEMENT TASK LIST** — resumable, checkboxed | DONE |
| [12_CODEBASE_FIRST_ONBOARDING.md](./12_CODEBASE_FIRST_ONBOARDING.md) | **THE CODEBASE-FIRST ONBOARDING WIZARD TASK LIST** — resumable, checkboxed | **ACTIVE** |


---

## Phase Summary

| Phase | Name | Tasks | Status |
|-------|------|-------|--------|
| Phase 1 | Foundation & Inline Estimation | T01–T15 | `[TODO]` |
| Phase 2 | Sub-Item Intelligence & One-Click Create | T16–T28 | `[TODO]` |
| Phase 3 | Git Repo RAG — Codebase Awareness | T29–T42 | `[TODO]` |
| Phase 4 | Agentic Tool Calling & Multi-Turn Copilot | T43–T58 | `[TODO]` |
| Phase 5 | Re-estimation, Bulk Operations & Analytics | T59–T68 | `[TODO]` |

---

## Project Context (For Agent Reference)

```
Repository: d:\TGI\Products\OfficeTaskManagement
Domain hierarchy: Project → Epic → Feature → UserStory → TaskItem
PERT model: (O + 4M + P) / 6 — already implemented in WorkflowEngineService
Existing Gemini: GeminiAnalyticsService — 4 methods, gemini-2.5-flash, HttpClient pattern
Gemini API Key: Already in User Secrets (confirmed)
Currency: BDT only
Weekend: Friday + Saturday (Bangladesh)
```
