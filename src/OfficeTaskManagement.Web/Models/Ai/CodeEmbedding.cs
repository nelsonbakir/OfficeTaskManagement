using Pgvector;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.Models.Ai
{
    /// <summary>
    /// Stores a semantic embedding for a chunk of code or documentation
    /// from the Git repository. Used by CodebaseRetrievalService for
    /// vector similarity search (pgvector).
    /// </summary>
    public class CodeEmbedding : IMustHaveTenant
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Tenant scope — prevents cross-tenant code leakage.</summary>
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int ProjectId { get; set; }
        public Project Project { get; set; } = null!;

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
        /// 768-dimensional vector from Gemini text-embedding-004.
        /// </summary>
        [Required]
        public Vector Embedding { get; set; } = null!;

        /// <summary>MD5 hash of the file at index time. Used to skip unchanged files.</summary>
        [StringLength(32)]
        public string FileHash { get; set; } = string.Empty;

        public DateTimeOffset IndexedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
