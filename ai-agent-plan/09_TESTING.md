# Testing Plan
**OfficeTaskManagement · AI Agent Integration · xUnit + Moq + InMemory**

---

## Testing Strategy

Follow existing project pattern: **xUnit + Moq + EF Core InMemory** (no real DB, no real Gemini API in tests).

All AI service tests mock `IGeminiAiService` or mock the underlying `HttpClient` to return controlled JSON.

---

## Test Files to Create

```
Tests/OfficeTaskManagement.Tests/
├── Services/
│   ├── GeminiAiServiceTests.cs          ← Core: prompt building, JSON parsing, fallback
│   ├── ContextBuilderServiceTests.cs    ← Token budget logic, compression rules
│   ├── PmKnowledgeServiceTests.cs       ← History stats, hourly rate calculation
│   ├── CodebaseRetrievalServiceTests.cs ← Chunk retrieval (mock pgvector)
│   └── AgentToolDispatcherTests.cs      ← Function call routing (Phase 4)
├── Controllers/
│   └── AiEstimationControllerTests.cs  ← Endpoint contract tests
└── Integration/
    └── BulkCreateTests.cs              ← Transaction rollback on failure
```

---

## GeminiAiServiceTests.cs

```csharp
public class GeminiAiServiceTests
{
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;
    private readonly GeminiAiService _service;

    public GeminiAiServiceTests()
    {
        _httpHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(_httpHandlerMock.Object);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Gemini:ApiKey", "test-key" },
                { "Gemini:GenerativeModel", "gemini-2.5-flash" }
            })
            .Build();

        // Build InMemory DB + mock ContextBuilderService
        var db = CreateInMemoryDb();
        var contextBuilder = new Mock<ContextBuilderService>(db, null!, null!, null!);
        contextBuilder.Setup(c => c.BuildContextAsync(It.IsAny<EstimationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptContext { HourlyRateBDT = 800 });

        _service = new GeminiAiService(httpClient, config, contextBuilder.Object, null!);
    }

    [Fact]
    public async Task EstimateAsync_ValidResponse_ReturnsPertHours()
    {
        // Arrange — mock Gemini returning well-formed JSON
        SetupGeminiResponse(new
        {
            optimisticHours    = 4.0,
            mostLikelyHours    = 8.0,
            pessimisticHours   = 16.0,
            pertHours          = 9.0,
            priority           = "High",
            storyPoints        = 8,
            estimatedBudgetBDT = 7200.0,
            confidence         = "Medium",
            rationale          = "Based on 3 similar tasks.",
            risks              = new[] { "Scope creep risk" }
        });

        var request = new EstimationRequest("Task", "Implement JWT Auth", null, 1, null, null, null);

        // Act
        var result = await _service.EstimateAsync(request);

        // Assert
        Assert.Equal(4.0m,  result.OptimisticHours);
        Assert.Equal(8.0m,  result.MostLikelyHours);
        Assert.Equal(16.0m, result.PessimisticHours);
        Assert.Equal(9.0m,  result.PertHours);
        Assert.Equal("High", result.Priority);
        Assert.Single(result.Risks);
    }

    [Fact]
    public async Task EstimateAsync_ApiKeyMissing_ReturnsFallbackEstimate()
    {
        // Arrange — service with no API key
        var configNoKey = new ConfigurationBuilder().Build();
        var service = new GeminiAiService(new HttpClient(), configNoKey, null!, null!);

        // Act
        var result = await service.EstimateAsync(
            new EstimationRequest("Task", "Test task", null, null, null, null, null));

        // Assert — falls back to safe defaults, does not throw
        Assert.NotNull(result);
        Assert.Equal("Low", result.Confidence);
        Assert.Contains("unavailable", result.Rationale, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EstimateAsync_MalformedJson_ReturnsFallback()
    {
        // Arrange — Gemini returns garbage JSON
        SetupGeminiRawResponse("{ malformed json }}}");

        var result = await _service.EstimateAsync(
            new EstimationRequest("Feature", "Login UI", null, 1, null, null, null));

        // Should not throw — returns fallback
        Assert.NotNull(result);
        Assert.Equal("Low", result.Confidence);
    }

    [Fact]
    public async Task EstimateAsync_ApiReturns429_RetriesAndSucceeds()
    {
        // First call: 429 Too Many Requests
        // Second call: 200 OK
        int callCount = 0;
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                return OkGeminiResponse(new { optimisticHours=4, mostLikelyHours=8, pessimisticHours=16,
                    pertHours=9, priority="Medium", storyPoints=5, estimatedBudgetBDT=6400,
                    confidence="Medium", rationale="Test", risks=new string[0] });
            });

        var result = await _service.EstimateAsync(
            new EstimationRequest("Task", "Test", null, 1, null, null, null));

        Assert.Equal(2, callCount);
        Assert.Equal(8.0m, result.MostLikelyHours);
    }

    // Helpers
    private void SetupGeminiResponse(object responsePayload) { /* ... */ }
    private void SetupGeminiRawResponse(string rawJson) { /* ... */ }
    private HttpResponseMessage OkGeminiResponse(object payload) { /* ... */ }
    private ApplicationDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ApplicationDbContext(opts);
    }
}
```

---

## ContextBuilderServiceTests.cs

