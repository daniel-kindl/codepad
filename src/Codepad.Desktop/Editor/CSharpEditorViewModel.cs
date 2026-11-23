using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Codepad.Application.Editing;
using Codepad.Domain.Results;

namespace Codepad.Desktop.Editor;

/// <summary>
/// The state of the C# editor: the diagnostics of the current text and a one-line summary. It owns one
/// <see cref="DiagnosticsCoordinator"/> for the open document. <see cref="Load"/> replaces it, so a late analysis of an
/// old document cannot reach the list.
/// </summary>
public sealed class CSharpEditorViewModel : INotifyPropertyChanged, IDisposable
{
    /// <summary>The summary while an analysis is pending.</summary>
    internal const string CHECKING_TEXT = "Checking…";

    /// <summary>The summary when the text has no diagnostic.</summary>
    internal const string NO_PROBLEMS_TEXT = "No problems found";

    /// <summary>The summary when the analysis failed.</summary>
    internal const string ANALYSIS_UNAVAILABLE_TEXT = "CodePad can't check this code right now.";

    private readonly ICSharpAnalyzer analyzer;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan debounce;
    private readonly Action<Action> post;
    private readonly Action<string> reportFailure;
    private readonly ICSharpCompletionProvider? completion;
    private readonly Action<string> reportCompletionFailure;
    private long documentVersion;
    private DiagnosticsCoordinator coordinator;
    private string text = string.Empty;
    private string summaryText = NO_PROBLEMS_TEXT;
    private bool loading;
    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="CSharpEditorViewModel"/> class.</summary>
    /// <param name="highlighter">Classifies the source for syntax coloring.</param>
    /// <param name="analyzer">Finds the diagnostics.</param>
    /// <param name="timeProvider">Supplies the idle timer.</param>
    /// <param name="debounce">The idle time before an analysis starts.</param>
    /// <param name="post">Runs an action on the UI thread. Null uses the Avalonia UI thread.</param>
    /// <param name="reportFailure">Receives the exception type name when the analysis fails. Null ignores it.</param>
    /// <param name="completion">Suggests code. Null turns the suggestions off.</param>
    /// <param name="reportCompletionFailure">Receives the exception type name when a completion request fails. Null ignores it.</param>
    public CSharpEditorViewModel(ICSharpHighlighter highlighter, ICSharpAnalyzer analyzer, TimeProvider timeProvider, TimeSpan debounce, Action<Action>? post = null, Action<string>? reportFailure = null, ICSharpCompletionProvider? completion = null, Action<string>? reportCompletionFailure = null)
    {
        ArgumentNullException.ThrowIfNull(highlighter);
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(timeProvider);
        Highlighter = highlighter;
        this.analyzer = analyzer;
        this.timeProvider = timeProvider;
        this.debounce = debounce;
        this.post = post ?? (action => Dispatcher.UIThread.Post(action));
        this.reportFailure = reportFailure ?? (_ => { });
        this.completion = completion;
        this.reportCompletionFailure = reportCompletionFailure ?? (_ => { });
        coordinator = CreateCoordinator();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the user edits the text. The argument is the new text. Autosave listens to it. A <see cref="Load"/> does not raise it.</summary>
    public event Action<string>? ContentChanged;

    /// <summary>Raised when <see cref="Load"/> replaces the document. The view shows the new text.</summary>
    public event Action<string>? DocumentLoaded;

    /// <summary>Gets the syntax highlighter that the view uses.</summary>
    public ICSharpHighlighter Highlighter { get; }

    /// <summary>Gets a value indicating whether the editor can suggest code.</summary>
    public bool CanComplete => completion is not null;

    /// <summary>Gets the version of the document. It grows with each edit and each <see cref="Load"/>. A completion belongs to one version.</summary>
    public long DocumentVersion => Volatile.Read(ref documentVersion);

    /// <summary>Gets the diagnostics of the current text. The list is empty while an analysis is pending.</summary>
    public ObservableCollection<DiagnosticItem> Diagnostics { get; } = [];

    /// <summary>Gets the current text.</summary>
    public string Text => text;

    /// <summary>Gets a one-line summary of the diagnostics.</summary>
    public string SummaryText
    {
        get => summaryText;
        private set => Set(ref summaryText, value);
    }

    /// <summary>Opens another document. It drops the diagnostics of the old one and cancels its analysis.</summary>
    /// <param name="source">The text of the new document.</param>
    public void Load(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(disposed, this);
        coordinator.Dispose();
        coordinator = CreateCoordinator();
        text = source;
        Interlocked.Increment(ref documentVersion);
        Diagnostics.Clear();
        loading = true;
        try
        {
            DocumentLoaded?.Invoke(source);
        }
        finally
        {
            loading = false;
        }

        BeginAnalysis();
    }

    /// <summary>Takes an edit from the view. The old diagnostics describe an old text, so the list empties until the new analysis ends.</summary>
    /// <param name="newText">The text after the edit.</param>
    public void OnTextEdited(string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);
        if (disposed || loading)
        {
            return;
        }

        text = newText;
        Interlocked.Increment(ref documentVersion);
        Diagnostics.Clear();
        BeginAnalysis();
        ContentChanged?.Invoke(newText);
    }

