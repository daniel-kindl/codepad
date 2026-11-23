using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Codepad.Application.Execution;
using Codepad.Contracts;
using Codepad.Domain.Executions;
using Codepad.Domain.Results;

namespace Codepad.Desktop.Results;

/// <summary>One row of the diagnostics list of a run.</summary>
/// <param name="SeverityText">The severity as a word.</param>
/// <param name="PlaceText">The place and the code, or an empty string when the diagnostic has no place.</param>
/// <param name="Message">The message.</param>
public sealed record RunDiagnosticRow(string SeverityText, string PlaceText, string Message)
{
    /// <summary>Gets the text that a screen reader reads for the row.</summary>
    public string AccessibleName => PlaceText.Length > 0 ? $"{SeverityText}, {PlaceText}: {Message}" : $"{SeverityText}: {Message}";
}

/// <summary>
/// The state of the result panel: the status of the last run, its value, diagnostics and output. A run that did not
/// succeed never removes the value of the last successful run. The panel keeps showing that value, and says so.
/// </summary>
public sealed class ResultsViewModel : INotifyPropertyChanged
{
    /// <summary>The status text before the first run.</summary>
    internal const string NO_RUN_TEXT = "No run yet";

    /// <summary>The text of the result tab before the first run.</summary>
    internal const string EMPTY_RESULT_TEXT = "Run results appear here. Running code isn't available in this build yet.";

    private static readonly JsonSerializerOptions s_copyOptions = new JsonSerializerOptions { WriteIndented = true };

    private string statusText = NO_RUN_TEXT;
    private string statusKind = "idle";
    private string executionText = string.Empty;
    private string emptyText = EMPTY_RESULT_TEXT;
    private ResultPresentation presentation;
    private string valueText = string.Empty;
    private ResultTable? table;
    private IReadOnlyList<ResultTreeItem> tree = [];
    private string truncationText = string.Empty;
    private bool isShowingPrevious;
    private string stdout = string.Empty;
    private string stderr = string.Empty;
    private string stdoutNote = string.Empty;
    private string stderrNote = string.Empty;
    private ResultNode? shownValue;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the status of the last run as a short phrase.</summary>
    public string StatusText
    {
        get => statusText;
        private set => Set(ref statusText, value);
    }

    /// <summary>Gets the class of the status for styling: <c>idle</c>, <c>success</c>, <c>error</c> or <c>warning</c>. The phrase carries the meaning too, so color is never the only signal.</summary>
    public string StatusKind
    {
        get => statusKind;
        private set => Set(ref statusKind, value);
    }

    /// <summary>Gets the execution ID and the completion time, or an empty string before the first run.</summary>
    public string ExecutionText
    {
        get => executionText;
        private set => Set(ref executionText, value);
    }

    /// <summary>Gets the text that the result tab shows when there is no value.</summary>
    public string EmptyText
    {
        get => emptyText;
        private set => Set(ref emptyText, value);
    }