```csharp
public class ContextBuilderServiceTests
{
    [Fact]
    public async Task BuildContext_SiblingList_CompressesNamesToCommaSeparated()
    {
        // Arrange — project with 4 epics
        var db = CreateInMemoryDbWithData();
        var service = new ContextBuilderService(db, Mock.Of<CodebaseRetrievalService>(),
            Mock.Of<PmKnowledgeService>(), new MemoryCache(new MemoryCacheOptions()));

        var request = new EstimationRequest("Epic", "New Epic", null, 1, null, null, null);

        // Act
        var ctx = await service.BuildContextAsync(request);

        // Assert — siblings are comma-separated names, no descriptions
        Assert.Contains("Login", ctx.SiblingList);
        Assert.Contains("Payroll", ctx.SiblingList);
        Assert.DoesNotContain("Description:", ctx.SiblingList); // No raw descriptions
    }

    [Fact]
    public async Task BuildContext_TokenBudgetExceeded_DropsCodeChunks()
    {
        // Arrange — mock code retrieval returning large chunks
        var codeRetrieval = new Mock<CodebaseRetrievalService>();
        codeRetrieval.Setup(c => c.GetRelevantChunksAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Repeat(new string('x', 2000), 3).ToList()); // 3 × 2000 char chunks

        var service = new ContextBuilderService(/* ... with tight budget ... */);
        var ctx = await service.BuildContextAsync(new EstimationRequest("Epic", "X", null, 1, null, null, null));

        // When budget is exhausted, code chunks should be null or empty
        Assert.True(ctx.CodeChunks == null || ctx.CodeChunks.Count == 0);
    }

    [Fact]
    public void EstimateTokens_Text_Returns25PercentOfCharCount()
    {
        var method = typeof(ContextBuilderService)
            .GetMethod("EstimateTokens", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var result = (int)method!.Invoke(null, new object[] { "hello world" })!;
        Assert.Equal(2, result); // 11 chars / 4 ≈ 2 tokens
    }
}
```

---

## AiEstimationControllerTests.cs

```csharp
public class AiEstimationControllerTests
{
    private readonly Mock<IGeminiAiService> _aiMock = new();
    private readonly AiEstimationController _controller;

    public AiEstimationControllerTests()
    {
        var db = CreateInMemoryDb();
        _controller = new AiEstimationController(_aiMock.Object, db,
            Mock.Of<AiEstimationLogService>(), Mock.Of<IWorkflowEngineService>())
        {
            ControllerContext = CreateAuthContext()
        };
    }

    [Fact]
    public async Task EstimateAsync_ReturnsOk_WithEstimationResult()
    {
        var expected = new EstimationResult(4, 8, 16, 9, "High", 8, 7200, "Medium", "Test", Array.Empty<string>(), 500, 200);
        _aiMock.Setup(s => s.EstimateAsync(It.IsAny<EstimationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _controller.EstimateAsync(
            new EstimationRequest("Task", "Test", null, 1, null, null, null), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var data = Assert.IsType<EstimationResult>(ok.Value);
        Assert.Equal(9m, data.PertHours);
    }

    [Fact]
    public async Task BulkCreateAsync_CreatesFeatures_ReturnsCreatedIds()
    {
        var request = new BulkCreateRequest(new[]
        {
            new BulkCreateItemDto("Feature", 1, "Login UI", "Login desc", null, "High", null, 8, null),
            new BulkCreateItemDto("Feature", 1, "Password Reset", "Reset desc", null, "Medium", null, 6, null)
        });

        var result = await _controller.BulkCreateAsync(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var data = Assert.IsType<BulkCreateResult>(ok.Value);
        Assert.Equal(2, data.CreatedIds.Length);
    }

    [Fact]
    public async Task BulkCreateAsync_CreatesTask_WithPertCalculation()
    {
        var request = new BulkCreateRequest(new[]
        {
            new BulkCreateItemDto("Task", 1, "Implement API", null, null, "High", 4m, 8m, 16m)
        });

        await _controller.BulkCreateAsync(request, CancellationToken.None);

        var db = GetDb();
        var task = await db.Tasks.FirstAsync(t => t.Title == "Implement API");
        Assert.Equal(9m, task.PertEstimatedHours); // (4 + 4*8 + 16) / 6 = 9
        Assert.Equal(9m, task.EstimatedHours);
    }

    [Fact]
    public async Task BulkCreateAsync_DbFailure_RollsBackAllItems()
    {
        // Simulate DB failure midway — all items should be rolled back
        // (Tested via transaction behavior with InMemory DB exception injection)
        // ... test body ...
    }
}
```

---

## BulkCreateTests.cs (Integration)

```csharp
public class BulkCreateTests : IClassFixture<WebApplicationFactory<Program>>
{
    // Full integration test using TestServer
    // Verifies: auth required, CSRF token, transaction rollback, redirect URL

    [Fact]
    public async Task BulkCreate_UnauthorizedRequest_Returns401()
    {
        var client = _factory.CreateClient(); // No auth cookie
        var response = await client.PostAsJsonAsync("/api/ai/bulk-create",
            new BulkCreateRequest(Array.Empty<BulkCreateItemDto>()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

---

## Running Tests

```bash
# All tests
dotnet test

# AI service tests only
dotnet test --filter "FullyQualifiedName~GeminiAiService"

# Context builder tests
dotnet test --filter "FullyQualifiedName~ContextBuilder"

# Controller tests
dotnet test --filter "FullyQualifiedName~AiEstimationController"

# All AI-related tests
dotnet test --filter "FullyQualifiedName~Ai"
```

---

## Mock Gemini Response Helper

Add to `Tests/Helpers/GeminiMockHelper.cs`:

```csharp
public static class GeminiMockHelper
{
    /// <summary>
    /// Wraps a payload object in the Gemini API response envelope.
    /// </summary>
    public static string WrapInGeminiResponse(object payload)
    {
        return JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new { text = JsonSerializer.Serialize(payload) }
                        }
                    }
                }
            },
            usageMetadata = new { promptTokenCount = 500, candidatesTokenCount = 200 }
        });
    }
}
```