    /// <summary>
    /// Asks for suggestions at a caret position in the current text. The work runs off the UI thread on an immutable
    /// copy of the text. Call it from the UI thread. The result is null when the text or the document changed while
    /// the request ran, when the request was cancelled, or when it failed. A stale result never reaches the caller.
    /// </summary>
    /// <param name="caret">The zero-based caret offset in the current text.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The suggestions for the version that was current at the call, or null.</returns>
    public async Task<CompletionResult?> GetCompletionAsync(int caret, CancellationToken cancellationToken)
    {
        if (completion is null || disposed)
        {
            return null;
        }

        long requested = DocumentVersion;
        string snapshot = text;
        try
        {
            CompletionResult result = await Task.Run(() => completion.GetCompletionsAsync(snapshot, caret, cancellationToken), cancellationToken);

            // The continuation runs on the UI thread, so no edit can come between this check and the caller.
            return disposed || requested != DocumentVersion || cancellationToken.IsCancellationRequested ? null : result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            reportCompletionFailure(exception.GetType().Name);
            return null;
        }
    }

    /// <summary>Stops the analysis.</summary>
    public void Dispose()
    {
        disposed = true;
        coordinator.Dispose();
    }

    private DiagnosticsCoordinator CreateCoordinator()
    {
        DiagnosticsCoordinator created = new DiagnosticsCoordinator(analyzer, timeProvider, debounce);
        created.Published += update => post(() => Apply(created, update));
        created.Failed += reason => post(() => Fail(created, reason));
        return created;
    }

    private void BeginAnalysis()
    {
        SummaryText = CHECKING_TEXT;
        coordinator.Update(text);
    }

    private void Apply(DiagnosticsCoordinator source, AnalysisUpdate update)
    {
        // The check runs on the UI thread, so no edit can come between the check and the change.
        if (disposed || !ReferenceEquals(source, coordinator) || !source.IsCurrent(update.Version))
        {
            return;
        }

        Diagnostics.Clear();
        foreach (EditorDiagnostic diagnostic in update.Analysis.Diagnostics)
        {
            Diagnostics.Add(new DiagnosticItem(diagnostic));
        }

        SummaryText = Summarize(update.Analysis.Diagnostics);
    }

    private void Fail(DiagnosticsCoordinator source, string reason)
    {
        if (disposed || !ReferenceEquals(source, coordinator))
        {
            return;
        }

        SummaryText = ANALYSIS_UNAVAILABLE_TEXT;
        reportFailure(reason);
    }

    private static string Summarize(IReadOnlyList<EditorDiagnostic> diagnostics)
    {
        int errors = diagnostics.Count(item => item.Severity == DiagnosticSeverity.Error);
        int warnings = diagnostics.Count(item => item.Severity == DiagnosticSeverity.Warning);
        if (diagnostics.Count == 0)
        {
            return NO_PROBLEMS_TEXT;
        }

        List<string> parts = [];
        if (errors > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{errors} {(errors == 1 ? "error" : "errors")}"));
        }

        if (warnings > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{warnings} {(warnings == 1 ? "warning" : "warnings")}"));
        }

        int others = diagnostics.Count - errors - warnings;
        if (others > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{others} {(others == 1 ? "message" : "messages")}"));
        }

        return string.Join(", ", parts);
    }

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field != value)
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
