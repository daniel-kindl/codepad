namespace Codepad.Application.Editing;

/// <summary>Finds the syntax and semantic diagnostics of C# source. It never runs the source.</summary>
public interface ICSharpAnalyzer
{
    /// <summary>Analyzes one immutable version of the source.</summary>
    /// <param name="source">The source text.</param>
    /// <param name="cancellationToken">Cancels the analysis when a newer version exists.</param>
    /// <returns>The diagnostics of this version.</returns>
    Task<EditorAnalysis> AnalyzeAsync(string source, CancellationToken cancellationToken);
}
