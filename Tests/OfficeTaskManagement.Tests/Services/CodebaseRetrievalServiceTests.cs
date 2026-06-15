using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models.Ai;
using OfficeTaskManagement.Services.Ai;
using OfficeTaskManagement.Services.Codebase;
using OfficeTaskManagement.Services.Codebase.Chunkers;
using Xunit;

namespace OfficeTaskManagement.Tests.Services
{
    public class CodebaseRetrievalServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly Mock<IGeminiEmbeddingService> _embeddingMock;

        public CodebaseRetrievalServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            _db = new ApplicationDbContext(options);
            _embeddingMock = new Mock<IGeminiEmbeddingService>();
        }

        public void Dispose() => _db.Dispose();

        private CodebaseRetrievalService CreateService()
            => new(_db, _embeddingMock.Object, NullLogger<CodebaseRetrievalService>.Instance);

        // ── LineWindowChunker ─────────────────────────────────────────────────
        [Fact]
        public void LineWindowChunker_SmallFile_ReturnsSingleChunk()
        {
            var chunker = new LineWindowChunker();
            var content = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"line {i}"));

            var chunks = chunker.Chunk("/test.js", content).ToList();

            Assert.Single(chunks);
            Assert.Equal("window", chunks[0].ChunkType);
        }

        [Fact]
        public void LineWindowChunker_LargeFile_ProducesMultipleWindows()
        {
            var chunker = new LineWindowChunker();
            var content = string.Join('\n', Enumerable.Range(1, 120).Select(i => $"code line {i}"));

            var chunks = chunker.Chunk("/big.js", content).ToList();

            Assert.True(chunks.Count > 1, "Large file should produce more than one window");
        }

        // ── MarkdownChunker ───────────────────────────────────────────────────
        [Fact]
        public void MarkdownChunker_SplitsAtH2Headings()
        {
            var chunker = new MarkdownChunker();
            var content = @"# Title

Some intro text that is short.

## Section One

This is the first section with enough content to meet the minimum threshold for chunking.
It contains multiple lines of text that provide meaningful context for the AI model.

## Section Two

This is the second section with enough content to meet the minimum threshold for chunking.
It also contains multiple lines of text that provide meaningful context for the AI model.";

            var chunks = chunker.Chunk("/test.md", content).ToList();

            Assert.Equal(2, chunks.Count);
            Assert.All(chunks, c => Assert.Equal("section", c.ChunkType));
        }

        // ── CSharpChunker ─────────────────────────────────────────────────────
        [Fact]
        public void CSharpChunker_ValidClass_ReturnsClassHeaderAndMethodChunks()
        {
            var chunker = new CSharpChunker();
            var code = @"
namespace MyApp
{
    public class MyService
    {
        private readonly string _name;
        public string Name => _name;

        public MyService(string name) { _name = name; }

        public string GetGreeting() => $""Hello, {_name}!"";
    }
}";

            var chunks = chunker.Chunk("/MyService.cs", code).ToList();

            Assert.True(chunks.Count >= 1, "Should produce at least a class_header chunk");
            Assert.Contains(chunks, c => c.ChunkType == "class_header");
        }

        [Fact]
        public void CSharpChunker_UnparsableCode_ReturnsFileFallback()
        {
            var chunker = new CSharpChunker();
            var code = "this is definitely not valid C#!!!! }}}}{{{{{";

            var chunks = chunker.Chunk("/broken.cs", code).ToList();

            // Should not throw; returns at least one chunk
            Assert.Single(chunks);
        }

        // ── CodebaseRetrievalService — empty DB returns empty ─────────────────
        [Fact]
        public async Task GetRelevantChunksAsync_EmptyDb_ReturnsEmpty()
        {
            var service = CreateService();

            var result = await service.GetRelevantChunksAsync("login feature", topK: 3);

            Assert.Empty(result);
        }

        // ── CodebaseRetrievalService — keyword fallback returns seeded chunk ──
        [Fact]
        public async Task GetRelevantChunksAsync_KeywordMatch_ReturnsChunk()
        {
            // Seed a code embedding with matching content
            _db.CodeEmbeddings.Add(new CodeEmbedding
            {
                FilePath  = "src/Services/AuthService.cs",
                ChunkType = "method",
                StartLine = 42,
                ChunkText = "public async Task<bool> LoginAsync(string email, string password) { ... }",
                Embedding = Array.Empty<float>(), // No real embedding in test
                FileHash  = "abc123"
            });
            await _db.SaveChangesAsync();

            var service = CreateService();

            // Act — uses keyword fallback (InMemory provider)
            var result = await service.GetRelevantChunksAsync("login authentication", topK: 3);

            Assert.NotEmpty(result);
            Assert.Contains("AuthService.cs", result[0]);
        }

        // ── Retrieval with empty query returns empty ──────────────────────────
        [Fact]
        public async Task GetRelevantChunksAsync_EmptyQuery_ReturnsEmpty()
        {
            var service = CreateService();

            var result = await service.GetRelevantChunksAsync("   ");

            Assert.Empty(result);
        }
    }
}
