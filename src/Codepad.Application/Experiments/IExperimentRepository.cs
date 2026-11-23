using Codepad.Domain.Experiments;

namespace Codepad.Application.Experiments;

/// <summary>The result of a save that reached storage.</summary>
public enum SaveResult
{
    /// <summary>The write is durable. Storage now holds the experiment at the given revision.</summary>
    Committed,

    /// <summary>Storage already holds the same or a newer revision. The write changed nothing.</summary>
    Stale,
}

/// <summary>Stores the experiments that the user edits. Each write is one transaction.</summary>
public interface IExperimentRepository
{
    /// <summary>
    /// Creates the experiment, or replaces the stored one when the given revision is higher than the stored revision.
    /// An older or equal revision never replaces a newer one, also when the writes arrive late or in a different order.
    /// </summary>
    /// <param name="experiment">The experiment to store.</param>
    /// <param name="cancellationToken">Cancels the wait for the write.</param>
    /// <returns><see cref="SaveResult.Committed"/> after the commit, or <see cref="SaveResult.Stale"/> when storage is newer.</returns>
    /// <exception cref="ExperimentStorageException">Storage failed. No partial data remains.</exception>
    Task<SaveResult> SaveAsync(Experiment experiment, CancellationToken cancellationToken = default);

    /// <summary>Reads one experiment. Reading never runs the source.</summary>
    /// <param name="id">The identity of the experiment.</param>
    /// <param name="cancellationToken">Cancels the wait for the read.</param>
    /// <returns>The last committed experiment, or null if storage has none with this ID.</returns>
    /// <exception cref="ExperimentStorageException">Storage failed.</exception>
    Task<Experiment?> GetAsync(ExperimentId id, CancellationToken cancellationToken = default);

    /// <summary>Lists all experiments. The most recently modified experiment is first.</summary>
    /// <param name="cancellationToken">Cancels the wait for the read.</param>
    /// <returns>The last committed experiments.</returns>
    /// <exception cref="ExperimentStorageException">Storage failed.</exception>
    Task<IReadOnlyList<Experiment>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes the user preferences that CodePad keeps in the database.</summary>
public interface IPreferenceRepository
{
    /// <summary>Reads one preference.</summary>
    /// <param name="key">The name of the preference.</param>
    /// <param name="cancellationToken">Cancels the wait for the read.</param>
    /// <returns>The value, or null if the preference is not set.</returns>
    /// <exception cref="ExperimentStorageException">Storage failed.</exception>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Sets one preference. A second call with the same key replaces the value.</summary>
    /// <param name="key">The name of the preference.</param>
    /// <param name="value">The value.</param>
    /// <param name="cancellationToken">Cancels the wait for the write.</param>
    /// <returns>A task that completes after the commit.</returns>
    /// <exception cref="ExperimentStorageException">Storage failed. No partial data remains.</exception>
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
}

/// <summary>Storage failed. The <see cref="Reason"/> is safe to log. The message never holds source, input or a path.</summary>
public sealed class ExperimentStorageException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ExperimentStorageException"/> class.</summary>
    /// <param name="reason">A stable, log-safe reason code, for example <c>disk_full</c>.</param>
    /// <param name="innerException">The storage exception. Do not log its message.</param>
    public ExperimentStorageException(string reason, Exception? innerException = null)
        : base($"Storage failed: {reason}.", innerException)
    {
        Reason = reason;
    }

    /// <summary>Gets the stable, log-safe reason code.</summary>
    public string Reason { get; }
}
