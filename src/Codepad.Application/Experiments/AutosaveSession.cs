using Codepad.Domain.Experiments;

namespace Codepad.Application.Experiments;

/// <summary>
/// Saves one open experiment. It waits for a pause in the edits, then writes the newest revision once.
/// Writes never overlap. The session never says <see cref="SaveState.Saved"/> before the repository reports a commit,
/// and it never drops the edited buffer: after a failure, <see cref="Current"/> still holds the edits.
/// </summary>
/// <remarks>
/// <see cref="StateChanged"/> can run on a timer thread. The UI must move to its own thread.
/// A failed timer save does not retry by itself. The next edit, or <see cref="FlushAsync"/>, tries again.
/// </remarks>
public sealed class AutosaveSession : IAsyncDisposable
{
    private static readonly TimeSpan _defaultDebounce = TimeSpan.FromMilliseconds(500);

    private readonly IExperimentRepository repository;
    private readonly SemaphoreSlim writeGate = new SemaphoreSlim(1, 1);
    private readonly ITimer timer;
    private readonly TimeSpan debounce;
    private readonly object sync = new object();
    private Experiment current;
    private long committedRevision;
    private SaveState state;
    private string? errorReason;
    private bool disposed;

    private AutosaveSession(IExperimentRepository repository, Experiment experiment, long committedRevision, TimeProvider timeProvider, TimeSpan debounce)
    {
        this.repository = repository;
        this.committedRevision = committedRevision;
        this.debounce = debounce;
        current = experiment;
        state = committedRevision >= experiment.Revision ? SaveState.Clean : SaveState.Dirty;
        timer = timeProvider.CreateTimer(OnTimer, null, state == SaveState.Dirty ? debounce : Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Gets the idle time before a save starts. The first target is 500 ms. Tune it with tests.</summary>
    public static TimeSpan DefaultDebounce => _defaultDebounce;

    /// <summary>Raised after <see cref="State"/> changes.</summary>
    public event Action? StateChanged;

    /// <summary>Gets the experiment as the editor holds it. It can be newer than storage.</summary>
    public Experiment Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    /// <summary>Gets the save state.</summary>
    public SaveState State
    {
        get
        {
            lock (sync)
            {
                return state;
            }
        }
    }

    /// <summary>Gets the log-safe reason code of the last failure while <see cref="State"/> is <see cref="SaveState.Error"/>. Otherwise null.</summary>
    public string? ErrorReason
    {
        get
        {
            lock (sync)
            {
                return errorReason;
            }
        }
    }

    /// <summary>Starts a session for an experiment that storage already holds, at the given revision.</summary>
    /// <param name="repository">Where the session writes.</param>
    /// <param name="stored">The experiment as storage holds it.</param>
    /// <param name="timeProvider">Supplies the idle timer.</param>
    /// <param name="debounce">The idle time before a save starts.</param>
    /// <returns>A clean session.</returns>
    public static AutosaveSession Open(IExperimentRepository repository, Experiment stored, TimeProvider timeProvider, TimeSpan debounce)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(timeProvider);
        return new AutosaveSession(repository, stored, stored.Revision, timeProvider, debounce);
    }

    /// <summary>Starts a session for a new experiment that storage does not hold yet. The first save creates it.</summary>
    /// <param name="repository">Where the session writes.</param>
    /// <param name="created">The new experiment.</param>
    /// <param name="timeProvider">Supplies the idle timer.</param>
    /// <param name="debounce">The idle time before a save starts.</param>
    /// <returns>A dirty session.</returns>
    public static AutosaveSession StartNew(IExperimentRepository repository, Experiment created, TimeProvider timeProvider, TimeSpan debounce)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(created);
        ArgumentNullException.ThrowIfNull(timeProvider);
        return new AutosaveSession(repository, created, 0, timeProvider, debounce);
    }

