using Codepad.Application.Commands;
using Codepad.Application.Execution;
using Codepad.Application.History;
using Codepad.Desktop.History;
using Codepad.Desktop.Json;
using Codepad.Desktop.Results;
using Codepad.Desktop.Tests.History;
using Codepad.Desktop.Workspace;
using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;

namespace Codepad.Desktop.Tests.Workspace;

public sealed class RunControllerTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RunningTheActiveCSharpTabRunsItsCurrentSourceAndShowsTheResultAndStoresARecord()
    {
        Fixture fixture = await Fixture.CreateAsync();
        fixture.Csharp.Editor.OnTextEdited("1 + 1");

        await fixture.Runs.RunAsync(fixture.Csharp, TestContext.Current.CancellationToken);
        fixture.RunUi();

        Assert.Equal("1 + 1", Assert.Single(fixture.Runner.Requests).Source);
        Assert.Equal("Succeeded", fixture.Results.StatusText);
        Assert.Equal("2", fixture.Results.ValueText);
        Assert.Single(fixture.History.Records);
        Assert.False(fixture.Runs.IsRunning);
    }

    [Fact]
    public async Task RunIsNotAvailableForAJsonTabOrWithoutATab()
    {
        Fixture fixture = await Fixture.CreateAsync();
        JsonTabViewModel json = Assert.IsType<JsonTabViewModel>(await fixture.Workspace.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken));

        CommandAvailability forJson = fixture.Runs.Availability(json);
        CommandAvailability forNone = fixture.Runs.Availability(null);

        Assert.False(forJson.Enabled);
        Assert.Contains("C# experiment", forJson.Reason, StringComparison.Ordinal);
        Assert.False(forNone.Enabled);
        Assert.Empty(fixture.Runner.Requests);
    }

    [Fact]
    public async Task RunningNeverEvaluatesLiveJsonOrRunsAJsonTab()
    {
        Fixture fixture = await Fixture.CreateAsync();
        JsonTabViewModel json = Assert.IsType<JsonTabViewModel>(await fixture.Workspace.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken));
        json.Tool.OnTextEdited("[1]");

        await fixture.Runs.RunAsync(json, TestContext.Current.CancellationToken);

        Assert.Empty(fixture.Runner.Requests);
        Assert.Empty(fixture.History.Records);
    }

    [Fact]
    public async Task OnlyOneRunAtATimeAndASecondRequestDoesNotStartAnother()
    {
        Fixture fixture = await Fixture.CreateAsync();
        TaskCompletionSource gate = new TaskCompletionSource();
        fixture.Runner.Gate = gate.Task;

        Task first = fixture.Runs.RunAsync(fixture.Csharp, TestContext.Current.CancellationToken);
        await fixture.WaitAsync(() => fixture.Runner.Requests.Count == 1);
        Assert.True(fixture.Runs.IsRunning);
        Assert.False(fixture.Runs.Availability(fixture.Csharp).Enabled);
        Assert.Contains("already running", fixture.Runs.Availability(fixture.Csharp).Reason, StringComparison.Ordinal);

        await fixture.Runs.RunAsync(fixture.Csharp, TestContext.Current.CancellationToken);
        gate.SetResult();
        await first;

        Assert.Single(fixture.Runner.Requests);
    }

    [Fact]
    public async Task CancelStopsTheRunAndTheRecordSaysCanceled()
    {
        Fixture fixture = await Fixture.CreateAsync();
        fixture.Runner.WaitForCancel = true;

        Task run = fixture.Runs.RunAsync(fixture.Csharp, TestContext.Current.CancellationToken);
        await fixture.WaitAsync(() => fixture.Runner.Requests.Count == 1);
        Assert.True(fixture.Runs.CancelAvailability().Enabled);

        fixture.Runs.Cancel();
        await run;
        fixture.RunUi();

        Assert.Equal("Canceled", fixture.Results.StatusText);
        Assert.False(fixture.Runs.CancelAvailability().Enabled);
    }

    [Fact]
    public async Task AResultForATabThatIsNotActiveAnyMoreDoesNotReplaceThePanel()
    {
        Fixture fixture = await Fixture.CreateAsync();
        TaskCompletionSource gate = new TaskCompletionSource();
        fixture.Runner.Gate = gate.Task;
        Task run = fixture.Runs.RunAsync(fixture.Csharp, TestContext.Current.CancellationToken);
        await fixture.WaitAsync(() => fixture.Runner.Requests.Count == 1);

        JsonTabViewModel json = Assert.IsType<JsonTabViewModel>(await fixture.Workspace.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken));
        fixture.Runs.OnActivated(json);
        gate.SetResult();
        await run;
        fixture.RunUi();

        Assert.Equal("No run yet", fixture.Results.StatusText);

        // Coming back shows the result of the run that ended meanwhile.
        fixture.Runs.OnActivated(fixture.Csharp);
        Assert.Equal("Succeeded", fixture.Results.StatusText);
    }

    [Fact]
    public async Task SwitchingTabsShowsTheLastResultOfEachTab()
    {
        Fixture fixture = await Fixture.CreateAsync();
        await fixture.Runs.RunAsync(fixture.Csharp, TestContext.Current.CancellationToken);
        fixture.RunUi();
        CSharpTabViewModel other = Assert.IsType<CSharpTabViewModel>(await fixture.Workspace.Model.NewExperimentAsync(OperationKind.CSharp, TestContext.Current.CancellationToken));

        fixture.Runs.OnActivated(other);
        Assert.Equal("No run yet", fixture.Results.StatusText);

        fixture.Runs.OnActivated(fixture.Csharp);
        Assert.Equal("Succeeded", fixture.Results.StatusText);
    }

    [Fact]
    public async Task AFailedHistoryWriteKeepsTheResultAndTellsTheUser()
    {
        Fixture fixture = await Fixture.CreateAsync();
        fixture.History.FailWith = "disk_full";

        await fixture.Runs.RunAsync(fixture.Csharp, TestContext.Current.CancellationToken);
        fixture.RunUi();

        Assert.Equal("Succeeded", fixture.Results.StatusText);
        Assert.Contains("couldn't save it to history", fixture.Notices.Single(), StringComparison.Ordinal);
        Assert.Equal(["disk_full"], fixture.Failures);
    }

    [Fact]
    public async Task RunIsUnavailableWithoutAWorker()
    {
        Fixture fixture = await Fixture.CreateAsync(workerReason: "The worker program is missing.");

        CommandAvailability availability = fixture.Runs.Availability(fixture.Csharp);

        Assert.False(availability.Enabled);
        Assert.Equal("The worker program is missing.", availability.Reason);
        await fixture.Runs.RunAsync(fixture.Csharp, TestContext.Current.CancellationToken);
        Assert.Empty(fixture.Runner.Requests);
    }

    [Fact]
    public async Task ARunnerBugShowsAFriendlyNoticeAndReportsOnlyTheType()
    {
        Fixture fixture = await Fixture.CreateAsync();
        fixture.Runner.Throw = new InvalidOperationException("secret source text");

        await fixture.Runs.RunAsync(fixture.Csharp, TestContext.Current.CancellationToken);
        fixture.RunUi();

        Assert.Equal([nameof(InvalidOperationException)], fixture.Failures);
        Assert.DoesNotContain("secret", string.Join(' ', fixture.Notices), StringComparison.Ordinal);
        Assert.False(fixture.Runs.IsRunning);
        Assert.Equal("The run stopped unexpectedly.", fixture.Results.StatusText);
    }

    private sealed class Fixture
    {
        private Fixture()
        {
        }

        public WorkspaceFixture Workspace { get; private set; } = null!;

        public CapturingRunner Runner { get; } = new CapturingRunner();

        public MemoryHistory History { get; } = new MemoryHistory();

        public ResultsViewModel Results { get; } = new ResultsViewModel();

        public RunController Runs { get; private set; } = null!;

        public CSharpTabViewModel Csharp { get; private set; } = null!;

        public List<string> Notices { get; } = [];

        public List<string> Failures { get; } = [];

        public static async Task<Fixture> CreateAsync(string? workerReason = null)
        {
            Fixture fixture = new Fixture();
            fixture.Workspace = new WorkspaceFixture();
            RunHistoryService service = new RunHistoryService(fixture.Runner, fixture.History, fixture.Workspace.Time);
            RunHistoryViewModel history = new RunHistoryViewModel(fixture.History, service, fixture.Results);
            fixture.Runs = new RunController(workerReason is null ? service : null, history, fixture.Results, fixture.Workspace.Post, fixture.Notices.Add, fixture.Failures.Add, workerReason);
            await fixture.Workspace.Model.RestoreAsync(TestContext.Current.CancellationToken);
            fixture.Csharp = Assert.IsType<CSharpTabViewModel>(fixture.Workspace.Model.Tabs[0]);
            fixture.Runs.OnActivated(fixture.Csharp);
            return fixture;
        }

        public void RunUi() => Workspace.RunUi();

        public async Task WaitAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow + _timeout;
            while (true)
            {
                RunUi();
                if (condition())
                {
                    return;
                }

                Assert.True(DateTime.UtcNow < deadline, "The condition did not become true.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }
    }

    private sealed class CapturingRunner : ICSharpRunner
    {
        public List<ExecutionRequest> Requests { get; } = [];

        public Task? Gate { get; set; }

        public bool WaitForCancel { get; set; }

        public Exception? Throw { get; set; }

        public async Task<RunReport> RunAsync(ExecutionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Throw is not null)
            {
                throw Throw;
            }

            if (Gate is not null)
            {
                await Gate;
            }

            if (WaitForCancel)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return MemoryHistory.Report(request, ExecutionOutcome.Canceled);
                }
            }

            return MemoryHistory.Report(request, ExecutionOutcome.Success);
        }
    }
}
