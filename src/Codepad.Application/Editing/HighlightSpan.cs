namespace Codepad.Application.Editing;

/// <summary>A range of source that has one <see cref="HighlightKind"/>.</summary>
/// <param name="Kind">The class of the range.</param>
/// <param name="Start">The zero-based offset of the first character.</param>
/// <param name="Length">The number of characters. At least 1.</param>
public sealed record HighlightSpan(HighlightKind Kind, int Start, int Length);
