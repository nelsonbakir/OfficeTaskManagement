# Team Memory

Shared knowledge across all reins. Add entries at the bottom with date.

---

### 2026-06-05 Bootstrap

- .NET 10 / ASP.NET Core 10 MVC project
- EF Core 10 with PostgreSQL (prod) / SQLite (dev)
- Main domain services: ResourceService, CapacityPlanningService, WorkflowEngineService, StageGateService
- Test stack: xUnit + Moq + EF Core InMemory
- Migrations use Npgsql (PostgreSQL) with legacy timestamp behavior enabled