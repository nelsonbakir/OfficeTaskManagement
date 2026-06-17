# Execution Task List — Codebase Indexing First Onboarding
**OfficeTaskManagement · Resumable Onboarding Wizard Plan**

> **HOW TO USE**: Follow the step-by-step tasks to implement the repository cloning, indexing, RAG analysis, and multi-step interactive onboarding wizard.
> 
> Status: `[ ]` = TODO · `[/]` = IN PROGRESS · `[x]` = DONE

---

## 📅 PHASE 1 — DTOs & Models Schema Setup
*Goal: Setup data contracts for step-by-step discovery and hierarchical onboarding.*

### T01 — Create Project Onboarding Models
- [ ] Create `src/OfficeTaskManagement.Web/Models/Ai/ProjectOnboardingModels.cs`
- [ ] Define `ProjectAnalysisResult`, `EpicSuggestionDto`, `FeatureSuggestionDto`, `UserStorySuggestionDto`, `TaskAndTestCaseSuggestionsDto`, `TaskSuggestionDto`, and `TestCaseSuggestionDto`.
- [ ] Define `OnboardProjectRequest` and nested hierarchy classes (`OnboardEpicDto`, `OnboardFeatureDto`, `OnboardUserStoryDto`, `OnboardTaskDto`, `OnboardTestCaseDto`).
- [ ] Run `dotnet build`

---

## ⚙️ PHASE 2 — Git Cloning & Codebase Scanners
*Goal: Implement Git URL cloning and codebase file scanners.*

### T02 — Create Git Clone Service
- [ ] Create `src/OfficeTaskManagement.Web/Services/Codebase/GitCloneService.cs`
- [ ] Implement `CloneRepositoryAsync` using process start for `git clone --depth 1`.
- [ ] Target folder should be `wwwroot/uploads/cloned_repos/project-{projectId}`.
- [ ] Add service to DI builder in `Program.cs`.

### T03 — Create Directory Scanner in Gemini AI Service
- [ ] Open `src/OfficeTaskManagement.Web/Services/Ai/GeminiAiService.cs`
- [ ] Implement `ScanCodebaseStructureAsync` private helper.
- [ ] Scan directory tree max 3 levels deep, ignoring bin/obj/node_modules/.git/.vs/Migrations.
- [ ] Read up to 2000 characters from README.md if present.
- [ ] Search for test files recursively to determine `TestsAbsentOrIncomplete`.

---

## 🔍 PHASE 3 — Step-by-Step AI Analysis API
*Goal: Implement discovery APIs using structured Gemini outputs.*

### T04 — Extend IGeminiAiService
- [ ] Open `src/OfficeTaskManagement.Web/Services/Ai/IGeminiAiService.cs`
- [ ] Add the 4 signatures: `AnalyzeProjectCodebaseAsync`, `SuggestFeaturesForEpicAsync`, `SuggestUserStoriesForFeatureAsync`, `SuggestTasksAndTestCasesAsync`.

### T05 — Implement AnalyzeProjectCodebaseAsync
- [ ] Open `src/OfficeTaskManagement.Web/Services/Ai/GeminiAiService.cs`
- [ ] Implement `AnalyzeProjectCodebaseAsync`:
  - [ ] Check if project has a Git URL and clone it first if path is empty.
  - [ ] Scan directory structure.
  - [ ] Call Gemini with response schema for project description, tech stack, test coverage, and suggested Epics.

### T06 — Implement Epic Features & User Stories Suggestions
- [ ] In `GeminiAiService.cs`, implement `SuggestFeaturesForEpicAsync`:
  - [ ] Use `CodebaseRetrievalService` to fetch relevant chunks based on Epic name.
  - [ ] Ask Gemini to suggest features grounded in these code chunks.
- [ ] Implement `SuggestUserStoriesForFeatureAsync`:
  - [ ] Retrieve code chunks for feature name.
  - [ ] Ask Gemini to suggest stories with Given/When/Then acceptance criteria.

