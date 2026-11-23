namespace Codepad.Application.Editing;

/// <summary>Classifies C# source for syntax coloring. It reads the syntax only, so it is fast enough for each key press.</summary>
public interface ICSharpHighlighter
{
    /// <summary>Classifies the source.</summary>
    /// <param name="source">The source text.</param>
    /// <returns>The classified ranges in source order. They do not overlap. Plain text has no range.</returns>
    IReadOnlyList<HighlightSpan> Highlight(string source);
}
