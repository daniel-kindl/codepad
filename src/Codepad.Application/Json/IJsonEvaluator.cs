namespace Codepad.Application.Json;

/// <summary>Checks and formats one JSON text. An implementation is side-effect-free: it reads no file and no network, and it runs no user code.</summary>
public interface IJsonEvaluator
{
    /// <summary>Evaluates one text.</summary>
    /// <param name="text">The JSON text.</param>
    /// <param name="cancellationToken">Asks the work to stop. An implementation can ignore it. The caller drops a late result.</param>
    /// <returns>The result. An invalid text is a result, not an exception.</returns>
    Task<JsonEvaluation> EvaluateAsync(string text, CancellationToken cancellationToken);
}
