using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Codepad.Application.Editing;
using Codepad.Domain.Results;

namespace Codepad.Desktop.Editor;

/// <summary>
/// Colors C# syntax and underlines diagnostics. It classifies the whole text once for each text version, on the
/// first line that the editor draws, and reuses the result for every other line.
/// The colors come from a callback, so a theme change takes effect on the next redraw.
/// </summary>
internal sealed class SyntaxColorizer(ICSharpHighlighter highlighter, Func<HighlightKind, IBrush?> brushFor, Func<IReadOnlyList<EditorDiagnostic>> diagnostics, Func<DiagnosticSeverity, IBrush?> underlineBrushFor) : DocumentColorizingTransformer
{
    /// <summary>The longest text that the colorizer classifies. A longer text stays plain, so typing stays fast.</summary>
    internal const int MAX_CLASSIFIED_CHARS = 200_000;

    private IReadOnlyList<HighlightSpan> spans = [];
    private bool stale = true;

    /// <summary>Marks the cached classification as old. Call it after each text change.</summary>
    public void Invalidate() => stale = true;

    /// <inheritdoc />
    protected override void ColorizeLine(DocumentLine line)
    {
        if (stale)
        {
            spans = CurrentContext.Document.TextLength <= MAX_CLASSIFIED_CHARS ? highlighter.Highlight(CurrentContext.Document.Text) : [];
            stale = false;
        }

        int lineStart = line.Offset;
        int lineEnd = line.EndOffset;
        foreach (HighlightSpan span in SpansIn(lineStart, lineEnd))
        {
            IBrush? brush = brushFor(span.Kind);
            if (brush is null)
            {
                continue;
            }

            int start = Math.Max(span.Start, lineStart);
            int end = Math.Min(span.Start + span.Length, lineEnd);
            if (end > start)
            {
                ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush));
            }
        }

        foreach (EditorDiagnostic diagnostic in diagnostics())
        {
            int start = Math.Max(diagnostic.Start, lineStart);
            int end = Math.Min(Math.Max(diagnostic.Start + diagnostic.Length, diagnostic.Start + 1), lineEnd);
            IBrush? brush = underlineBrushFor(diagnostic.Severity);
            if (end > start && brush is not null)
            {
                TextDecorationCollection decorations =
                [
                    new TextDecoration { Location = TextDecorationLocation.Underline, Stroke = brush, StrokeThickness = 1.5 },
                ];
                ChangeLinePart(start, end, element => element.TextRunProperties.SetTextDecorations(decorations));
            }
        }
    }

    private IEnumerable<HighlightSpan> SpansIn(int lineStart, int lineEnd)
    {
        // The spans are sorted and do not overlap, so a binary search finds the first span that can touch the line.
        int low = 0;
        int high = spans.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (spans[middle].Start + spans[middle].Length <= lineStart)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        for (int index = low; index < spans.Count && spans[index].Start < lineEnd; index++)
        {
            yield return spans[index];
        }
    }
}
