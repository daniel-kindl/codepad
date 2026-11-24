using Codepad.Application.History;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;

namespace Codepad.Application.Experiments;

/// <summary>The result of a run of an experiment from the command line.</summary>
/// <param name="Status">Whether the experiment ran. <see cref="CatalogStatus.Updated"/> means that it ran.</param>
/// <param name="Experiment">The experiment that was run, or the stored one on a conflict, or null.</param>
/// <param name="Run">The completed run, or null when nothing ran.</param>
public sealed record ExperimentRunResult(CatalogStatus Status, Experiment? Experiment, CompletedRun? Run);

/// <summary>
/// Runs a C# experiment for a caller that asks for it: the command line or an agent. It stores the experiment first, so
/// the run has an experiment and a revision, and the run history shows in the desktop app. It never runs a JSON experiment.
/// </summary>
/// <param name="catalog">Stores and reads experiments.</param>
/// <param name="runs">Runs a snapshot and stores the record.</param>
public sealed class ExperimentRunService(ExperimentCatalog catalog, RunHistoryService runs)
{
    /// <summary>Creates a C# experiment from a snippet and runs it.</summary>
    /// <param name="title">The title.</param>
    /// <param name="source">The snippet.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The result.</returns>
    public async Task<ExperimentRunResult> RunNewAsync(string title, string source, CancellationToken cancellationToken = default)
    {
        Experiment experiment = await catalog.CreateAsync(OperationKind.CSharp, title, source, cancellationToken).ConfigureAwait(false);
        return new ExperimentRunResult(CatalogStatus.Updated, experiment, await runs.RunAsync(experiment, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Runs a stored C# experiment, after it stores a new source when the caller gives one.</summary>
    /// <param name="id">The experiment.</param>
    /// <param name="source">The new source, or null to run the stored source.</param>
    /// <param name="expectedRevision">The revision that the caller read, or null.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The result.</returns>
    public async Task<ExperimentRunResult> RunExistingAsync(ExperimentId id, string? source, long? expectedRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        Experiment? stored = await catalog.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return new ExperimentRunResult(CatalogStatus.NotFound, null, null);
        }

        if (stored.Kind != OperationKind.CSharp)
        {
            return new ExperimentRunResult(CatalogStatus.WrongKind, stored, null);
        }

        Experiment toRun = stored;
        if (source is not null || expectedRevision is not null)
        {
            CatalogUpdate update = await catalog.UpdateAsync(id, null, source, expectedRevision, cancellationToken).ConfigureAwait(false);
            if (update.Status != CatalogStatus.Updated)
            {
                return new ExperimentRunResult(update.Status, update.Experiment, null);
            }

            toRun = update.Experiment!;
        }

        return new ExperimentRunResult(CatalogStatus.Updated, toRun, await runs.RunAsync(toRun, cancellationToken).ConfigureAwait(false));
    }
}
