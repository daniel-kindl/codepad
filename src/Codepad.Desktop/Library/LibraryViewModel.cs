using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Codepad.Application.Experiments;
using Codepad.Application.Library;
using Codepad.Desktop.Workspace;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;

namespace Codepad.Desktop.Library;

/// <summary>
/// The library panel: a search box, a kind filter and a list of experiments, newest edit first. A search starts after a
/// pause in typing. A newer search cancels the older one, and the app drops a late result of an old search. A search
/// never changes an experiment. Opening a row opens or focuses the tab of that experiment. The list refreshes after
/// autosave writes an experiment, so a rename or an edit shows once it is stored.
/// </summary>
public sealed class LibraryViewModel : INotifyPropertyChanged, IDisposable
{
    /// <summary>The status when the library has no experiment.</summary>
    internal const string EMPTY_LIBRARY_TEXT = "No experiments yet. Use + C# or + JSON in the tab strip to start one.";

    /// <summary>The status when a search failed. The list keeps the earlier result.</summary>
    internal const string SEARCH_FAILED_TEXT = "CodePad can't search your experiments right now.";

    /// <summary>The status while a search runs.</summary>
    internal const string SEARCHING_TEXT = "Searching…";

    private const int MAX_QUERY_IN_STATUS = 40;

    private readonly ILibraryRepository repository;
    private readonly WorkspaceViewModel workspace;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan debounce;
    private readonly Action<Action> post;
    private readonly Action<string> reportFailure;
    private readonly int pageSize;
    private CancellationTokenSource? running;
    private long version;
    private string query = string.Empty;
    private LibraryFilter kindFilter;
    private string statusText = SEARCHING_TEXT;
    private bool hasMore;
    private bool isEmpty;
    private bool refreshing;
    private LibraryItem? selectedItem;
    private ExperimentId? selectedId;
    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="LibraryViewModel"/> class.</summary>
    /// <param name="repository">Searches the stored experiments.</param>
    /// <param name="workspace">Opens the tabs.</param>
    /// <param name="timeProvider">Supplies the idle timer.</param>
    /// <param name="debounce">The idle time before a search starts.</param>
    /// <param name="post">Runs an action on the UI thread. Null uses the Avalonia UI thread.</param>
    /// <param name="reportFailure">Receives the storage reason when a search fails. It never holds the query. Null ignores it.</param>
    /// <param name="pageSize">The number of rows in one page. Null uses <see cref="LibraryQuery.DefaultLimit"/>.</param>
    public LibraryViewModel(ILibraryRepository repository, WorkspaceViewModel workspace, TimeProvider timeProvider, TimeSpan debounce, Action<Action>? post = null, Action<string>? reportFailure = null, int? pageSize = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(debounce, TimeSpan.Zero);
        this.repository = repository;
        this.workspace = workspace;
        this.timeProvider = timeProvider;
        this.debounce = debounce;
        this.post = post ?? (action => Dispatcher.UIThread.Post(action));
        this.reportFailure = reportFailure ?? (_ => { });
        this.pageSize = pageSize ?? LibraryQuery.DefaultLimit;
        workspace.ExperimentSaved += OnExperimentSaved;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the rows of the current result.</summary>
    public ObservableCollection<LibraryItem> Items { get; } = [];

    /// <summary>
    /// Gets or sets the selected row. A new result keeps the selection when the same experiment is still in the list,
    /// so a refresh after an autosave does not move the user.
    /// </summary>
    public LibraryItem? SelectedItem
    {
        get => selectedItem;
        set
        {
            // The list control reports null while the rows change. That is not a choice of the user.
            if (refreshing)
            {
                return;
            }

            selectedItem = value;
            selectedId = value?.Id;
            Raise();
        }
    }

    /// <summary>Gets or sets the search text. A change starts a search after a pause.</summary>
    public string Query
    {
        get => query;
        set
        {
            if (query != value)
            {
                query = value ?? string.Empty;
                Raise();
                Schedule(debounce);
            }
        }
    }

    /// <summary>Gets or sets the kind filter. A change searches at once.</summary>
    public LibraryFilter KindFilter
    {
        get => kindFilter;
        set
        {
            if (kindFilter != value)
            {
                kindFilter = value;
                Raise();
                Raise(nameof(IsAllFilter));
                Raise(nameof(IsCSharpFilter));
                Raise(nameof(IsJsonFilter));
                Schedule(TimeSpan.Zero);
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether the filter shows all kinds. A radio button binds to it.</summary>
    public bool IsAllFilter
    {
        get => kindFilter == LibraryFilter.All;
        set => SetFilter(value, LibraryFilter.All);
    }

    /// <summary>Gets or sets a value indicating whether the filter shows C# experiments. A radio button binds to it.</summary>
    public bool IsCSharpFilter
    {
        get => kindFilter == LibraryFilter.CSharp;
        set => SetFilter(value, LibraryFilter.CSharp);
    }

    /// <summary>Gets or sets a value indicating whether the filter shows JSON experiments. A radio button binds to it.</summary>
    public bool IsJsonFilter
    {
        get => kindFilter == LibraryFilter.Json;
        set => SetFilter(value, LibraryFilter.Json);
    }

    /// <summary>Gets a sentence about the result: the count, the empty library or no match. It is a live region.</summary>
    public string StatusText
    {
        get => statusText;
        private set
        {
            if (statusText != value)
            {
                statusText = value;
                Raise();
            }
        }
    }

    /// <summary>Gets a value indicating whether more results follow the list.</summary>
    public bool HasMore
    {
        get => hasMore;
        private set
        {
            if (hasMore != value)
            {
                hasMore = value;
                Raise();
            }
        }
    }

    /// <summary>Gets a value indicating whether the library has no experiment at all.</summary>
    public bool IsEmpty
    {
        get => isEmpty;
        private set
        {
            if (isEmpty != value)
            {
                isEmpty = value;
                Raise();
            }
        }
    }

    /// <summary>Searches again now, with the current text and filter, and shows the first page. Callers that wait for the result use this.</summary>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>A task that ends when the result shows.</returns>
    public Task RefreshAsync(CancellationToken cancellationToken = default) => SearchAsync(0, append: false, cancellationToken);

    /// <summary>Adds the next page to the list.</summary>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>A task that ends when the page shows.</returns>
    public Task LoadMoreAsync(CancellationToken cancellationToken = default) => HasMore ? SearchAsync(Items.Count, append: true, cancellationToken) : Task.CompletedTask;

    /// <summary>Empties the search text and shows the recent list again.</summary>
    public void ClearQuery()
    {
        if (query.Length == 0)
        {
            return;
        }

        query = string.Empty;
        Raise(nameof(Query));
        Schedule(TimeSpan.Zero);
    }

    /// <summary>Opens the experiment of a row in a tab. An open experiment only gets the focus.</summary>
    /// <param name="item">The row.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that ends when the tab is open.</returns>
    public async Task OpenAsync(LibraryItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (await workspace.OpenAsync(item.Id, cancellationToken).ConfigureAwait(true) is null)
        {
            // The workspace said why. The row is out of date, so the list searches again.
            Schedule(TimeSpan.Zero);
        }
    }

    /// <summary>Stops the search and the refresh.</summary>
    public void Dispose()
    {
        disposed = true;
        workspace.ExperimentSaved -= OnExperimentSaved;
        CancellationTokenSource? current = Interlocked.Exchange(ref running, null);
        current?.Cancel();
        current?.Dispose();
    }

    private void SetFilter(bool selected, LibraryFilter filter)
    {
        // A radio button reports false for the old choice. Only the new choice changes the filter.
        if (selected)
        {
            KindFilter = filter;
        }
    }

    private void OnExperimentSaved() => Schedule(debounce);

    private void Schedule(TimeSpan delay)
    {
        if (disposed)
        {
            return;
        }

        CancellationTokenSource next = new CancellationTokenSource();
        CancellationTokenSource? previous = Interlocked.Exchange(ref running, next);
        previous?.Cancel();
        previous?.Dispose();
        _ = RunScheduledAsync(delay, next.Token);
    }

    private async Task RunScheduledAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, timeProvider, token).ConfigureAwait(false);
            }

            await SearchAsync(0, append: false, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A newer search or Dispose cancelled this one.
        }
    }

    private async Task SearchAsync(int offset, bool append, CancellationToken cancellationToken)
    {
        long mine = Interlocked.Increment(ref version);
        LibraryQuery request = new LibraryQuery(query, kindFilter switch
        {
            LibraryFilter.CSharp => OperationKind.CSharp,
            LibraryFilter.Json => OperationKind.Json,
            _ => null,
        }, pageSize, offset);

        LibraryPage page;
        try
        {
            page = await repository.SearchAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ExperimentStorageException exception)
        {
            post(() => ShowFailure(mine, exception.Reason));
            return;
        }

        post(() => Show(mine, request, page, append));
    }

    // The check runs on the UI thread, so no new search can start between the check and the change.
    private void Show(long mine, LibraryQuery request, LibraryPage page, bool append)
    {
        if (disposed || mine != Volatile.Read(ref version))
        {
            return;
        }

        refreshing = true;
        try
        {
            if (!append)
            {
                Items.Clear();
            }

            foreach (ExperimentSummary summary in page.Items)
            {
                Items.Add(LibraryItem.From(summary));
            }
        }
        finally
        {
            refreshing = false;
        }

        selectedItem = selectedId is ExperimentId keep ? Items.FirstOrDefault(item => item.Id == keep) : null;
        Raise(nameof(SelectedItem));

        HasMore = page.HasMore;
        bool filtered = request.Text.Length > 0 || request.Kind is not null;
        IsEmpty = Items.Count == 0 && !filtered;
        StatusText = Describe(request, filtered);
    }

    private void ShowFailure(long mine, string reason)
    {
        if (disposed || mine != Volatile.Read(ref version))
        {
            return;
        }

        StatusText = SEARCH_FAILED_TEXT;
        reportFailure(reason);
    }

    private string Describe(LibraryQuery request, bool filtered)
    {
        if (Items.Count == 0)
        {
            if (!filtered)
            {
                return EMPTY_LIBRARY_TEXT;
            }

            string shown = request.Text.Length > MAX_QUERY_IN_STATUS ? request.Text[..MAX_QUERY_IN_STATUS] + "…" : request.Text;
            return request.Text.Length > 0
                ? $"No experiment matches “{shown}”."
                : "No experiment of this kind yet.";
        }

        string noun = filtered ? (Items.Count == 1 ? "match" : "matches") : (Items.Count == 1 ? "experiment" : "experiments");
        string count = string.Create(CultureInfo.InvariantCulture, $"{Items.Count} {noun}");
        return HasMore ? count + ". More are available." : count;
    }

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
