using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using SimpleParser;

public static class RoslynAstService
{
    public const int MaxSerializedNodes = 12_000;
    private const int MaxDepth = 256;

    public static RoslynParseResponse Parse(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var root = tree.GetRoot();
        var counter = new Counter();
        var ast = Serialize(root, source, counter, 0);
        var diagnostics = tree.GetDiagnostics().Select(diagnostic =>
        {
            var location = diagnostic.Location.IsInSource ? diagnostic.Location.GetLineSpan().StartLinePosition : default;
            return new RoslynDiagnosticDto(diagnostic.Id, diagnostic.Severity.ToString(), diagnostic.GetMessage(),
                diagnostic.Location.IsInSource ? new SourceLocation(location.Line + 1, location.Character + 1) : null);
        }).ToArray();
        return new RoslynParseResponse(!diagnostics.Any(diagnostic => diagnostic.Severity == "Error"), ast, counter.Value, diagnostics);
    }

    private static AstNodeDto Serialize(SyntaxNodeOrToken element, string source, Counter counter, int depth)
    {
        if (depth > MaxDepth) throw new AstLimitException($"Syntax tree nesting exceeds {MaxDepth} levels.");
        if (++counter.Value > MaxSerializedNodes)
            throw new AstLimitException($"Syntax tree exceeds the {MaxSerializedNodes:N0}-node visualization limit. Reduce the source or inspect a smaller section.");

        var span = element.Span;
        var lineSpan = element.GetLocation()!.GetLineSpan();
        var start = lineSpan.StartLinePosition;
        var end = lineSpan.EndLinePosition;
        var position = new SourceLocation(start.Line + 1, start.Character + 1);
        var sourceSpan = new SourceSpanDto(span.Start, span.Length, start.Line + 1, start.Character + 1, end.Line + 1, end.Character + 1, element.IsMissing);
        var rawText = span.Length == 0 ? string.Empty : source.Substring(span.Start, span.Length);
        var text = Snippet(rawText, 140);

        if (element.IsNode)
        {
            var node = element.AsNode()!;
            var kind = node.Kind().ToString();
            var children = node.ChildNodesAndTokens().Select(child => Serialize(child, source, counter, depth + 1)).ToArray();
            var properties = new Dictionary<string, object?>
            {
                ["kind"] = kind,
                ["rawKind"] = node.RawKind,
                ["isMissing"] = node.IsMissing,
                ["sourceText"] = text
            };
            return new AstNodeDto("SyntaxNode", text.Length == 0 ? kind : $"{kind} · {text}", text, properties, children, position, sourceSpan);
        }

        var token = element.AsToken();
        var tokenKind = token.Kind().ToString();
        var tokenText = token.Text;
        var tokenProperties = new Dictionary<string, object?>
        {
            ["kind"] = tokenKind,
            ["rawKind"] = token.RawKind,
            ["isMissing"] = token.IsMissing,
            ["isKeyword"] = token.IsKeyword(),
            ["sourceText"] = Snippet(tokenText, 140)
        };
        return new AstNodeDto("SyntaxToken", tokenText.Length == 0 ? $"{tokenKind} (missing)" : $"{tokenKind} · {Snippet(tokenText, 60)}",
            tokenText, tokenProperties, [], position, sourceSpan);
    }

    private static string Snippet(string text, int maxLength)
    {
        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength ? normalized : normalized[..(maxLength - 1)] + "…";
    }

    private sealed class Counter { public int Value; }
}

public sealed record SourceSpanDto(int Start, int Length, int StartLine, int StartColumn, int EndLine, int EndColumn, bool IsMissing);
public sealed record RoslynDiagnosticDto(string Id, string Severity, string Message, SourceLocation? Location);
public sealed record RoslynParseResponse(bool Success, AstNodeDto Ast, int NodeCount, IReadOnlyList<RoslynDiagnosticDto> Diagnostics);
public sealed class AstLimitException(string message) : Exception(message);
