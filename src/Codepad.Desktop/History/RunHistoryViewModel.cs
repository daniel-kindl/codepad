using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Codepad.Application.Experiments;
using Codepad.Application.History;
using Codepad.Desktop.Results;
using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;

namespace Codepad.Desktop.History;

/// <summary>One line of the history list. The outcome is a word, so color is never the only signal.</summary>
/// <param name="Id">The ID of the execution.</param>
/// <param name="Text">The line: outcome, time and duration.</param>
public sealed record RunHistoryItem(ExecutionId Id, string Text)
{
    /// <summary>Gets the accessible name of the line.</summary>
    public string AccessibleName => "Saved run: " + Text;
}

/// <summary>
/// The state of the history panel: the saved runs of one experiment, newest first. Selecting a run shows its saved
/// source and result in read-only form. It never changes the editor. A rerun is a separate, explicit command. The
/// inspected record stays in memory, so retention that removes it from storage does not break the view.
/// </summary>
public sealed class RunHistoryViewModel : INotifyPropertyChanged
{
    /// <summary>The text before an experiment is shown.</summary>
    internal const string NO_EXPERIMENT_TEXT = "Open an experiment to see its saved runs.";

    /// <summary>The text of an experiment with no saved run.</summary>
    internal const string NO_RUNS_TEXT = "No saved runs yet. Each run you start is saved here.";

    /// <summary>The notice when history could not store a run.</summary>
    internal const string NOT_SAVED_TEXT = "This run finished, but CodePad couldn't save it to history.";

    /// <summary>The notice when the list could not be read.</summary>
    internal const string LOAD_FAILED_TEXT = "CodePad couldn't read the saved runs.";

    /// <summary>The notice when a saved run is no longer in storage.</summary>
    internal const string GONE_TEXT = "That saved run is no longer available. History keeps only the newest runs.";

    private readonly IRunHistoryRepository history;
    private readonly RunHistoryService service;
    private readonly ResultsViewModel results;
    private ExperimentId? experimentId;
    private RunRecord? inspected;
    private string notice = string.Empty;
    private string emptyText = NO_EXPERIMENT_TEXT;
    private readonly RunGate gate;
    private int navigation;
    private int listVersion;

