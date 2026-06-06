# Code Standards

## C# conventions

- Nullable reference types: always enabled (`<Nullable>enable</Nullable>` in csproj)
- `async`/`await` for all I/O — never `.Result` or `.Wait()`
- Use `CancellationToken` for cancellable operations
- Avoid `dynamic`; prefer strongly typed models

## Project structure

- **Controllers**: thin, delegate to services, return `IActionResult` or typed result
- **Services**: business logic, registered via DI in `Program.cs`
- **Models**: domain entities in `Models/`, enums in `Models/Enums/`, view models in `ViewModels/`
- **Data**: `ApplicationDbContext` in `Data/`, migrations auto-generated

## Dependency injection

Register services in `Program.cs`. Use interfaces for testability. Prefer scoped for per-request services.

## Migrations

```powershell
dotnet ef migrations add <MigrationName> --project src/OfficeTaskManagement.Web
```

## Testing pattern

Each test class gets its own `InMemoryDatabase`:
```csharp
var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
    .Options;
```

Use Moq for mocking services:
```csharp
var mockResourceService = new Mock<IResourceService>();
```

## Git workflow

- Branch from `main`
- Commit: conventional commits (`feat:`, `fix:`, `docs:`, `refactor:`)
- PR: all tests green before opening