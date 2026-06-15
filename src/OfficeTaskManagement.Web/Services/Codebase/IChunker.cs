namespace OfficeTaskManagement.Services.Codebase;

/// <summary>Contract for all language-specific file chunkers.</summary>
public interface IChunker
{
    /// <summary>
    /// Splits <paramref name="content"/> into semantic chunks suitable for embedding.
    /// </summary>
    IEnumerable<CodeChunk> Chunk(string filePath, string content);
}
