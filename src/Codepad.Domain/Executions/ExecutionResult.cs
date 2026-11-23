using Codepad.Domain.Results;

namespace Codepad.Domain.Executions;

/// <summary>The outcome, the diagnostics and the value of one completed execution. It is immutable.</summary>
public sealed class ExecutionResult
{
    private readonly Diagnostic[] _diagnostics;

    /// <summary>Makes a result and checks that its parts agree with the outcome.</summary>
    /// <param name="outcome">How the execution ended.</param>
    /// <param name="diagnostics">The diagnostics. The result keeps its own copy.</param>
    /// <param name="value">The value. Only a <see cref="ExecutionOutcome.Success"/> result can have one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="diagnostics"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="outcome"/> is not defined.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="diagnostics"/> contains null; a result that is not a success has a value;
    /// a compilation failure has no error diagnostic; or a success has an error diagnostic.
    /// </exception>
    public ExecutionResult(ExecutionOutcome outcome, IReadOnlyList<Diagnostic> diagnostics, ResultNode? value)
    {
        Guard.Defined(outcome);
        _diagnostics = Guard.CopyWithNoNulls(diagnostics);
        bool hasError = Array.Exists(_diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        if (value is not null && outcome != ExecutionOutcome.Success)
        {
            throw new ArgumentException($"A {outcome} result cannot have a value.", nameof(value));
        }

        if (outcome == ExecutionOutcome.CompilationFailed && !hasError)
        {
            throw new ArgumentException("A CompilationFailed result needs at least one error diagnostic.", nameof(diagnostics));
        }

        if (outcome == ExecutionOutcome.Success && hasError)
        {
            throw new ArgumentException("A Success result cannot have an error diagnostic.", nameof(diagnostics));
        }

        Outcome = outcome;
        Value = value;
    }

    /// <summary>Gets how the execution ended.</summary>
    public ExecutionOutcome Outcome { get; }

    /// <summary>Gets the diagnostics.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>Gets the value, or null. Only a success can have a value.</summary>
    public ResultNode? Value { get; }
}
