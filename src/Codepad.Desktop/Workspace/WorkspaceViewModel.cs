using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Codepad.Application.Experiments;
using Codepad.Application.Workspace;
using Codepad.Desktop.Editor;
using Codepad.Desktop.Json;
using Codepad.Desktop.Results;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;

namespace Codepad.Desktop.Workspace;

/// <summary>
/// The open tabs. It creates, opens, activates, moves and closes tabs, and it saves the tab order and the active
/// tab apart from the experiments. Closing a tab never deletes the experiment. A tab that cannot save its text
/// stays open the first time the user closes it, so the text is not lost by one click.
/// </summary>
public sealed class WorkspaceViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private const string NEW_CSHARP_TITLE = "New C# experiment";
    private const string NEW_JSON_TITLE = "New JSON experiment";

    private readonly IExperimentRepository experiments;
    private readonly SessionStore sessionStore;
    private readonly TimeProvider timeProvider;
    private readonly Func<CSharpEditorViewModel> createEditor;
    private readonly Func<JsonToolViewModel> createTool;
    private readonly Action<Action> post;
    private readonly Action<string> reportFailure;
    private readonly RunController? runs;
    private readonly Action<string> reportSaveFailure;
    private const int MAX_FLUSH_PASSES = 5;

    // A double click or a held key repeats a request within this time. Only a later request means "discard".
    private static readonly TimeSpan _warningTime = TimeSpan.FromSeconds(1);

    // A tab is armed for one buffer: the revision that failed to save. A new edit has a new revision, so the warning returns.
    private readonly Dictionary<ExperimentId, (long Revision, DateTimeOffset At)> discardArmed = [];
    private readonly HashSet<ExperimentId> closing = [];
    private bool sessionWritesSuspended;
    private TabSet tabs = TabSet.Empty;
    private ExperimentTabViewModel? activeTab;
    private string notice = string.Empty;
    private Task sessionSaved = Task.CompletedTask;

    /// <summary>Initializes a new instance of the <see cref="WorkspaceViewModel"/> class.</summary>
    /// <param name="experiments">The experiment storage.</param>
    /// <param name="sessionStore">Stores the tab order and the active tab.</param>
    /// <param name="timeProvider">Supplies the edit time and the autosave timer.</param>
    /// <param name="createEditor">Creates the editor of a C# tab.</param>
    /// <param name="createTool">Creates the tool of a JSON tab.</param>
    /// <param name="post">Runs an action on the UI thread. Null uses the Avalonia UI thread.</param>
    /// <param name="reportFailure">Receives the exception type name when the session cannot be saved or read. Null ignores it.</param>
    /// <param name="runs">Runs C# tabs and owns the shared result and history panels. Null means that the app cannot run code.</param>
    /// <param name="reportSaveFailure">Receives the reason code once when an autosave fails, for example <c>disk_full</c>. It never holds source or a path. Null ignores it.</param>
    public WorkspaceViewModel(IExperimentRepository experiments, SessionStore sessionStore, TimeProvider timeProvider, Func<CSharpEditorViewModel> createEditor, Func<JsonToolViewModel> createTool, Action<Action>? post = null, Action<string>? reportFailure = null, RunController? runs = null, Action<string>? reportSaveFailure = null)
    {
        ArgumentNullException.ThrowIfNull(experiments);
        ArgumentNullException.ThrowIfNull(sessionStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(createEditor);
        ArgumentNullException.ThrowIfNull(createTool);
        this.experiments = experiments;
        this.sessionStore = sessionStore;
        this.timeProvider = timeProvider;
        this.createEditor = createEditor;
        this.createTool = createTool;
        this.post = post ?? (action => Dispatcher.UIThread.Post(action));
        this.reportFailure = reportFailure ?? (_ => { });
        this.runs = runs;
        this.reportSaveFailure = reportSaveFailure ?? (_ => { });
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised on the UI thread when an experiment has just been written to storage. The library listens to it.</summary>
    public event Action? ExperimentSaved;

    /// <summary>Gets the open tabs in tab order.</summary>
    public ObservableCollection<ExperimentTabViewModel> Tabs { get; } = [];

    /// <summary>Gets the active tab, or null when no tab is open.</summary>
    public ExperimentTabViewModel? ActiveTab
    {
        get => activeTab;
        private set
        {
            if (!ReferenceEquals(activeTab, value))
            {
                activeTab = value;
                foreach (ExperimentTabViewModel tab in Tabs)
                {
                    tab.IsActive = ReferenceEquals(tab, value);
                }

                Raise();
                Raise(nameof(ActiveSaveText));
                Raise(nameof(ActiveResults));
                runs?.OnActivated(value);
            }
        }
    }

    /// <summary>Gets the run controller, or null when the app cannot run code.</summary>
    public RunController? Runs => runs;

    /// <summary>Gets the result panel of the active tab, or null when no tab is open. The Copy command uses it.</summary>
    public ResultsViewModel? ActiveResults => ActiveTab switch
    {
        CSharpTabViewModel csharp => csharp.Results,
        JsonTabViewModel json => json.Tool.Results,
        _ => null,
    };

    /// <summary>Shows a notice to the user.</summary>
    /// <param name="text">The sentence.</param>
    public void ShowNotice(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Notice = text;
    }

    /// <summary>Gets a value indicating whether at least one tab is open.</summary>
    public bool HasTabs => Tabs.Count > 0;

    /// <summary>Gets the save text of the active tab, or an empty string.</summary>
    public string ActiveSaveText => ActiveTab?.SaveText ?? string.Empty;

    /// <summary>Gets a notice for the user, for example when a tab did not close. It is empty when there is nothing to say.</summary>
    public string Notice
    {
        get => notice;
        private set
        {
            if (notice != value)
            {
                notice = value;
                Raise();
            }
        }
    }

    /// <summary>Gets a task that ends when the last queued write of the session ends. Tests wait for it.</summary>
    public Task SessionSaved => sessionSaved;

    /// <summary>
    /// Opens the tabs of the last session. An experiment that no longer exists is omitted. Without a valid session
    /// it opens one new C# tab.
    /// </summary>
    /// <param name="cancellationToken">Cancels the restore.</param>
    /// <returns>A task that ends when the tabs are open.</returns>
    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<ExperimentId, Experiment> stored = [];
        try
        {
            foreach (Experiment experiment in await experiments.ListAsync(cancellationToken).ConfigureAwait(true))
            {
                stored[experiment.Id] = experiment;
            }
        }
        catch (ExperimentStorageException exception)
        {
            reportFailure(exception.Reason);
            Notice = "CodePad couldn't read your saved experiments.";

            // The stored session is still good. A write now would replace it with the one tab that the app opens next.
            sessionWritesSuspended = true;
        }

        TabSet restored;
        try
        {
            restored = await sessionStore.LoadAsync(stored.ContainsKey, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is ExperimentStorageException or InvalidOperationException)
        {
            reportFailure(exception.GetType().Name);
            restored = TabSet.Empty;
        }

        foreach (ExperimentId id in restored.Open)
        {
            AddTab(AutosaveSession.Open(experiments, stored[id], timeProvider, AutosaveSession.DefaultDebounce));
        }

        tabs = restored;
        ActiveTab = restored.Active is ExperimentId active ? Find(active) : null;
        Raise(nameof(HasTabs));
        if (Tabs.Count == 0)
        {
            await NewExperimentAsync(OperationKind.CSharp, cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>Creates an experiment, saves it, opens it in a new tab and activates the tab.</summary>
    /// <param name="kind">The operation kind.</param>
    /// <param name="cancellationToken">Cancels the wait for the first save.</param>
    /// <returns>The new tab.</returns>
    public async Task<ExperimentTabViewModel> NewExperimentAsync(OperationKind kind, CancellationToken cancellationToken = default)
    {
        Experiment created = Experiment.Create(ExperimentId.New(), kind, NextTitle(kind), string.Empty, timeProvider.GetUtcNow());
        ExperimentTabViewModel tab = AddTab(AutosaveSession.StartNew(experiments, created, timeProvider, AutosaveSession.DefaultDebounce));
        Apply(tabs.OpenTab(tab.Id));

        // The session lists only experiments that storage holds. A failed first save shows on the tab.
        await tab.FlushAsync(cancellationToken).ConfigureAwait(true);
        return tab;
    }

    /// <summary>Opens a stored experiment in a tab. An open experiment only gets the focus.</summary>
    /// <param name="id">The experiment.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The tab, or null when the experiment no longer exists.</returns>
    public async Task<ExperimentTabViewModel?> OpenAsync(ExperimentId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (Find(id) is ExperimentTabViewModel existing)
        {
            Activate(existing);
            return existing;
        }

        Experiment? stored;
        try
        {
            stored = await experiments.GetAsync(id, cancellationToken).ConfigureAwait(true);
        }
        catch (ExperimentStorageException exception)
        {
            reportFailure(exception.Reason);
            Notice = "CodePad couldn't open that experiment.";
            return null;
        }

        if (stored is null)
        {
            Notice = "That experiment no longer exists.";
            return null;
        }

        // Another open call can finish first while this one waits for storage. One experiment keeps one tab.
        if (Find(id) is ExperimentTabViewModel raced)
        {
            Activate(raced);
            return raced;
        }

        ExperimentTabViewModel tab = AddTab(AutosaveSession.Open(experiments, stored, timeProvider, AutosaveSession.DefaultDebounce));
        Apply(tabs.OpenTab(tab.Id));
        return tab;
    }

    /// <summary>Activates a tab.</summary>
    /// <param name="tab">The tab.</param>
    public void Activate(ExperimentTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (!ReferenceEquals(tab, ActiveTab))
        {
            Apply(tabs.Activate(tab.Id));
        }
    }

    /// <summary>Activates the next tab. After the last tab it goes to the first.</summary>
    public void Next() => Apply(tabs.Next());

    /// <summary>Activates the previous tab. Before the first tab it goes to the last.</summary>
    public void Previous() => Apply(tabs.Previous());

    /// <summary>Moves a tab to a position.</summary>
    /// <param name="tab">The tab.</param>
    /// <param name="index">The new position, from 0. A position outside the list stops at the nearest end.</param>
    public void Move(ExperimentTabViewModel tab, int index)
    {
        ArgumentNullException.ThrowIfNull(tab);
        Apply(tabs.Move(tab.Id, index));
    }

    /// <summary>Moves a tab by a number of positions.</summary>
    /// <param name="tab">The tab.</param>
    /// <param name="offset">The offset. A negative number moves the tab left.</param>
    public void MoveBy(ExperimentTabViewModel tab, int offset)
    {
        ArgumentNullException.ThrowIfNull(tab);
        Move(tab, Tabs.IndexOf(tab) + offset);
    }

    /// <summary>
    /// Saves the tab and closes it. The experiment stays in storage. When the save fails, the first request keeps
    /// the tab open and says so. A second request closes the tab and discards the unsaved text.
    /// </summary>
    /// <param name="tab">The tab.</param>
    /// <param name="cancellationToken">Cancels the wait for the save.</param>
    /// <returns>True when the tab closed.</returns>
    public async Task<bool> CloseAsync(ExperimentTabViewModel tab, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tab);

        // A double click or a held key sends the request twice. The second one must not count as "close again".
        if (!closing.Add(tab.Id))
        {
            return false;
        }

        try
        {
            await FlushSettledAsync(tab, cancellationToken).ConfigureAwait(true);

            // No await from here to the removal: no edit can come between the check and the close.
            if (tab.HasSaveProblem && !IsReadyToDiscard(tab))
            {
                Warn(tab);
                Notice = $"\"{tab.Title}\" is not saved. Close it again to discard the unsaved text.";
                return false;
            }

            if (!Tabs.Contains(tab))
            {
                return true;
            }

            discardArmed.Remove(tab.Id);
            Notice = string.Empty;
            Apply(tabs.Close(tab.Id), removed: tab);
        }
        finally
        {
            closing.Remove(tab.Id);
        }

        await tab.DisposeAsync().ConfigureAwait(true);
        return true;
    }

    /// <summary>Saves every tab before the window closes.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>True when the window can close. False once when a tab could not save. The next request returns true.</returns>
    public async Task<bool> TryCloseAllAsync(CancellationToken cancellationToken = default)
    {
        // An edit can arrive while a later tab saves, so the pass repeats until no tab has unsaved edits.
        for (int pass = 0; pass < MAX_FLUSH_PASSES; pass++)
        {
            foreach (ExperimentTabViewModel tab in Tabs.ToList())
            {
                await tab.FlushAsync(cancellationToken).ConfigureAwait(true);
            }

            if (!Tabs.Any(tab => tab.IsUnsaved))
            {
                break;
            }
        }

        // No await from here to the return: the answer describes the tabs as they are now.
        bool blocked = false;
        foreach (ExperimentTabViewModel tab in Tabs)
        {
            if (tab.HasSaveProblem && !IsReadyToDiscard(tab))
            {
                Warn(tab);
                Notice = $"\"{tab.Title}\" is not saved. Close CodePad again to discard the unsaved text.";
                blocked = true;
            }
        }

        if (!blocked)
        {
            await sessionSaved.ConfigureAwait(true);
        }

        return !blocked;
    }

    /// <summary>Stops all tabs. It does not save: call <see cref="TryCloseAllAsync"/> first.</summary>
    /// <returns>A task that ends when all tabs stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        foreach (ExperimentTabViewModel tab in Tabs.ToList())
        {
            await tab.DisposeAsync().ConfigureAwait(true);
        }
    }

    // The warning belongs to one buffer (its revision) and needs a moment to be read. A new edit starts it again.
    private bool IsReadyToDiscard(ExperimentTabViewModel tab) =>
        discardArmed.TryGetValue(tab.Id, out (long Revision, DateTimeOffset At) armed)
        && armed.Revision == tab.Revision
        && timeProvider.GetUtcNow() - armed.At >= _warningTime;

    private void Warn(ExperimentTabViewModel tab)
    {
        // A repeated request for the same buffer keeps the first time, so the wait does not restart.
        if (!discardArmed.TryGetValue(tab.Id, out (long Revision, DateTimeOffset At) armed) || armed.Revision != tab.Revision)
        {
            discardArmed[tab.Id] = (tab.Revision, timeProvider.GetUtcNow());
        }
    }

    // Saves until the tab has no pending edit. A failed save ends the loop: the state is then Error.
    private static async Task FlushSettledAsync(ExperimentTabViewModel tab, CancellationToken cancellationToken)
    {
        for (int pass = 0; pass < MAX_FLUSH_PASSES; pass++)
        {
            await tab.FlushAsync(cancellationToken).ConfigureAwait(true);
            if (!tab.IsUnsaved)
            {
                return;
            }
        }
    }

    private ExperimentTabViewModel AddTab(AutosaveSession session)
    {
        Experiment experiment = session.Current;
        ExperimentTabViewModel tab = experiment.Kind == OperationKind.Json
            ? new JsonTabViewModel(session, createTool(), timeProvider, post)
            : new CSharpTabViewModel(session, createEditor(), timeProvider, post, runs?.Results, runs?.History);
        tab.PropertyChanged += OnTabChanged;
        Tabs.Add(tab);
        Raise(nameof(HasTabs));
        return tab;
    }

    private void OnTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExperimentTabViewModel.State) && sender is ExperimentTabViewModel changed)
        {
            if (changed.State == SaveState.Error && !changed.FailureReported)
            {
                // One failure is logged once, not at each state change of the same failure.
                changed.FailureReported = true;
                reportSaveFailure(changed.SaveErrorReason ?? "unknown");
            }
            else if (changed.State != SaveState.Error)
            {
                changed.FailureReported = false;
            }

            if (changed.State == SaveState.Saved)
            {
                ExperimentSaved?.Invoke();
            }
        }

        if (ReferenceEquals(sender, ActiveTab) && e.PropertyName == nameof(ExperimentTabViewModel.SaveText))
        {
            Raise(nameof(ActiveSaveText));
        }
    }

    // One place applies a new tab set: it fixes the order of the list, the active tab and the stored session.
    private void Apply(TabSet next, ExperimentTabViewModel? removed = null)
    {
        tabs = next;
        if (removed is not null)
        {
            removed.PropertyChanged -= OnTabChanged;
            Tabs.Remove(removed);
        }

        for (int index = 0; index < next.Open.Count; index++)
        {
            ExperimentTabViewModel tab = Find(next.Open[index])!;
            int current = Tabs.IndexOf(tab);
            if (current != index)
            {
                Tabs.Move(current, index);
            }
        }

        ActiveTab = next.Active is ExperimentId active ? Find(active) : null;
        Raise(nameof(HasTabs));
        if (!sessionWritesSuspended)
        {
            QueueSessionWrite(next);
        }
    }

    private void QueueSessionWrite(TabSet snapshot)
    {
        Task previous = sessionSaved;
        sessionSaved = WriteAfterAsync(previous, snapshot);
    }

    private async Task WriteAfterAsync(Task previous, TabSet snapshot)
    {
        await previous.ConfigureAwait(false);
        try
        {
            await sessionStore.SaveAsync(snapshot).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The tabs still work. The next change tries to save the session again. The type name is safe to log.
            reportFailure(exception is ExperimentStorageException storage ? storage.Reason : exception.GetType().Name);
        }
    }

    private ExperimentTabViewModel? Find(ExperimentId id) => Tabs.FirstOrDefault(tab => tab.Id == id);

    private string NextTitle(OperationKind kind)
    {
        string baseTitle = kind == OperationKind.Json ? NEW_JSON_TITLE : NEW_CSHARP_TITLE;
        HashSet<string> used = [.. Tabs.Select(tab => tab.Title)];
        if (!used.Contains(baseTitle))
        {
            return baseTitle;
        }

        for (int number = 2; ; number++)
        {
            string candidate = string.Create(CultureInfo.InvariantCulture, $"{baseTitle} {number}");
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
