using OfficeTaskManagement.Services.Codebase.Chunkers;

namespace OfficeTaskManagement.Services.Codebase;

/// <summary>
/// Maps file extensions to their optimal chunker implementation.
/// Spec: ai-agent-plan/04_CODEBASE_RAG.md → Chunker Registry
/// </summary>
public static class ChunkerRegistry
{
    private static readonly LineWindowChunker _lineWindow = new();
    private static readonly CSharpChunker     _csharp     = new();
    private static readonly MarkdownChunker   _markdown   = new();

    public static IChunker GetChunker(string fileExtension) =>
        fileExtension.ToLowerInvariant() switch
        {
            ".cs"     => _csharp,     // Roslyn class + method level
            ".md"     => _markdown,   // H2 heading level
            ".js"     => _lineWindow, // 50-line sliding window
            ".ts"     => _lineWindow,
            ".py"     => _lineWindow,
            ".sql"    => _lineWindow,
            ".yaml"   => _lineWindow,
            ".yml"    => _lineWindow,
            ".json"   => _lineWindow,
            ".cshtml" => _lineWindow,
            ".html"   => _lineWindow,
            ".csproj" => _lineWindow,
            _         => _lineWindow  // Fallback
        };
}
