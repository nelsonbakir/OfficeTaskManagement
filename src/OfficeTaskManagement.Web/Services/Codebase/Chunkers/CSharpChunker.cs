using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OfficeTaskManagement.Services.Codebase.Chunkers;

/// <summary>
/// Splits a .cs file into semantic chunks at class and method boundaries using Roslyn.
/// Spec: ai-agent-plan/04_CODEBASE_RAG.md → C# Chunker Logic
/// </summary>
public sealed class CSharpChunker : IChunker
{
    private const int MaxChunkChars = 3000;

    public IEnumerable<CodeChunk> Chunk(string filePath, string content)
    {
        // Parse first — if Roslyn fails, we return the fallback outside the try
        SyntaxTree? tree = null;
        bool parseSucceeded = false;

        try
        {
            tree = CSharpSyntaxTree.ParseText(content);
            parseSucceeded = true;
        }
        catch
        {
            // handled below
        }

        if (!parseSucceeded || tree == null)
        {
            // Fallback: return whole file as single chunk
            yield return new CodeChunk(filePath, "file", 0, Truncate(content));
            yield break;
        }

        var root    = tree.GetRoot();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();

        if (!classes.Any())
        {
            // No classes — could be a record, interface, enum or top-level statements
            yield return new CodeChunk(filePath, "file", 0, Truncate(content));
            yield break;
        }

        foreach (var classDecl in classes)
        {
            var startLine = classDecl.GetLocation().GetLineSpan().StartLinePosition.Line;

            // Class-level chunk: identifier, base types, fields, properties (no method bodies)
            var header = ExtractClassHeader(classDecl);
            if (!string.IsNullOrWhiteSpace(header))
            {
                yield return new CodeChunk(
                    filePath,
                    "class_header",
                    startLine,
                    Truncate($"// {filePath}\n{header}"));
            }

            // Method-level chunks
            foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                var methodLine = method.GetLocation().GetLineSpan().StartLinePosition.Line;
                var methodText = $"// {filePath}\n{classDecl.Identifier} :: {method.Identifier}\n{method.ToFullString()}";
                yield return new CodeChunk(
                    filePath,
                    "method",
                    methodLine,
                    Truncate(methodText));
            }
        }
    }

    private static string ExtractClassHeader(ClassDeclarationSyntax classDecl)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(classDecl.Modifiers.ToString() + " class " + classDecl.Identifier.ToString());
        if (classDecl.BaseList != null)
            sb.AppendLine("    : " + classDecl.BaseList.ToString());

        foreach (var member in classDecl.Members)
        {
            if (member is FieldDeclarationSyntax or PropertyDeclarationSyntax)
                sb.AppendLine("  " + member.ToString().Trim());
        }
        return sb.ToString();
    }

    private static string Truncate(string s) =>
        s.Length <= MaxChunkChars ? s : s[..MaxChunkChars] + "\n// [truncated]";
}
