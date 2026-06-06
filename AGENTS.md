# AGENTS.md

Office task management system — ASP.NET Core MVC web app with workflow engine, capacity planning, and RACI-based resource management.

## Setup commands

- Install deps: `dotnet restore`
- Build: `dotnet build`
- Test: `dotnet test`
- Run: `dotnet run --project src/OfficeTaskManagement.Web/OfficeTaskManagement.csproj`

## Project layout

- `src/OfficeTaskManagement.Web/` — ASP.NET Core MVC/Razor web application
  - `Controllers/` — MVC and API controllers
  - `Services/` — Business logic (ResourceService, CapacityPlanningService, WorkflowEngine)
  - `Services/WorkflowEngine/` — Workflow engine (StageGateService, WorkflowEngineService, KanbanGovernanceService, LagSchedulingService)
  - `Models/` — Domain entities, enums, view models
  - `Data/` — EF Core DbContext, seed data
  - `Migrations/` — EF Core migrations (PostgreSQL)
  - `Views/`, `wwwroot/` — Razor views and static assets
- `Tests/OfficeTaskManagement.Tests/` — xUnit test suite
  - `Services/` — Service unit tests (ResourceService, StageGateService, WorkflowEngineService, CapacityPlanningService)
  - `Controllers/` — Controller unit tests (ResourceController)

## Tech stack

- .NET 10, ASP.NET Core 10 MVC
- Entity Framework Core 10 (SQLite for dev, PostgreSQL for prod)
- Authentication: ASP.NET Identity + JWT Bearer
- Media: AWS S3 (production) / local file (development)
- Analytics: Gemini AI service integration
- PDF generation: QuestPDF
- Spreadsheet export: ClosedXML

## Code style

- C# 12+ with nullable reference types enabled
- Use dependency injection for all services
- Async/await for all I/O operations
- Keep controllers thin; business logic in service layer
- Tests use EF Core InMemory provider (no real DB needed for unit tests)

## Testing instructions

- Unit tests: `dotnet test` (xUnit + Moq + InMemory EF Core)
- Run specific test class: `dotnet test --filter "FullyQualifiedName~ResourceServiceTests"`
- Add tests for every new behavior — match the pattern in `Tests/OfficeTaskManagement.Tests/Services/`

## PR & commit conventions

- Branch from `main`; never push directly to `main`
- Commit message: conventional commits (`feat:`, `fix:`, `docs:`, `refactor:`)
- All tests must pass before opening a PR

## Security

- Never commit secrets — appsettings are in `.gitignore`
- JWT secrets stored in UserSecrets (dev) or environment variables (prod)
- RBAC via `HasPermissionAttribute` — check existing permission groups before adding new ones