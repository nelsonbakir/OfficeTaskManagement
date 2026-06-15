namespace OfficeTaskManagement.Services.Codebase.Chunkers;

/// <summary>
/// Fallback chunker: splits any file into 50-line overlapping windows.
/// Used for .js, .ts, .py, .sql, .yaml, .json, .cshtml and unknown file types.
/// Spec: ai-agent-plan/04_CODEBASE_RAG.md → T32
/// </summary>
public sealed class LineWindowChunker : IChunker
{
    private const int WindowSize = 50;
    private const int StepSize   = 40; // 10-line overlap

    public IEnumerable<CodeChunk> Chunk(string filePath, string content)
    {
        var lines = content.Split('\n');
        if (lines.Length == 0) yield break;

        // If the whole file is smaller than one window, return it as-is
        if (lines.Length <= WindowSize)
        {
            yield return new CodeChunk(filePath, "window", 0, content.Trim());
            yield break;
        }

        for (int start = 0; start < lines.Length; start += StepSize)
        {
            var windowLines = lines.Skip(start).Take(WindowSize).ToArray();
            var text = string.Join('\n', windowLines).Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return new CodeChunk(filePath, "window", start + 1, text);
            }
        }
    }
}