    /// <summary>Gets how the value is shown.</summary>
    public ResultPresentation Presentation
    {
        get => presentation;
        private set
        {
            if (Set(ref presentation, value))
            {
                foreach (string name in new[] { nameof(IsEmpty), nameof(IsText), nameof(IsTree), nameof(IsTable) })
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
                }
            }
        }
    }

    /// <summary>Gets a value indicating whether there is no value to show.</summary>
    public bool IsEmpty => presentation == ResultPresentation.None;

    /// <summary>Gets a value indicating whether the value shows as text.</summary>
    public bool IsText => presentation == ResultPresentation.Text;

    /// <summary>Gets a value indicating whether the value shows as a tree.</summary>
    public bool IsTree => presentation == ResultPresentation.Tree;

    /// <summary>Gets a value indicating whether the value shows as a table.</summary>
    public bool IsTable => presentation == ResultPresentation.Table;

    /// <summary>Gets a value indicating whether the Copy command has a value to copy.</summary>
    public bool CanCopy => shownValue is not null;

    /// <summary>Gets the text of a single value, for <see cref="ResultPresentation.Text"/>.</summary>
    public string ValueText
    {
        get => valueText;
        private set => Set(ref valueText, value);
    }

    /// <summary>Gets the table, for <see cref="ResultPresentation.Table"/>.</summary>
    public ResultTable? Table
    {
        get => table;
        private set => Set(ref table, value);
    }

    /// <summary>Gets the root rows of the tree, for <see cref="ResultPresentation.Tree"/>.</summary>
    public IReadOnlyList<ResultTreeItem> Tree
    {
        get => tree;
        private set => Set(ref tree, value);
    }

    /// <summary>Gets a sentence that tells that the value holds less than the full result, or an empty string.</summary>
    public string TruncationText
    {
        get => truncationText;
        private set => Set(ref truncationText, value);
    }

    /// <summary>Gets a value indicating whether the last run did not succeed and the panel shows the value of an earlier run.</summary>
    public bool IsShowingPrevious
    {
        get => isShowingPrevious;
        private set => Set(ref isShowingPrevious, value);
    }

    /// <summary>Gets the diagnostics of the last run.</summary>
    public ObservableCollection<RunDiagnosticRow> Diagnostics { get; } = [];

    /// <summary>Gets the standard output of the last run.</summary>
    public string Stdout
    {
        get => stdout;
        private set => Set(ref stdout, value);
    }

    /// <summary>Gets the standard error of the last run.</summary>
    public string Stderr
    {
        get => stderr;
        private set => Set(ref stderr, value);
    }

    /// <summary>Gets a sentence about a cut standard output, or an empty string.</summary>
    public string StdoutNote
    {
        get => stdoutNote;
        private set => Set(ref stdoutNote, value);
    }

    /// <summary>Gets a sentence about a cut standard error, or an empty string.</summary>
    public string StderrNote
    {
        get => stderrNote;
        private set => Set(ref stderrNote, value);
    }

    /// <summary>Gets the title of the diagnostics tab, with the count.</summary>
    public string DiagnosticsTitle => Diagnostics.Count == 0 ? "Problems" : string.Create(CultureInfo.InvariantCulture, $"Problems ({Diagnostics.Count})");

    /// <summary>Shows the report of a finished run.</summary>
    /// <param name="report">The report.</param>
    /// <param name="completedAt">When the run ended.</param>
    public void Show(RunReport report, DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(report);
        (StatusText, StatusKind) = Describe(report.Result.Outcome);
        ExecutionText = string.Create(CultureInfo.InvariantCulture, $"Run {report.ExecutionId} · finished {completedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");

        Diagnostics.Clear();
        foreach (Diagnostic diagnostic in report.Result.Diagnostics)
        {
            Diagnostics.Add(ToRow(diagnostic));
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiagnosticsTitle)));
        (Stdout, StdoutNote) = Output(report.Stdout, "Standard output");
        (Stderr, StderrNote) = Output(report.Stderr, "Standard error");

        if (report.Result.Outcome == ExecutionOutcome.Success)
        {
            ShowValue(report.Result.Value, previous: false);
        }
        else if (shownValue is not null)
        {
            // Keep the earlier value. The flag tells the user that it is not from this run.
            IsShowingPrevious = true;
        }
        else
        {
            ShowValue(null, previous: false);
            EmptyText = report.Result.Outcome == ExecutionOutcome.CompilationFailed
                ? "The code did not compile. See Problems."
                : "This run has no value. See Problems and Errors.";
        }
    }

    /// <summary>Clears everything, for example when another experiment opens.</summary>
    public void Clear()
    {
        StatusText = NO_RUN_TEXT;
        StatusKind = "idle";
        ExecutionText = string.Empty;
        Diagnostics.Clear();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiagnosticsTitle)));
        Stdout = string.Empty;
        Stderr = string.Empty;
        StdoutNote = string.Empty;
        StderrNote = string.Empty;
        ShowValue(null, previous: false);
        EmptyText = EMPTY_RESULT_TEXT;
    }

    /// <summary>Gets the bounded value as indented JSON, for the Copy command.</summary>
    /// <returns>The JSON, or null when there is no value.</returns>
    public string? GetCopyText() => shownValue is null ? null : JsonSerializer.Serialize(shownValue, s_copyOptions);

    private void ShowValue(ResultNode? value, bool previous)
    {
        shownValue = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanCopy)));
        IsShowingPrevious = previous;
        Table = null;
        Tree = [];
        ValueText = string.Empty;
        TruncationText = string.Empty;
        switch (value)
        {
            case null:
                Presentation = ResultPresentation.None;
                break;
            case NullNode:
                Presentation = ResultPresentation.Text;
                ValueText = "null";
                break;
            case ScalarNode scalar:
                Presentation = ResultPresentation.Text;
                ValueText = scalar.Value;
                break;
            case TextNode text:
                Presentation = ResultPresentation.Text;
                ValueText = text.Value;
                TruncationText = text.IsTruncated ? $"{ResultText.Truncation(TruncationReason.TextLimit)}. The full text has {text.OriginalLength:N0} characters." : string.Empty;
                break;
            case SequenceNode sequence when ResultTable.TryCreate(sequence) is ResultTable created:
                Presentation = ResultPresentation.Table;
                Table = created;
                TruncationText = created.Truncation is TruncationReason reason ? ResultText.Truncation(reason) + "." : string.Empty;
                break;
            default:
                Presentation = ResultPresentation.Tree;
                ResultTreeItem root = new ResultTreeItem("Result", value);
                Tree = [root];
                TruncationText = root.IsTruncated ? root.TruncationText + "." : string.Empty;
                break;
        }
    }

    private static (string Text, string Kind) Describe(ExecutionOutcome outcome) => outcome switch
    {
        ExecutionOutcome.Success => ("Succeeded", "success"),
        ExecutionOutcome.CompilationFailed => ("Compilation failed", "error"),
        ExecutionOutcome.RuntimeFailed => ("Failed with an exception", "error"),
        ExecutionOutcome.Canceled => ("Canceled", "warning"),
        ExecutionOutcome.TimedOut => ("Stopped: time limit reached", "warning"),
        ExecutionOutcome.WorkerFailed => ("The worker stopped unexpectedly", "error"),
        _ => ("Unknown outcome", "error"),
    };

    private static RunDiagnosticRow ToRow(Diagnostic diagnostic)
    {
        string severity = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => "Error",
            DiagnosticSeverity.Warning => "Warning",
            _ => "Info",
        };
        string place = diagnostic.Location is SourceLocation location ? $"Line {location.Line}, column {location.Column}" : string.Empty;
        if (diagnostic.Code is { Length: > 0 } code)
        {
            place = place.Length > 0 ? $"{place} · {code}" : code;
        }

        return new RunDiagnosticRow(severity, place, diagnostic.Message);
    }

    private static (string Text, string Note) Output(CapturedText captured, string name) =>
        (captured.Text, captured.OriginalLength is int length
            ? string.Create(CultureInfo.InvariantCulture, $"{name} is cut. It shows {captured.Text.Length:N0} of {length:N0} characters.")
            : string.Empty);

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
