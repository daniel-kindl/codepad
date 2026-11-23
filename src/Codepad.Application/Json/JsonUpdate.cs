namespace Codepad.Application.Json;

/// <summary>The evaluation of one version of the text.</summary>
/// <param name="Version">The version that the coordinator gave to the text.</param>
/// <param name="Evaluation">The result.</param>
public sealed record JsonUpdate(long Version, JsonEvaluation Evaluation);
