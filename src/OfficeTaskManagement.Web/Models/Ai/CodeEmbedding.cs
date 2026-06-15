using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OfficeTaskManagement.Models.Ai
{
    /// <summary>
    /// Stores a semantic embedding for a chunk of code or documentation
    /// from the Git repository. Used by CodebaseRetrievalService for
    /// vector similarity search (pgvector).
    /// </summary>
    public class CodeEmbedding
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Tenant scope — prevents cross-tenant code leakage.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Relative path from repo root, e.g. "src/Services/ResourceService.cs".</summary>
        [Required]
        [StringLength(500)]
        public string FilePath { get; set; } = string.Empty;

        /// <summary>Chunking type: class_header | method | function | section | statement | window</summary>
        [StringLength(50)]
        public string ChunkType { get; set; } = string.Empty;

        /// <summary>Line number in the file where this chunk starts (1-indexed).</summary>
        public int? StartLine { get; set; }

        /// <summary>The actual text that was embedded. Max 3000 chars.</summary>
        [Required]
        public string ChunkText { get; set; } = string.Empty;

        /// <summary>
        /// 768-dimensional float vector from Gemini text-embedding-004.
        /// Stored as TEXT (JSON array) in SQLite dev; overridden to vector(768) in PostgreSQL migration.
        /// </summary>
        public float[] Embedding { get; set; } = Array.Empty<float>();

        /// <summary>MD5 hash of the file at index time. Used to skip unchanged files.</summary>
        [StringLength(32)]
        public string FileHash { get; set; } = string.Empty;

        public DateTimeOffset IndexedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
