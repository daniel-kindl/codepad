using System.ComponentModel;
using System.Runtime.CompilerServices;
using Codepad.Application.Commands;
using Codepad.Application.History;
using Codepad.Desktop.History;
using Codepad.Desktop.Results;
using Codepad.Domain.Operations;

namespace Codepad.Desktop.Workspace;

/// <summary>
/// Runs the C# experiment of a tab, only when the user asks. It takes a snapshot of the source, runs it in the worker,
/// stores one history record and shows the report. One run goes at a time. Live JSON never goes through this class.
/// The result panel and the history panel are shared by all C# tabs: the controller fills them for the active tab.
/// </summary>
public sealed class RunController : INotifyPropertyChanged
{
    /// <summary>The reason when no C# experiment is active.</summary>
    internal const string NO_CSHARP_REASON = "Open a C# experiment to run it.";

    /// <summary>The reason while a run is in progress.</summary>
    internal const string BUSY_REASON = "A run is already running. Cancel it or wait for it to end.";

    /// <summary>The notice when the run ended but the history store failed.</summary>
    internal const string NOT_SAVED_NOTICE = "The run finished, but CodePad couldn't save it to history.";

    private readonly RunHistoryService? service;
    private readonly RunHistoryViewModel? history;
    private readonly Action<Action> post;
    private readonly Action<string> notify;
    private readonly Action<string> reportFailure;
    private readonly string? unavailableReason;
    private CancellationTokenSource? running;
    private ExperimentTabViewModel? activeTab;
    private ExperimentTabViewModel? runningTab;

    /// <summary>Initializes a new instance of the <see cref="RunController"/> class.</summary>
    /// <param name="service">Runs a snapshot and stores the record. Null when no worker is available.</param>
    /// <param name="history">The history panel, or null.</param>
    /// <param name="results">The shared result panel.</param>
    /// <param name="post">Runs an action on the UI thread.</param>
    /// <param name="notify">Shows a notice to the user.</param>
    /// <param name="reportFailure">Receives a storage reason or an exception type name. It never holds source or output.</param>
    /// <param name="unavailableReason">Why Run is unavailable when <paramref name="service"/> is null.</param>
    public RunController(RunHistoryService? service, RunHistoryViewModel? history, ResultsViewModel results, Action<Action> post, Action<string> notify, Action<string> reportFailure, string? unavailableReason = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(notify);
        ArgumentNullException.ThrowIfNull(reportFailure);
        this.service = service;
        this.history = history;
        Results = results;
        this.post = post;
        this.notify = notify;
        this.reportFailure = reportFailure;
        this.unavailableReason = unavailableReason ?? "Running code is not available in this build.";
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the shared result panel.</summary>
    public ResultsViewModel Results { get; }

    /// <summary>Gets the shared history panel, or null.</summary>
    public RunHistoryViewModel? History => history;

    /// <summary>Gets a value indicating whether a run is in progress.</summary>
    public bool IsRunning => running is not null;

    /// <summary>Tells whether Run can start for a tab, with the reason when it cannot.</summary>
    /// <param name="tab">The tab, or null when none is open.</param>
    /// <returns>The availability.</returns>
    public CommandAvailability Availability(ExperimentTabViewModel? tab)
    {
        if (service is null)
        {
            return new CommandAvailability(false, unavailableReason);
        }

        if (tab is not CSharpTabViewModel)
        {
            return new CommandAvailability(false, NO_CSHARP_REASON);
        }

        return IsRunning ? new CommandAvailability(false, BUSY_REASON) : CommandAvailability.Available;
    }

    /// <summary>Tells whether Cancel can stop a run now.</summary>
    /// <returns>The availability.</returns>
    public CommandAvailability CancelAvailability() => IsRunning ? CommandAvailability.Available : new CommandAvailability(false, "No run is in progress.");

    /// <summary>Runs the tab. A request that Run cannot take does nothing. The caller shows the reason.</summary>
    /// <param name="tab">The tab.</param>
    /// <param name="cancellationToken">Cancels the run. <see cref="Cancel"/> does the same.</param>
    /// <returns>A task that ends when the run ends and the report is shown.</returns>
    public async Task RunAsync(ExperimentTabViewModel tab, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (!Availability(tab).Enabled || tab is not CSharpTabViewModel csharp)
        {
            return;
        }

        using CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        running = source;
        runningTab = csharp;
        Raise(nameof(IsRunning));
        Results.BeginRun();
        try
        {
            CompletedRun run = await service!.RunAsync(csharp.CurrentExperiment, source.Token).ConfigureAwait(true);
            csharp.LastRun = run.Record;
            if (ReferenceEquals(activeTab, csharp))
            {
                Results.Show(run.Record.Report, run.Record.CompletedAt);
            }

            if (run.StorageFailure is string failure)
            {
                reportFailure(failure);
                notify(NOT_SAVED_NOTICE);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The service turns a storage failure and a worker failure into a result. This is a bug. The text can hold the source, so only the type goes out.
            reportFailure(exception.GetType().Name);
            if (ReferenceEquals(activeTab, csharp))
            {
                Results.ShowStopped("The run stopped unexpectedly.", "CodePad could not run this code. Try again.");
            }
        }
        finally
        {
            running = null;
            runningTab = null;
            Raise(nameof(IsRunning));
        }

        await RefreshHistoryAsync(csharp).ConfigureAwait(true);
    }

    /// <summary>Asks the run in progress to stop. The report then has the outcome Canceled.</summary>
    public void Cancel() => running?.Cancel();

    /// <summary>Fills the shared panels for the tab that became active.</summary>
    /// <param name="tab">The tab, or null when no tab is open.</param>
    public void OnActivated(ExperimentTabViewModel? tab)
    {
        activeTab = tab;
        Results.Clear();
        if (tab is not CSharpTabViewModel csharp)
        {
            return;
        }

        if (csharp.LastRun is RunRecord last)
        {
            Results.Show(last.Report, last.CompletedAt);
        }

        if (ReferenceEquals(runningTab, csharp))
        {
            Results.BeginRun();
        }

        _ = RefreshHistoryAsync(csharp);
    }

    private async Task RefreshHistoryAsync(CSharpTabViewModel tab)
    {
        if (history is null || !ReferenceEquals(activeTab, tab))
        {
            return;
        }

        try
        {
            await history.ShowExperimentAsync(tab.Id).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            reportFailure(exception.GetType().Name);
        }
    }

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
