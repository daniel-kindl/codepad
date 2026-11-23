using Codepad.Application.Editing;
using Codepad.Language;

namespace Codepad.Tests.Editing;

public sealed class CSharpHighlighterTests
{
    private readonly CSharpHighlighter highlighter = new CSharpHighlighter();

    [Theory]
    [InlineData("int", HighlightKind.Keyword)]
    [InlineData("var x = 1;", HighlightKind.Keyword)]
    [InlineData("await Task.Delay(1);", HighlightKind.Keyword)]
    [InlineData("foreach", HighlightKind.Keyword)]
    [InlineData("// note", HighlightKind.Comment)]
    [InlineData("/* note */", HighlightKind.Comment)]
    [InlineData("/// <summary>x</summary>", HighlightKind.Comment)]
    [InlineData("\"text\"", HighlightKind.StringLiteral)]
    [InlineData("'c'", HighlightKind.StringLiteral)]
    [InlineData("$\"a{1}b\"", HighlightKind.StringLiteral)]
    [InlineData("\"\"\"raw\"\"\"", HighlightKind.StringLiteral)]
    [InlineData("42", HighlightKind.Number)]
    [InlineData("3.14m", HighlightKind.Number)]
    [InlineData("0xFF", HighlightKind.Number)]
    [InlineData("=>", HighlightKind.Punctuation)]
    [InlineData(";", HighlightKind.Punctuation)]
    public void ClassifiesTheFirstTokenOfEachKind(string source, HighlightKind expected)
    {
        IReadOnlyList<HighlightSpan> spans = highlighter.Highlight(source);

        Assert.Equal(expected, spans[0].Kind);
        Assert.Equal(0, spans[0].Start);
    }

    [Fact]
    public void ColorsATypeNameAndLeavesAVariableNamePlain()
    {
        const string SOURCE = "List<string> names = new();";

        IReadOnlyList<HighlightSpan> spans = highlighter.Highlight(SOURCE);

        Assert.Contains(spans, span => span.Kind == HighlightKind.Type && SOURCE.Substring(span.Start, span.Length) == "List");
        Assert.Contains(spans, span => span.Kind == HighlightKind.Keyword && SOURCE.Substring(span.Start, span.Length) == "string");
        Assert.DoesNotContain(spans, span => SOURCE.Substring(span.Start, span.Length) == "names");
    }

    [Fact]
    public void KeepsOffsetsExactAcrossLinesAndDoesNotOverlap()
    {
        const string SOURCE = "int a = 1; // one\r\nstring s = \"x\";\n/* multi\nline */ return a;";

        IReadOnlyList<HighlightSpan> spans = highlighter.Highlight(SOURCE);

        int end = 0;
        foreach (HighlightSpan span in spans)
        {
            Assert.True(span.Start >= end, "Spans overlap or are out of order.");
            Assert.True(span.Length > 0);
            Assert.True(span.Start + span.Length <= SOURCE.Length);
            end = span.Start + span.Length;
        }

        HighlightSpan comment = Assert.Single(spans, span => span.Kind == HighlightKind.Comment && SOURCE[span.Start] == '/' && SOURCE[span.Start + 1] == '/');
        Assert.Equal("// one", SOURCE.Substring(comment.Start, comment.Length));
        HighlightSpan block = Assert.Single(spans, span => SOURCE.Substring(span.Start, span.Length).StartsWith("/*", StringComparison.Ordinal));
        Assert.Equal("/* multi\nline */", SOURCE.Substring(block.Start, block.Length));
    }

    [Fact]
    public void ClassifiesAKeywordAtTheStartOfEachLine()
    {
        const string SOURCE = "// c\nList<int> a = new();\nint total = 1;\nint other = 2;";

        IReadOnlyList<HighlightSpan> spans = highlighter.Highlight(SOURCE);

        foreach (int offset in new[] { SOURCE.IndexOf("int total", StringComparison.Ordinal), SOURCE.IndexOf("int other", StringComparison.Ordinal) })
        {
            Assert.Contains(spans, span => span.Start == offset && span.Length == 3 && span.Kind == HighlightKind.Keyword);
        }
    }

    [Fact]
    public void EmptySourceHasNoSpans()
    {
        Assert.Empty(highlighter.Highlight(string.Empty));
    }

    [Fact]
    public void BrokenSourceStillHighlights()
    {
        IReadOnlyList<HighlightSpan> spans = highlighter.Highlight("if (x { \"open");

        Assert.Contains(spans, span => span.Kind == HighlightKind.Keyword);
    }
}
