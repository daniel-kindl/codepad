using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;

namespace Codepad.Application.Experiments;

/// <summary>The rules for an experiment that the user or an agent writes.</summary>
public static class ExperimentRules
{
    /// <summary>Gets the longest title in characters.</summary>
    public static int MaxTitleLength { get; } = 100;

    /// <summary>Trims a title and checks it.</summary>
    /// <param name="title">The title.</param>
    /// <returns>The trimmed title.</returns>
    /// <exception cref="ArgumentException">The title is empty or longer than <see cref="MaxTitleLength"/>.</exception>
    public static string NormalizeTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        string trimmed = title.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxTitleLength)
        {
            throw new ArgumentException($"A title needs 1 to {MaxTitleLength} characters.", nameof(title));
        }

        return trimmed;
    }
}

/// <summary>The result of an operation of the catalog.</summary>
public enum CatalogStatus
{
    /// <summary>The operation worked.</summary>
    Updated,

    /// <summary>The experiment does not exist.</summary>
    NotFound,

    /// <summary>The experiment changed since the caller read it. Nothing was written.</summary>
    Conflict,

    /// <summary>The experiment is of another kind than the operation needs.</summary>
    WrongKind,
}

/// <summary>The result of an update.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Experiment">The experiment after the update, or the stored one on a conflict, or null when it does not exist.</param>
public sealed record CatalogUpdate(CatalogStatus Status, Experiment? Experiment);

/// <summary>
/// Creates, reads and updates experiments for the callers that are not the editor: the command line and agents. It uses the
/// same storage and the same revision rule as autosave. A write that loses to a newer revision is a conflict, never a lost
/// write. The catalog never runs code.
/// </summary>
/// <param name="repository">The experiment storage.</param>
/// <param name="clock">The time source.</param>
public sealed class ExperimentCatalog(IExperimentRepository repository, TimeProvider clock)
{
    /// <summary>Creates and stores an experiment.</summary>
    /// <param name="kind">The operation kind.</param>
    /// <param name="title">The title.</param>
    /// <param name="source">The source.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The stored experiment at revision 1.</returns>
    /// <exception cref="ArgumentException">The title is not valid.</exception>
    /// <exception cref="ExperimentStorageException">The storage failed.</exception>
    public async Task<Experiment> CreateAsync(OperationKind kind, string title, string source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        Experiment experiment = Experiment.Create(ExperimentId.New(), kind, ExperimentRules.NormalizeTitle(title), source, clock.GetUtcNow());
        await repository.SaveAsync(experiment, cancellationToken).ConfigureAwait(false);
        return experiment;
    }

    /// <summary>Reads an experiment.</summary>
    /// <param name="id">The experiment.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The experiment, or null.</returns>
    public Task<Experiment?> GetAsync(ExperimentId id, CancellationToken cancellationToken = default) => repository.GetAsync(id, cancellationToken);

    /// <summary>Changes the title or the source of an experiment.</summary>
    /// <param name="id">The experiment.</param>
    /// <param name="title">The new title, or null to keep it.</param>
    /// <param name="source">The new source, or null to keep it.</param>
    /// <param name="expectedRevision">The revision that the caller read, or null to update whatever is stored. A different stored revision is a conflict.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException">The title is not valid.</exception>
    /// <exception cref="ExperimentStorageException">The storage failed.</exception>
    public async Task<CatalogUpdate> UpdateAsync(ExperimentId id, string? title, string? source, long? expectedRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        string? normalized = title is null ? null : ExperimentRules.NormalizeTitle(title);
        Experiment? stored = await repository.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return new CatalogUpdate(CatalogStatus.NotFound, null);
        }

        if (expectedRevision is long expected && expected != stored.Revision)
        {
            return new CatalogUpdate(CatalogStatus.Conflict, stored);
        }

        Experiment changed = stored;
        if (normalized is not null)
        {
            changed = changed.WithTitle(normalized, clock.GetUtcNow());
        }

        if (source is not null)
        {
            changed = changed.WithSource(source, clock.GetUtcNow());
        }

        if (changed.Revision == stored.Revision)
        {
            return new CatalogUpdate(CatalogStatus.Updated, stored);
        }

        // The write is a compare and set on the revision. If the desktop app or another process wrote revision N+1 after our read, our write is stale.
        SaveResult saved = await repository.SaveAsync(changed, cancellationToken).ConfigureAwait(false);
        return saved == SaveResult.Committed
            ? new CatalogUpdate(CatalogStatus.Updated, changed)
            : new CatalogUpdate(CatalogStatus.Conflict, await repository.GetAsync(id, cancellationToken).ConfigureAwait(false) ?? stored);
    }
}