    /// <summary>Initializes a new instance of the <see cref="RunHistoryViewModel"/> class.</summary>
    /// <param name="history">Reads the saved runs.</param>
    /// <param name="service">Runs a snapshot again and stores the run.</param>
    /// <param name="results">The result panel that shows the saved or the new run.</param>
    /// <param name="gate">Lets one run go at a time. Share it with the Run command. Null gives this model its own gate.</param>
    public RunHistoryViewModel(IRunHistoryRepository history, RunHistoryService service, ResultsViewModel results, RunGate? gate = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(results);
        this.history = history;
        this.service = service;
        this.results = results;
        this.gate = gate ?? new RunGate();
        this.gate.Changed += () => Raise(nameof(CanRerun));
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the result panel that this model fills. The window shows the same instance.</summary>
    public ResultsViewModel Results => results;

    /// <summary>Gets the saved runs of the shown experiment, newest first.</summary>
    public ObservableCollection<RunHistoryItem> Items { get; } = [];

    /// <summary>Gets the text that the panel shows when the list is empty.</summary>
    public string EmptyText
    {
        get => emptyText;
        private set => Set(ref emptyText, value, nameof(EmptyText));
    }

    /// <summary>Gets a short notice about the last action, or an empty string.</summary>
    public string Notice
    {
        get => notice;
        private set => Set(ref notice, value, nameof(Notice));
    }

    /// <summary>Gets a value indicating whether the panel shows a saved run.</summary>
    public bool IsInspecting => inspected is not null;

    /// <summary>Gets the ID of the inspected run, or null.</summary>
    public ExecutionId? InspectedId => inspected?.Request.Id;

    /// <summary>Gets the saved source of the inspected run, or an empty string.</summary>
    public string InspectedSource => inspected?.Request.Source ?? string.Empty;

    /// <summary>Gets the line that says the panel shows a saved run and not the current code.</summary>
    public string InspectionText => inspected is null
        ? string.Empty
        : string.Create(CultureInfo.InvariantCulture, $"Saved run from {inspected.CompletedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}. This is the code as it was then. Your current code is unchanged.");

    /// <summary>Gets a value indicating whether Rerun has a saved run and no run is active.</summary>
    public bool CanRerun => inspected is not null && !gate.IsRunning;

    /// <summary>Shows the saved runs of an experiment. It ends any inspection.</summary>
    /// <param name="id">The experiment.</param>
    /// <param name="cancellationToken">Cancels the wait for storage.</param>
    /// <returns>A task that completes when the list is ready.</returns>
    public async Task ShowExperimentAsync(ExperimentId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        experimentId = id;
        EndInspection();
        Notice = string.Empty;
        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Shows the saved source and result of one run. It does not run anything and does not change the editor.</summary>
    /// <param name="item">The entry to inspect.</param>
    /// <param name="cancellationToken">Cancels the wait for storage.</param>
    /// <returns>A task that completes when the run is shown.</returns>
    public async Task InspectAsync(RunHistoryItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        // Each click, each end of an inspection and each experiment switch starts a new navigation. A read that
        // finishes after a newer navigation started is stale, so it must not change the panel.
        int mine = ++navigation;
        RunRecord? record;
        try
        {
            record = await history.GetAsync(item.Id, cancellationToken).ConfigureAwait(true);
        }
        catch (ExperimentStorageException)
        {
            if (mine == navigation)
            {
                Notice = LOAD_FAILED_TEXT;
            }

            return;
        }

        if (mine != navigation)
        {
            return;
        }

        if (record is null)
        {
            Notice = GONE_TEXT;
            return;
        }

        Notice = string.Empty;
        inspected = record;
        results.Show(record.Report, record.CompletedAt);
        RaiseInspectionChanged();
    }

    /// <summary>Runs the inspected snapshot again. This is an explicit user action. The saved run does not change.</summary>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>A task that completes when the new run is stored and shown.</returns>
    public async Task RerunAsync(CancellationToken cancellationToken = default)
    {
        RunRecord? source = inspected;
        if (source is null || !gate.TryEnter())
        {
            return;
        }

        int mine = navigation;
        try
        {
            CompletedRun run = await service.RerunAsync(source, cancellationToken).ConfigureAwait(true);
            if (mine == navigation)
            {
                // The user is still on the saved run that started this rerun, so the new run replaces it in the panel.
                inspected = run.Record;
                results.Show(run.Record.Report, run.Record.CompletedAt);
                RaiseInspectionChanged();
                Notice = run.StorageFailure is null ? string.Empty : NOT_SAVED_TEXT;
            }

            if (run.StorageFailure is null && experimentId == source.Request.ExperimentId)
            {
                await ReloadAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // The caller canceled the run or the list read. The stored run, if any, is already in history.
        }
        finally
        {
            gate.Exit();
        }
    }

    /// <summary>Stops showing a saved run and clears the result panel.</summary>
    public void EndInspection()
    {
        navigation++;
        if (inspected is null)
        {
            return;
        }

        inspected = null;
        results.Clear();
        RaiseInspectionChanged();
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        int mine = ++listVersion;
        IReadOnlyList<RunSummary> summaries;
        try
        {
            summaries = experimentId is null ? [] : await history.ListAsync(experimentId, cancellationToken).ConfigureAwait(true);
        }
        catch (ExperimentStorageException)
        {
            if (mine == listVersion)
            {
                Items.Clear();
                Notice = LOAD_FAILED_TEXT;
            }

            return;
        }

        if (mine != listVersion)
        {
            // A newer reload started while this one waited. Its list is the right one.
            return;
        }

        Items.Clear();
        foreach (RunSummary summary in summaries)
        {
            Items.Add(new RunHistoryItem(summary.Id, Describe(summary)));
        }

        EmptyText = experimentId is null ? NO_EXPERIMENT_TEXT : NO_RUNS_TEXT;
        if (Notice == LOAD_FAILED_TEXT)
        {
            Notice = string.Empty;
        }
    }

    private static string Describe(RunSummary summary)
    {
        string outcome = summary.Outcome switch
        {
            ExecutionOutcome.Success => "Succeeded",
            ExecutionOutcome.CompilationFailed => "Compilation failed",
            ExecutionOutcome.RuntimeFailed => "Exception",
            ExecutionOutcome.Canceled => "Canceled",
            ExecutionOutcome.TimedOut => "Timed out",
            ExecutionOutcome.WorkerFailed => "Worker failed",
            _ => "Unknown",
        };
        return string.Create(CultureInfo.InvariantCulture, $"{outcome} · {summary.CompletedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {summary.Duration.TotalSeconds:0.##} s");
    }

    private void RaiseInspectionChanged()
    {
        Raise(nameof(IsInspecting));
        Raise(nameof(InspectedSource));
        Raise(nameof(InspectionText));
        Raise(nameof(CanRerun));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Set(ref string field, string value, string name)
    {
        if (!string.Equals(field, value, StringComparison.Ordinal))
        {
            field = value;
            Raise(name);
        }
    }
}
