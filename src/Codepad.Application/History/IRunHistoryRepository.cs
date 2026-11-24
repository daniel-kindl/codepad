using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;

namespace Codepad.Application.History;

/// <summary>Stores the completed runs of the experiments. A stored record never changes.</summary>
public interface IRunHistoryRepository
{
    /// <summary>
    /// Stores one record and removes the oldest runs of the same experiment that go over the retention limit.
    /// The two steps are one transaction. Retention never removes an experiment.
    /// </summary>
    /// <param name="record">The record to store.</param>
    /// <param name="cancellationToken">Cancels the wait for the write.</param>
    /// <returns>A task that completes after the commit.</returns>
    /// <exception cref="Codepad.Application.Experiments.ExperimentStorageException">
    /// Storage failed, a record with this ID exists (<c>duplicate</c>), or the experiment is not stored. No partial data remains.
    /// </exception>
    Task AddAsync(RunRecord record, CancellationToken cancellationToken = default);

    /// <summary>Lists the runs of one experiment. The newest run is first.</summary>
    /// <param name="experimentId">The experiment.</param>
    /// <param name="cancellationToken">Cancels the wait for the read.</param>
    /// <returns>The summaries. A damaged row is not in the list.</returns>
    /// <exception cref="Codepad.Application.Experiments.ExperimentStorageException">Storage failed.</exception>
    Task<IReadOnlyList<RunSummary>> ListAsync(ExperimentId experimentId, CancellationToken cancellationToken = default);

    /// <summary>Reads one record. Reading never runs the source.</summary>
    /// <param name="id">The execution ID.</param>
    /// <param name="cancellationToken">Cancels the wait for the read.</param>
    /// <returns>The record, or null if history has none with this ID (for example, retention removed it).</returns>
    /// <exception cref="Codepad.Application.Experiments.ExperimentStorageException">Storage failed, or the row is damaged (<c>corrupt</c>).</exception>
    Task<RunRecord?> GetAsync(ExecutionId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all saved runs of all experiments. It never deletes an experiment, its source or its settings.
    /// </summary>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>The number of runs that it deleted.</returns>
    Task<int> ClearAsync(CancellationToken cancellationToken = default);
}
