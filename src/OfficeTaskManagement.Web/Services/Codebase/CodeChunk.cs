namespace OfficeTaskManagement.Services.Codebase;

/// <summary>Represents a single semantic chunk extracted from a source file.</summary>
public record CodeChunk(
    string FilePath,
    string ChunkType,
    int? StartLine,
    string Text
);
