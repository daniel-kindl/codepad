using Codepad.Contracts;
using Codepad.Domain.Executions;

namespace Codepad.Application.Execution;

/// <summary>
/// The report of one C# run. A run that ends before the worker answers (timeout, cancel or worker failure)
/// has empty captured text and no host facts.
/// </summary>
/// <param name="ExecutionId">The ID of the execution.</param>
/// <param name="SourceRevision">The revision of the source that ran. The diagnostic locations belong to this revision.</param>
/// <param name="Result">The result of the run.</param>
/// <param name="Stdout">The captured standard output of the script.</param>
/// <param name="Stderr">The captured standard error of the script.</param>
/// <param name="Host">The facts about the host that ran the script, or null when the worker did not answer.</param>
public sealed record RunReport(
    ExecutionId ExecutionId,
    long SourceRevision,
    ExecutionResult Result,
    CapturedText Stdout,
    CapturedText Stderr,
    WorkerHostInfo? Host);

/// <summary>
/// Compiles and runs one snapshot of C# source in a disposable worker process.
/// The process is a crash boundary. It is not a security sandbox: the script has the permissions of the
/// user, so the caller must trust the source. Only an explicit Run may call this interface.
/// </summary>
public interface ICSharpRunner
{
    /// <summary>
    /// Runs the snapshot. A failure of the run is an outcome in the report. It is not an exception.
    /// The returned task does not block the caller while the worker runs.
    /// </summary>
    /// <param name="request">The snapshot to run. Its policy must be for C#.</param>
    /// <param name="cancellationToken">Cancels the run. The worker process tree stops, and the outcome is <see cref="ExecutionOutcome.Canceled"/>.</param>
    /// <returns>The report. It always has an outcome.</returns>
    /// <exception cref="ArgumentException">The policy of the request is not for C#.</exception>
    Task<RunReport> RunAsync(ExecutionRequest request, CancellationToken cancellationToken = default);
}