    /// <summary>Takes a new revision from the editor and restarts the idle timer.</summary>
    /// <param name="edited">The edited experiment. It is the same experiment, at the same or a newer revision.</param>
    /// <exception cref="ArgumentException">The experiment has another ID, or an older revision than <see cref="Current"/>.</exception>
    public void Edit(Experiment edited)
    {
        ArgumentNullException.ThrowIfNull(edited);
        lock (sync)
        {
            if (edited.Id != current.Id)
            {
                throw new ArgumentException("The edit is for another experiment.", nameof(edited));
            }

            if (edited.Revision < current.Revision)
            {
                throw new ArgumentException("The edit has an older revision than the buffer.", nameof(edited));
            }

            if (edited.Revision == current.Revision)
            {
                return;
            }

            // After dispose the buffer still takes the edit, so no edit is lost silently. No write follows: the timer is gone.
            current = edited;
            state = SaveState.Dirty;
            errorReason = null;
            if (!disposed)
            {
                timer.Change(debounce, Timeout.InfiniteTimeSpan);
            }
        }

        RaiseStateChanged();
    }

    /// <summary>
    /// Saves now, until storage holds <see cref="Current"/>, or a write fails. Call it on a tab switch, on shutdown and for a retry.
    /// A failure does not throw. It sets <see cref="SaveState.Error"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait. The state returns to <see cref="SaveState.Dirty"/>.</param>
    /// <returns>A task that completes when the save ends.</returns>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        try
        {
            await SaveAsync(untilClean: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // A cancelled flush ends before storage holds the edits. Start the idle timer again, so the edits still reach storage.
            RearmTimerIfDirty();
        }
    }

    /// <summary>Stops the idle timer. It does not write. Call <see cref="FlushAsync"/> first when the edits must reach storage.</summary>
    /// <returns>A completed task.</returns>
    public async ValueTask DisposeAsync()
    {
        lock (sync)
        {
            disposed = true;
        }

        await timer.DisposeAsync().ConfigureAwait(false);
    }

    private void RearmTimerIfDirty()
    {
        lock (sync)
        {
            if (!disposed && state == SaveState.Dirty)
            {
                timer.Change(debounce, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void OnTimer(object? ignored) => _ = SaveFromTimerAsync();

    private async Task SaveFromTimerAsync()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }
        }

        try
        {
            await SaveAsync(untilClean: false, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Nothing cancels a timer save.
        }
    }

    private async Task SaveAsync(bool untilClean, CancellationToken cancellationToken)
    {
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                Experiment snapshot;
                lock (sync)
                {
                    snapshot = current;
                    if (snapshot.Revision <= committedRevision)
                    {
                        return;
                    }

                    state = SaveState.Saving;
                }

                RaiseStateChanged();
                if (!await TryWriteAsync(snapshot, cancellationToken).ConfigureAwait(false) || !untilClean)
                {
                    return;
                }
            }
        }
        finally
        {
            writeGate.Release();
        }
    }

    // Returns true when the write committed. A failure sets the error state and returns false.
    private async Task<bool> TryWriteAsync(Experiment snapshot, CancellationToken cancellationToken)
    {
        string? failure = null;
        try
        {
            SaveResult result = await repository.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
            if (result == SaveResult.Stale)
            {
                failure = "stale_revision";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(SaveState.Dirty, null);
            throw;
        }
        catch (ExperimentStorageException exception)
        {
            failure = exception.Reason;
        }
        catch (Exception exception)
        {
            // A timer save has no caller to throw to. The type is the reason: the message can hold source or a path.
            failure = exception.GetType().Name;
        }

        if (failure is not null)
        {
            SetState(SaveState.Error, failure);
            return false;
        }

        lock (sync)
        {
            committedRevision = snapshot.Revision;
            state = current.Revision > committedRevision ? SaveState.Dirty : SaveState.Saved;
            errorReason = null;
        }

        RaiseStateChanged();
        return true;
    }

    private void SetState(SaveState newState, string? reason)
    {
        lock (sync)
        {
            state = newState;
            errorReason = reason;
        }

        RaiseStateChanged();
    }

    // The state is final before the event. A faulty subscriber must not stop a save or leave the state wrong, so its exception stops here.
    private void RaiseStateChanged()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }
        }

        try
        {
            StateChanged?.Invoke();
        }
        catch (Exception)
        {
            // Not rethrown on purpose. See the comment above.
        }
    }
}
