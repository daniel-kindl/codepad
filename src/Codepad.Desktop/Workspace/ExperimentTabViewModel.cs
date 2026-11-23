using System.ComponentModel;
using System.Runtime.CompilerServices;
using Codepad.Application.Experiments;
using Codepad.Desktop.Shell;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;

namespace Codepad.Desktop.Workspace;

/// <summary>
/// One open experiment: its tab label, its save state and its editor. Each tab owns its autosave session, so a failed
/// save shows on this tab only and never marks the tab clean. The editor holds the text and the view holds the caret
/// and the scroll position, so both stay apart for each tab.
/// </summary>
public abstract class ExperimentTabViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly AutosaveSession session;
    private readonly TimeProvider timeProvider;
    private readonly Action<Action> post;
    private bool isActive;
    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="ExperimentTabViewModel"/> class.</summary>
    /// <param name="session">The autosave session of the experiment.</param>
    /// <param name="timeProvider">Supplies the edit time.</param>
    /// <param name="post">Runs an action on the UI thread.</param>
    protected ExperimentTabViewModel(AutosaveSession session, TimeProvider timeProvider, Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(post);
        this.session = session;
        this.timeProvider = timeProvider;
        this.post = post;
        session.StateChanged += OnStateChanged;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the experiment ID. It is the identity of the tab.</summary>
    public ExperimentId Id => session.Current.Id;

    /// <summary>Gets the operation kind of the experiment.</summary>
    public OperationKind Kind => session.Current.Kind;

    /// <summary>Gets the title of the experiment.</summary>
    public string Title => session.Current.Title;

    /// <summary>Gets the kind as a short word for the tab label.</summary>
    public string KindText => Kind == OperationKind.Json ? "JSON" : "C#";

    /// <summary>Gets the current source, including edits that autosave has not written yet.</summary>
    public string Source => session.Current.Source;

    /// <summary>Gets the full state of the autosave session.</summary>
    public SaveState State => session.State;

    /// <summary>Gets or sets a value indicating whether this tab is the active tab.</summary>
    public bool IsActive
    {
        get => isActive;
        set
        {
            if (isActive != value)
            {
                isActive = value;
                Raise();
            }
        }
    }

    /// <summary>Gets the text of the save state. It is empty when there is nothing to say.</summary>
    public string SaveText => SaveStatusText.For(session.State, session.ErrorReason);

    /// <summary>Gets a short word on the tab for an unsaved or failed save. The tab never shows the state by color alone.</summary>
    public string SaveBadge => session.State switch
    {
        SaveState.Dirty => "Unsaved",
        SaveState.Saving => "Saving",
        SaveState.Error => "Save failed",
        _ => string.Empty,
    };

    /// <summary>Gets the revision of the current buffer.</summary>
    public long Revision => session.Current.Revision;

    /// <summary>Gets a value indicating whether the buffer has edits that storage does not hold yet and that no failed save has stopped.</summary>
    public bool IsUnsaved => session.State is SaveState.Dirty or SaveState.Saving;

    /// <summary>Gets a value indicating whether the last save failed. The text is still in the editor.</summary>
    public bool HasSaveProblem => session.State == SaveState.Error;

    /// <summary>Gets the name that a screen reader reads for the tab.</summary>
    public string AccessibleName => SaveBadge.Length > 0 ? $"{Title}, {KindText}, {SaveBadge}" : $"{Title}, {KindText}";

    /// <summary>Gets the name that a screen reader reads for the close button of the tab.</summary>
    public string CloseName => $"Close {Title}";

    /// <summary>Saves the pending edits now. A failure sets the failed state. It does not throw for a storage error.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that ends when the save ends.</returns>
    public Task FlushAsync(CancellationToken cancellationToken = default) => session.FlushAsync(cancellationToken);

    /// <summary>Stops the autosave timer and the editor. It does not save: call <see cref="FlushAsync"/> first.</summary>
    /// <returns>A task that ends when the tab is stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        session.StateChanged -= OnStateChanged;
        DisposeEditor();
        await session.DisposeAsync().ConfigureAwait(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Takes the text of an edit from the editor.</summary>
    /// <param name="text">The text after the edit.</param>
    protected void Edited(string text)
    {
        session.Edit(session.Current.WithSource(text, timeProvider.GetUtcNow()));
        RaiseSaveState();
    }

    /// <summary>Stops the editor of the tab.</summary>
    protected abstract void DisposeEditor();

    private void OnStateChanged() => post(() =>
    {
        if (!disposed)
        {
            RaiseSaveState();
        }
    });

    private void RaiseSaveState()
    {
        Raise(nameof(State));
        Raise(nameof(SaveText));
        Raise(nameof(SaveBadge));
        Raise(nameof(HasSaveProblem));
        Raise(nameof(AccessibleName));
        Raise(nameof(Source));
    }

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
