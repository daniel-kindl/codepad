namespace Codepad.Application.Editing;

/// <summary>The result of the analysis of one version of the source.</summary>
/// <param name="Diagnostics">The diagnostics, most serious first, then in source order.</param>
public sealed record EditorAnalysis(IReadOnlyList<EditorDiagnostic> Diagnostics)
{
    /// <summary>Gets an analysis with no diagnostics.</summary>
    public static EditorAnalysis Empty { get; } = new EditorAnalysis([]);
}
