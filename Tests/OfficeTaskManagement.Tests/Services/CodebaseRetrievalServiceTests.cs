using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
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
            _db = PostgresTestDb.CreateContextAsync().GetAwaiter().GetResult();
            _embeddingMock = new Mock<IGeminiEmbeddingService>();
        }

        public void Dispose()
        {
            var dbName = _db.Database.GetDbConnection().Database;
            _db.Dispose();
            if (!string.IsNullOrEmpty(dbName))
            {
                PostgresTestDb.DropDatabaseAsync(dbName).GetAwaiter().GetResult();
            }
        }

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

            var result = await service.GetRelevantChunksAsync("login feature", projectId: null, topK: 3);

            Assert.Empty(result);
        }

        // ── CodebaseRetrievalService — vector search returns matching chunk ──
        [Fact]
        public async Task GetRelevantChunksAsync_VectorMatch_ReturnsChunk()
        {
            var project = new Project { Name = "Test Project", TenantId = "test-tenant" };
            _db.Projects.Add(project);
            await _db.SaveChangesAsync();

            var dummyVector = new float[768];
            dummyVector[0] = 1.0f;
            _embeddingMock.Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dummyVector);

            _db.CodeEmbeddings.Add(new CodeEmbedding
            {
                TenantId  = "test-tenant",
                ProjectId = project.Id,
                FilePath  = "src/Services/AuthService.cs",
                ChunkType = "method",
                StartLine = 42,
                ChunkText = "public async Task<bool> LoginAsync(string email, string password) { ... }",
                Embedding = new Pgvector.Vector(dummyVector),
                FileHash  = "abc123"
            });
            await _db.SaveChangesAsync();

            var service = CreateService();

            var result = await service.GetRelevantChunksAsync("login authentication", project.Id, topK: 3);

            Assert.NotEmpty(result);
            Assert.Contains("AuthService.cs", result[0]);
        }

        // ── CodebaseRetrievalService — project isolation ──────────────────────
        [Fact]
        public async Task GetRelevantChunksAsync_ProjectIsolation_FiltersOutOtherProjects()
        {
            var p1 = new Project { Name = "Proj 1", TenantId = "test-tenant" };
            var p2 = new Project { Name = "Proj 2", TenantId = "test-tenant" };
            _db.Projects.AddRange(p1, p2);
            await _db.SaveChangesAsync();

            var dummyVector = new float[768];
            dummyVector[0] = 1.0f;
            _embeddingMock.Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dummyVector);

            _db.CodeEmbeddings.Add(new CodeEmbedding
            {
                TenantId  = "test-tenant",
                ProjectId = p1.Id,
                FilePath  = "p1/file.cs",
                ChunkType = "class",
                ChunkText = "Project 1 code",
                Embedding = new Pgvector.Vector(dummyVector),
                FileHash  = "hash1"
            });

            _db.CodeEmbeddings.Add(new CodeEmbedding
            {
                TenantId  = "test-tenant",
                ProjectId = p2.Id,
                FilePath  = "p2/file.cs",
                ChunkType = "class",
                ChunkText = "Project 2 code",
                Embedding = new Pgvector.Vector(dummyVector),
                FileHash  = "hash2"
            });
            await _db.SaveChangesAsync();

            var service = CreateService();

            var result = await service.GetRelevantChunksAsync("query", p1.Id, topK: 3);

            Assert.Single(result);
            Assert.Contains("p1/file.cs", result[0]);
            Assert.DoesNotContain("p2/file.cs", result[0]);
        }

        // ── Retrieval with empty query returns empty ──────────────────────────
        [Fact]
        public async Task GetRelevantChunksAsync_EmptyQuery_ReturnsEmpty()
        {
            var service = CreateService();

            var result = await service.GetRelevantChunksAsync("   ", projectId: null);

            Assert.Empty(result);
        }
    }
}