### T07 — Implement Tasks & Test Cases Suggestions
- [ ] In `GeminiAiService.cs`, implement `SuggestTasksAndTestCasesAsync`:
  - [ ] Retrieve code chunks.
  - [ ] Ask Gemini to suggest tasks (with O/M/P hours) and test cases (steps + expected results).
  - [ ] If `suggestTests` is true, direct LLM to suggest comprehensive QA verification tasks and test cases.

---

## 🌐 PHASE 4 — Controller & Transaction Save API
*Goal: Expose endpoints to drive the wizard and persist the tree transactionally.*

### T08 — Expose Onboarding Wizard in ProjectsController
- [ ] Open `src/OfficeTaskManagement.Web/Controllers/ProjectsController.cs`
- [ ] Add `GET /Projects/OnboardWizard/{id}` which retrieves the project and returns a wizard view.

### T09 — Create ProjectInitiationApiController
- [ ] Create `src/OfficeTaskManagement.Web/Controllers/Api/ProjectInitiationApiController.cs`
- [ ] Implement endpoints `/api/onboard/clone/{projectId}`, `/api/onboard/analyze-project/{projectId}`, `/api/onboard/suggest-features`, `/api/onboard/suggest-stories`, `/api/onboard/suggest-tasks-and-tests`.
- [ ] Implement `/api/onboard/submit-onboarding` endpoint:
  - [ ] Receive `OnboardProjectRequest`.
  - [ ] Start database transaction.
  - [ ] Recursively insert Epics -> Features -> Stories -> Tasks & Test Cases.
  - [ ] Mark Project strategic status as `Active` (or update metadata).
  - [ ] Commit transaction.

---

## 🎨 PHASE 5 — Premium Wizard View & CSS Styles
*Goal: Build a responsive, gorgeous, and dynamic step-by-step onboarding UI.*

### T10 — Create CSS Stylesheet
- [ ] Create `src/OfficeTaskManagement.Web/wwwroot/css/onboard-wizard.css`
- [ ] Define glassmorphic cards, custom tabs, progress bars, loading spinners, and accordion panels.
- [ ] Add slide/fade animations for transitions.

### T11 — Create OnboardWizard Razor View
- [ ] Create `src/OfficeTaskManagement.Web/Views/Projects/OnboardWizard.cshtml`
- [ ] Design the tab structure (Clone & Index, Epics, Features, Stories, Tasks & Test Cases, Review).
- [ ] Embed progress indicator, step panels, edit forms, and the tree viewer.

### T12 — Create OnboardWizard JS Controller
- [ ] Create `src/OfficeTaskManagement.Web/wwwroot/js/onboard-wizard.js`
- [ ] Handle cloning/indexing API polling.
- [ ] Store wizard hierarchical JSON state in memory.
- [ ] Call AI endpoints when moving to the next tab.
- [ ] Handle client-side add/edit/delete of nodes in the hierarchy.
- [ ] Dispatch final submit payload.

### T13 — Link Details Page
- [ ] Open `src/OfficeTaskManagement.Web/Views/Projects/Details.cshtml`
- [ ] Add a prominent "✨ Codebase Onboarding Wizard" button if the project has repository metadata.

---

## 🧪 PHASE 6 — Verification & Testing
*Goal: Ensure the onboarding flow runs stably and passes all checks.*

### T14 — Write Service Tests
- [ ] Create `Tests/OfficeTaskManagement.Tests/Services/ProjectInitiationTests.cs`
- [ ] Test Git cloning behavior with mock process execution.
- [ ] Test transaction save commits all levels of the tree.
- [ ] Mock Gemini outputs to verify discovery parsing.

### T15 — Run Verification
- [ ] Run `dotnet build`
- [ ] Run `dotnet test`
- [ ] Manually verify end-to-end repository onboarding using a mock workspace folder.
