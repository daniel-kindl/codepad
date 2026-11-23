using Codepad.Domain.Results;

namespace Codepad.Application.Json;

/// <summary>
/// The result of one live JSON evaluation. A valid document has the formatted text and a bounded tree. An invalid
/// document has one error diagnostic. The user input is never part of the result: the editor keeps it as typed.
/// </summary>
public sealed record JsonEvaluation
{
    private JsonEvaluation(string? formatted, ResultNode? tree, Diagnostic? problem)
    {
        Formatted = formatted;
        Tree = tree;
        Problem = problem;
    }

    /// <summary>Gets the indented text of a valid document, or null.</summary>
    public string? Formatted { get; }

    /// <summary>Gets the bounded tree of a valid document, or null.</summary>
    public ResultNode? Tree { get; }

    /// <summary>Gets the error of an invalid document, or null.</summary>
    public Diagnostic? Problem { get; }

    /// <summary>Gets a value indicating whether the document is valid JSON within the limits.</summary>
    public bool IsValid => Problem is null;

    /// <summary>Creates the result of a valid document.</summary>
    /// <param name="formatted">The indented text.</param>
    /// <param name="tree">The bounded tree.</param>
    /// <returns>The result.</returns>
    public static JsonEvaluation Valid(string formatted, ResultNode tree)
    {
        ArgumentNullException.ThrowIfNull(formatted);
        ArgumentNullException.ThrowIfNull(tree);
        return new JsonEvaluation(formatted, tree, null);
    }

    /// <summary>Creates the result of an invalid document.</summary>
    /// <param name="problem">The error.</param>
    /// <returns>The result.</returns>
    public static JsonEvaluation Invalid(Diagnostic problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return new JsonEvaluation(null, null, problem);
    }
}
