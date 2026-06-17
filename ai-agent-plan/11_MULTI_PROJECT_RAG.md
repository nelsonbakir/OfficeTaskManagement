# Execution Task List — Multi-Project Codebase RAG
**OfficeTaskManagement · Resumable Enhancement Plan · Minimal Change Architecture**

> **HOW TO USE**: Follow the step-by-step tasks to transform the single-repo global RAG indexer into an optimized, project-isolated dynamic indexing engine.
> 
> Status: `[ ]` = TODO · `[/]` = IN PROGRESS · `[x]` = DONE

---

## 📅 PHASE 1 — Database & Model Schema Updates
*Goal: Link codebase chunks directly to their corresponding projects in the database.*

### T01 — Modify Project Entity
- [ ] Open `src/OfficeTaskManagement.Web/Models/Project.cs`
- [ ] Add properties for codebase path/URL:
  ```csharp
  public string? RepositoryPath { get; set; } // Local path for development/UNC
  public string? RepositoryUrl { get; set; }  // Git clone link for remote repositories
  ```
- [ ] Run `dotnet build`

### T02 — Modify CodeEmbedding Entity
- [ ] Open `src/OfficeTaskManagement.Web/Models/Ai/CodeEmbedding.cs`
- [ ] Add `ProjectId` foreign key reference to associate chunks with a specific project:
  ```csharp
  public int ProjectId { get; set; }
  public Project Project { get; set; } = null!;
  ```
- [ ] Run `dotnet build`

### T03 — Configure EF Core Cascade Relationship
- [ ] Open `src/OfficeTaskManagement.Web/Data/ApplicationDbContext.cs`
- [ ] In `OnModelCreating`, configure the relationship:
  ```csharp
  builder.Entity<CodeEmbedding>()
      .HasOne(e => e.Project)
      .WithMany()
      .HasForeignKey(e => e.ProjectId)
      .OnDelete(DeleteBehavior.Cascade);
  ```
- [ ] Run `dotnet build`

### T04 — Create Schema Migration
- [ ] Run: `dotnet ef migrations add AddMultiProjectRagFields --project src/OfficeTaskManagement.Web`
- [ ] Verify the migration file contains the correct foreign key constraint.
- [ ] Apply migration: `dotnet ef database update --project src/OfficeTaskManagement.Web`
- [ ] Run `dotnet build` → must succeed

---

## ⚙️ PHASE 2 — Optimized On-Demand Indexing Service
*Goal: Refactor the indexer to run dynamically per-project with user-triggered and lazy sync modes.*

### T05 — Disable Automatic Startup Scanning
- [ ] Open `src/OfficeTaskManagement.Web/Services/Codebase/CodebaseIndexingService.cs`
- [ ] Remove automatic execution from `StartAsync` (hosted service loop) so that it no longer scans the root repository globally on startup.

### T06 — Implement Project-Specific Delta Indexing
- [ ] In `CodebaseIndexingService.cs`, implement:
  `public async Task IndexProjectAsync(int projectId, CancellationToken ct)`
- [ ] Fetch the project's `RepositoryPath` (fallback to `.` if empty).
- [ ] Retrieve existing file hashes for the `projectId` from `db.CodeEmbeddings`.
- [ ] Execute AST/line-window chunkers only on files that are new or have mismatched MD5 hashes.
- [ ] Embed the updated chunks using `GeminiEmbeddingService` (with `outputDimensionality = 768`).
- [ ] Write only the modified/new chunks to the database, ensuring `ProjectId` is set.

### T07 — Implement Index Reset
- [ ] In `CodebaseIndexingService.cs`, implement:
  `public async Task PurgeProjectIndexAsync(int projectId)`
- [ ] Delete all rows in the `CodeEmbeddings` table where `ProjectId == projectId` to allow a fresh clean sync on demand.
- [ ] Run `dotnet build`

---

## 🔍 PHASE 3 — Project-Scoped Retrieval Service
*Goal: Restrict the semantic search context to the active project.*

### T08 — Scope Retrieval Query by ProjectId
- [ ] Open `src/OfficeTaskManagement.Web/Services/Codebase/CodebaseRetrievalService.cs`
- [ ] Update `GetChunksViaVectorSearchAsync` to accept `int projectId` as a parameter.
- [ ] Modify the EF query to enforce project-scoping:
  ```csharp
  var chunks = await _db.CodeEmbeddings
      .Where(e => e.ProjectId == projectId && e.Embedding != null && e.Embedding.Length > 0)
      .OrderBy(e => e.Embedding.CosineDistance(qVec.ToArray()))
      .Take(topK)
      .Select(e => $"[{e.FilePath}:{e.StartLine}]\n{e.ChunkText}")
      .ToListAsync(ct);
  ```
- [ ] Update usages in Copilot chat prompts to extract and pass the context `projectId`.

---

## 🌐 PHASE 4 — API & Web Control Layer
*Goal: Expose endpoints to monitor and manually trigger codebase indexing.*

### T09 — Expose Indexing Actions in API
- [ ] Open `src/OfficeTaskManagement.Web/Controllers/Api/AgentController.cs`
- [ ] Add `POST /api/agent/index-project/{projectId}` to trigger `IndexProjectAsync`.
- [ ] Add `DELETE /api/agent/index-project/{projectId}` to trigger `PurgeProjectIndexAsync`.
- [ ] Run `dotnet build`

### T10 — Add Status & Cost Analytics Endpoint
- [ ] In `AgentController.cs`, add `GET /api/agent/index-status/{projectId}`:
  * Returns number of indexed chunks for the project.
  * Checks if local code directory has newer file modified timestamps compared to the last index update (toggling a `NeedsSync` flag).
  * Returns total estimated cost/tokens used for that project.

---

## 🎨 PHASE 5 — Fluent UI Settings & Control Dashboard
*Goal: Give users a clean interface in Project Details to link repositories and trigger updates.*

### T11 — Add Repository Fields to Project Forms
- [ ] Edit `src/OfficeTaskManagement.Web/Views/Projects/Create.cshtml` and `Edit.cshtml`.
- [ ] Add input fields for `RepositoryPath` (local path / network share) and `RepositoryUrl`.
- [ ] Use Fluent design styles matching the existing theme borders and input padding.

### T12 — Integrate Control Panel in Project Details
- [ ] Edit `src/OfficeTaskManagement.Web/Views/Projects/Details.cshtml`.
- [ ] Add a "Codebase Settings & Sync" card.
- [ ] Display indexing status (e.g., "Up-to-date", "Needs Sync" in warning amber, or "Not Indexed").
- [ ] Embed the primary blue `#0078D4` button to trigger the manual sync action with loading spinners.

---

## 🧪 PHASE 6 — Verification & Testing
*Goal: Assure system stability and isolation.*

### T13 — Write Retrieval Isolation Unit Tests
- [ ] Open `Tests/OfficeTaskManagement.Tests/Services/CodebaseRetrievalServiceTests.cs`.
- [ ] Seed dummy embeddings for two projects (e.g., `ProjectId = 1` and `ProjectId = 2`).
- [ ] Query retrieval for `ProjectId = 1` and assert that no chunks from `ProjectId = 2` are returned.

### T14 — Run Verification Suites
- [ ] Run `dotnet build` to ensure clean compile.
- [ ] Run `dotnet test` to confirm all 85 tests (and new isolation tests) pass successfully.
