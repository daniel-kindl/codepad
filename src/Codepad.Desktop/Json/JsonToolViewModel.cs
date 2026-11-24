using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Codepad.Application.Json;
using Codepad.Domain.Results;
using Codepad.Desktop.Results;
using Codepad.Desktop.Settings;

namespace Codepad.Desktop.Json;

/// <summary>
/// The state of the live JSON tool: the input as the user typed it, the formatted text, and the result panel.
/// It owns one <see cref="LiveJsonCoordinator"/> for the open document. <see cref="Load"/> replaces it, so a late
/// result of an old document cannot show. The tool never changes the input text.
/// </summary>
public sealed class JsonToolViewModel : INotifyPropertyChanged, IDisposable
{
    /// <summary>The summary while an evaluation is pending.</summary>
    internal const string CHECKING_TEXT = "Checking…";

    /// <summary>The summary for a valid document.</summary>
    internal const string VALID_TEXT = "Valid JSON";

    /// <summary>The summary for an empty input.</summary>
    internal const string EMPTY_TEXT = "Type or paste JSON to see it formatted.";

    /// <summary>The summary when the evaluation failed.</summary>
    internal const string UNAVAILABLE_TEXT = "CodePad can't check this JSON right now.";

    private const string LIVE_INFO_TEXT = "Live preview · updates while you type";
    private const string INVALID_STATUS = "Invalid JSON";
    private const string PREVIOUS_TEXT = "The input is not valid JSON. The panel shows the last valid version.";
    private const string NO_VALUE_TEXT = "The input is not valid JSON. See Problems.";

    private readonly IJsonEvaluator evaluator;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan debounce;
    private readonly Action<Action> post;
    private readonly Action<string> reportFailure;
    private LiveJsonCoordinator coordinator;
    private string text = string.Empty;
    private string formattedText = string.Empty;
    private string summaryText = EMPTY_TEXT;
    private bool hasProblem;
    private bool loading;
    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="JsonToolViewModel"/> class.</summary>
    /// <param name="evaluator">Checks and formats the text.</param>
    /// <param name="timeProvider">Supplies the idle timer.</param>
    /// <param name="debounce">The idle time before an evaluation starts.</param>
    /// <param name="post">Runs an action on the UI thread. Null uses the Avalonia UI thread.</param>
    /// <param name="reportFailure">Receives the exception type name when the evaluation fails. Null ignores it.</param>
    /// <param name="preferences">The font size. The view applies it at once. Null uses the defaults.</param>
    public JsonToolViewModel(IJsonEvaluator evaluator, TimeProvider timeProvider, TimeSpan debounce, Action<Action>? post = null, Action<string>? reportFailure = null, EditorPreferences? preferences = null)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.evaluator = evaluator;
        Preferences = preferences ?? new EditorPreferences();
        this.timeProvider = timeProvider;
        this.debounce = debounce;
        this.post = post ?? (action => Dispatcher.UIThread.Post(action));
        this.reportFailure = reportFailure ?? (_ => { });
        coordinator = CreateCoordinator();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the user edits the text. The argument is the new text. Autosave listens to it. A <see cref="Load"/> does not raise it.</summary>
    public event Action<string>? ContentChanged;

    /// <summary>Raised when <see cref="Load"/> replaces the document. The view shows the new text.</summary>
    public event Action<string>? DocumentLoaded;

    /// <summary>Gets the font size that the view applies.</summary>
    public EditorPreferences Preferences { get; }

    /// <summary>Gets the result panel. It shows the tree of the last valid document and the problems.</summary>
    public ResultsViewModel Results { get; } = new ResultsViewModel();

    /// <summary>Gets the input exactly as the user typed it.</summary>
    public string Text => text;

