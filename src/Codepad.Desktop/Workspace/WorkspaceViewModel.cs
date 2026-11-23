using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Codepad.Application.Experiments;
using Codepad.Application.Workspace;
using Codepad.Desktop.Editor;
using Codepad.Desktop.Json;
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
    private readonly HashSet<ExperimentId> discardArmed = [];
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
    public WorkspaceViewModel(IExperimentRepository experiments, SessionStore sessionStore, TimeProvider timeProvider, Func<CSharpEditorViewModel> createEditor, Func<JsonToolViewModel> createTool, Action<Action>? post = null, Action<string>? reportFailure = null)
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
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

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
            }
        }
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
        await tab.FlushAsync(cancellationToken).ConfigureAwait(true);
        if (tab.HasSaveProblem && discardArmed.Add(tab.Id))
        {
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
        await tab.DisposeAsync().ConfigureAwait(true);
        return true;
    }

    /// <summary>Saves every tab before the window closes.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>True when the window can close. False once when a tab could not save. The next request returns true.</returns>
    public async Task<bool> TryCloseAllAsync(CancellationToken cancellationToken = default)
    {
        bool blocked = false;
        foreach (ExperimentTabViewModel tab in Tabs.ToList())
        {
            await tab.FlushAsync(cancellationToken).ConfigureAwait(true);
            if (tab.HasSaveProblem && discardArmed.Add(tab.Id))
            {
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

    private ExperimentTabViewModel AddTab(AutosaveSession session)
    {
        Experiment experiment = session.Current;
        ExperimentTabViewModel tab = experiment.Kind == OperationKind.Json
            ? new JsonTabViewModel(session, createTool(), timeProvider, post)
            : new CSharpTabViewModel(session, createEditor(), timeProvider, post);
        tab.PropertyChanged += OnTabChanged;
        Tabs.Add(tab);
        Raise(nameof(HasTabs));
        return tab;
    }

    private void OnTabChanged(object? sender, PropertyChangedEventArgs e)
    {
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
        QueueSessionWrite(next);
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
