using Codepad.Application.Execution;
using Codepad.Application.Experiments;
using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;

namespace Codepad.Application.History;

/// <summary>
/// Runs an experiment or a saved snapshot and stores the completed run. Each method is for an explicit user action:
/// nothing here starts a run by itself. The snapshot is taken before the run starts, so an edit while the run is
/// active does not change what history keeps.
/// </summary>
/// <param name="runner">Runs one snapshot.</param>
/// <param name="history">Stores the completed run.</param>
/// <param name="clock">The time source.</param>
public sealed class RunHistoryService(ICSharpRunner runner, IRunHistoryRepository history, TimeProvider clock)
{
    /// <summary>Takes a snapshot of the experiment, runs it and stores the record.</summary>
    /// <param name="experiment">The experiment at the revision to run. It must be a C# experiment.</param>
    /// <param name="cancellationToken">Cancels the run. The record then has the outcome Canceled.</param>
    /// <returns>The run. A storage failure is in the result, so the user still sees the outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="experiment"/> is null.</exception>
    /// <exception cref="ArgumentException">The experiment is not a C# experiment.</exception>
    public Task<CompletedRun> RunAsync(Experiment experiment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ExecutionRequest request = ExecutionRequest.Snapshot(ExecutionId.New(), experiment, OperationPolicy.CSharpDefault, clock.GetUtcNow());
        return RunSnapshotAsync(request, cancellationToken);
    }

    /// <summary>
    /// Runs a saved snapshot again. The new run has a new ID and its own record. The old record and the current
    /// source of the experiment do not change.
    /// </summary>
    /// <param name="record">The record to run again.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The new run.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="record"/> is null.</exception>
    public Task<CompletedRun> RerunAsync(RunRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return RunSnapshotAsync(record.Request.Rerun(ExecutionId.New(), clock.GetUtcNow()), cancellationToken);
    }

    private async Task<CompletedRun> RunSnapshotAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        RunReport report = await runner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        RunRecord record = new RunRecord(request, clock.GetUtcNow(), report);
        try
        {
            // The store must finish even when the user canceled the run, so it does not use the run token.
            await history.AddAsync(record, CancellationToken.None).ConfigureAwait(false);
            return new CompletedRun(record, null);
        }
        catch (ExperimentStorageException exception)
        {
            return new CompletedRun(record, exception.Reason);
        }
    }
}
