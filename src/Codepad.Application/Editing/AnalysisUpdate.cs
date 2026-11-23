namespace Codepad.Application.Editing;

/// <summary>A finished analysis and the version of the source that it belongs to.</summary>
/// <param name="Version">The version that the coordinator counted when it started the analysis.</param>
/// <param name="Analysis">The result.</param>
public sealed record AnalysisUpdate(long Version, EditorAnalysis Analysis);