    /// <summary>Gets the indented text of the last valid document, or an empty string when there is none.</summary>
    public string FormattedText
    {
        get => formattedText;
        private set
        {
            Set(ref formattedText, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FormattedNote)));
        }
    }

    /// <summary>Gets a notice for the formatted text. It is not empty when the text is from an earlier valid version.</summary>
    public string FormattedNote => HasProblem && formattedText.Length > 0 ? "Not valid now. This is the last valid version." : string.Empty;

    /// <summary>Gets a one-line summary. For an invalid document it names the line and the column.</summary>
    public string SummaryText
    {
        get => summaryText;
        private set => Set(ref summaryText, value);
    }

    /// <summary>Gets a value indicating whether the current text is invalid JSON or over a limit.</summary>
    public bool HasProblem
    {
        get => hasProblem;
        private set
        {
            Set(ref hasProblem, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FormattedNote)));
        }
    }

    /// <summary>Opens another document. It drops the result of the old one and cancels its evaluation.</summary>
    /// <param name="source">The text of the new document.</param>
    public void Load(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(disposed, this);
        coordinator.Dispose();
        coordinator = CreateCoordinator();
        text = source;
        FormattedText = string.Empty;
        HasProblem = false;
        Results.Clear();
        loading = true;
        try
        {
            DocumentLoaded?.Invoke(source);
        }
        finally
        {
            loading = false;
        }

        BeginEvaluation();
    }

    /// <summary>Takes an edit from the view.</summary>
    /// <param name="newText">The text after the edit.</param>
    public void OnTextEdited(string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);
        // A text box can report the text that Load gave it as a change, after Load ended. That is not an edit.
        if (disposed || loading || newText == text)
        {
            return;
        }

        text = newText;
        BeginEvaluation();
        ContentChanged?.Invoke(newText);
    }

    /// <summary>Stops the evaluation.</summary>
    public void Dispose()
    {
        disposed = true;
        coordinator.Dispose();
    }

    private LiveJsonCoordinator CreateCoordinator()
    {
        LiveJsonCoordinator created = new LiveJsonCoordinator(evaluator, timeProvider, debounce);
        created.Published += update => post(() => Apply(created, update));
        created.Failed += reason => post(() => Fail(created, reason));
        return created;
    }

    private void BeginEvaluation()
    {
        SummaryText = CHECKING_TEXT;
        coordinator.Update(text);
    }

    private void Apply(LiveJsonCoordinator source, JsonUpdate update)
    {
        // The check runs on the UI thread, so no edit can come between the check and the change.
        if (disposed || !ReferenceEquals(source, coordinator) || !source.IsCurrent(update.Version))
        {
            return;
        }

        JsonEvaluation evaluation = update.Evaluation;
        if (evaluation.Problem is Diagnostic problem)
        {
            ShowProblem(problem);
            return;
        }

        FormattedText = evaluation.Formatted ?? string.Empty;
        HasProblem = false;
        SummaryText = VALID_TEXT;
        Results.ShowPreview(evaluation.Tree!, VALID_TEXT, LIVE_INFO_TEXT);
    }

    private void ShowProblem(Diagnostic problem)
    {
        if (problem.Code == "JSON_EMPTY")
        {
            FormattedText = string.Empty;
            HasProblem = false;
            SummaryText = EMPTY_TEXT;
            Results.ClearPreview("No JSON yet", EMPTY_TEXT);
            return;
        }

        HasProblem = true;
        SummaryText = problem.Location is SourceLocation location
            ? string.Create(CultureInfo.InvariantCulture, $"{INVALID_STATUS} at line {location.Line}, column {location.Column}: {problem.Message}")
            : string.Create(CultureInfo.InvariantCulture, $"{INVALID_STATUS}: {problem.Message}");
        Results.ShowPreviewProblem(problem, INVALID_STATUS, PREVIOUS_TEXT, NO_VALUE_TEXT);
    }

    private void Fail(LiveJsonCoordinator source, string reason)
    {
        if (disposed || !ReferenceEquals(source, coordinator))
        {
            return;
        }

        FormattedText = string.Empty;
        HasProblem = true;
        SummaryText = UNAVAILABLE_TEXT;
        Results.ClearPreview("Check failed", UNAVAILABLE_TEXT);
        reportFailure(reason);
    }

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field != value)
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    private void Set(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field != value)
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
