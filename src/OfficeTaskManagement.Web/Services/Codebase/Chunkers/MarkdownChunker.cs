using System.Text.RegularExpressions;

namespace OfficeTaskManagement.Services.Codebase.Chunkers;

/// <summary>
/// Splits Markdown files into sections at H2 (##) headings.
/// Spec: ai-agent-plan/04_CODEBASE_RAG.md → Markdown Chunker Logic
/// </summary>
public sealed class MarkdownChunker : IChunker
{
    private const int MinChunkChars = 100;
    private const int MaxChunkChars = 2000;

    private static readonly Regex SectionSplit = new(
        @"(?=^## )", RegexOptions.Multiline | RegexOptions.Compiled);

    public IEnumerable<CodeChunk> Chunk(string filePath, string content)
    {
        var sections = SectionSplit.Split(content);
        int lineOffset = 0;

        foreach (var section in sections)
        {
            var trimmed = section.Trim();
            if (trimmed.Length >= MinChunkChars)
            {
                yield return new CodeChunk(
                    filePath,
                    "section",
                    lineOffset,
                    trimmed.Length <= MaxChunkChars ? trimmed : trimmed[..MaxChunkChars]);
            }
            // Advance line count estimate
            lineOffset += section.Count(c => c == '\n') + 1;
        }
    }
}
