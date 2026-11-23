namespace Codepad.Application.Editing;

/// <summary>The suggestions for one caret position in one version of the source.</summary>
/// <param name="ReplaceStart">The zero-based offset where the typed word starts. An accepted item replaces the text from here to the caret.</param>
/// <param name="ReplaceLength">The number of typed characters to replace. It can be 0.</param>
/// <param name="Items">The suggestions. Empty when the position has none.</param>
public sealed record CompletionResult(int ReplaceStart, int ReplaceLength, IReadOnlyList<CompletionItem> Items)
{
    /// <summary>Gets a result with no suggestions.</summary>
    public static CompletionResult None { get; } = new CompletionResult(0, 0, []);
}
