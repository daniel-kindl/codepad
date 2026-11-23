namespace Codepad.Application.Editing;

/// <summary>Suggests keywords, names in scope and members for C# source. It never runs the source and never uses the network.</summary>
public interface ICSharpCompletionProvider
{
    /// <summary>Finds the suggestions for one caret position in one immutable version of the source.</summary>
    /// <param name="source">The source text.</param>
    /// <param name="caret">The zero-based caret offset.</param>
    /// <param name="cancellationToken">Cancels the request when a newer edit or another document makes it obsolete.</param>
    /// <returns>The suggestions, or <see cref="CompletionResult.None"/>.</returns>
    Task<CompletionResult> GetCompletionsAsync(string source, int caret, CancellationToken cancellationToken);
}
