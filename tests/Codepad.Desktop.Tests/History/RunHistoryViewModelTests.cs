using Codepad.Application.Execution;
using Codepad.Application.Experiments;
using Codepad.Application.History;
using Codepad.Contracts;
using Codepad.Desktop.History;
using Codepad.Desktop.Results;
using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Domain.Results;

namespace Codepad.Desktop.Tests.History;

public sealed class RunHistoryViewModelTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 4, 5, 6, 7, 8, TimeSpan.Zero);

    private readonly MemoryHistory store = new MemoryHistory();
    private readonly FakeRunner runner = new FakeRunner();
    private readonly ResultsViewModel results = new ResultsViewModel();
    private readonly Experiment experiment = Experiment.Create(ExperimentId.New(), OperationKind.CSharp, "Scratch", "1 + 1", Start);

    private RunHistoryViewModel NewModel() => new RunHistoryViewModel(store, new RunHistoryService(runner, store, TimeProvider.System), results);

    [Fact]
    public async Task BeforeAnExperimentIsShownThePanelAsksForOne()
    {
        RunHistoryViewModel model = NewModel();

        Assert.Empty(model.Items);
        Assert.Equal(RunHistoryViewModel.NO_EXPERIMENT_TEXT, model.EmptyText);
        Assert.False(model.CanRerun);
        await model.RerunAsync(TestContext.Current.CancellationToken);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task ShowingAnExperimentListsItsRunsNewestFirstWithOutcomeWords()
    {
        RunRecord old = store.Seed(experiment, ExecutionOutcome.Success, 1);
        RunRecord recent = store.Seed(experiment, ExecutionOutcome.TimedOut, 2);
        RunHistoryViewModel model = NewModel();

        await model.ShowExperimentAsync(experiment.Id, TestContext.Current.CancellationToken);

        Assert.Equal([recent.Request.Id, old.Request.Id], model.Items.Select(item => item.Id));
        Assert.StartsWith("Timed out", model.Items[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("Succeeded", model.Items[1].Text, StringComparison.Ordinal);
        Assert.Equal(RunHistoryViewModel.NO_RUNS_TEXT, model.EmptyText);
    }

    [Fact]
    public async Task InspectingShowsTheSavedSourceAndResultAndLabelsThemAsSaved()
    {
        RunRecord record = store.Seed(experiment, ExecutionOutcome.Success, 1);
        RunHistoryViewModel model = NewModel();
        await model.ShowExperimentAsync(experiment.Id, TestContext.Current.CancellationToken);

        await model.InspectAsync(model.Items[0], TestContext.Current.CancellationToken);

        Assert.True(model.IsInspecting);
        Assert.Equal("1 + 1", model.InspectedSource);
        Assert.Contains("saved run", model.InspectionText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(record.Request.Id.ToString(), results.ExecutionText, StringComparison.Ordinal);
        Assert.True(model.CanRerun);
    }

    [Fact]
    public async Task InspectingDoesNotRunAnything()
    {
        store.Seed(experiment, ExecutionOutcome.Success, 1);
        RunHistoryViewModel model = NewModel();
        await model.ShowExperimentAsync(experiment.Id, TestContext.Current.CancellationToken);

        await model.InspectAsync(model.Items[0], TestContext.Current.CancellationToken);

        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task RerunRunsTheSavedSnapshotNotTheCurrentSourceAndAddsANewEntry()
    {
        RunRecord record = store.Seed(experiment, ExecutionOutcome.Success, 1);
        RunHistoryViewModel model = NewModel();
        await model.ShowExperimentAsync(experiment.Id, TestContext.Current.CancellationToken);
        await model.InspectAsync(model.Items[0], TestContext.Current.CancellationToken);

        await model.RerunAsync(TestContext.Current.CancellationToken);

        ExecutionRequest request = Assert.Single(runner.Requests);
        Assert.Equal("1 + 1", request.Source);
        Assert.NotEqual(record.Request.Id, request.Id);
        Assert.Equal(2, model.Items.Count);
        Assert.Equal(request.Id, model.Items[0].Id);
        Assert.Contains(request.Id.ToString(), results.ExecutionText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailureToStoreTheRerunIsShownAndTheResultStays()
    {
        store.Seed(experiment, ExecutionOutcome.Success, 1);
        RunHistoryViewModel model = NewModel();
        await model.ShowExperimentAsync(experiment.Id, TestContext.Current.CancellationToken);
        await model.InspectAsync(model.Items[0], TestContext.Current.CancellationToken);
        store.FailWith = "disk_full";

        await model.RerunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RunHistoryViewModel.NOT_SAVED_TEXT, model.Notice);
        Assert.DoesNotContain("disk_full", model.Notice, StringComparison.Ordinal);
        Assert.Equal("Succeeded", results.StatusText);
    }

    [Fact]
    public async Task InspectedRecordStaysUsableAfterRetentionRemovesItFromStorage()
    {
        RunRecord record = store.Seed(experiment, ExecutionOutcome.Success, 1);
        RunHistoryViewModel model = NewModel();
        await model.ShowExperimentAsync(experiment.Id, TestContext.Current.CancellationToken);
        await model.InspectAsync(model.Items[0], TestContext.Current.CancellationToken);
        store.Remove(record.Request.Id);

        Assert.True(model.CanRerun);
        await model.RerunAsync(TestContext.Current.CancellationToken);

        Assert.Equal("1 + 1", Assert.Single(runner.Requests).Source);
    }

    [Fact]
    public async Task InspectingAnEntryThatIsGoneShowsANoticeAndKeepsTheLastInspection()
    {
        RunRecord first = store.Seed(experiment, ExecutionOutcome.Success, 1);
        store.Seed(experiment, ExecutionOutcome.RuntimeFailed, 2);
        RunHistoryViewModel model = NewModel();
        await model.ShowExperimentAsync(experiment.Id, TestContext.Current.CancellationToken);
        RunHistoryItem gone = model.Items[1];
        store.Remove(first.Request.Id);

        await model.InspectAsync(gone, TestContext.Current.CancellationToken);

        Assert.Equal(RunHistoryViewModel.GONE_TEXT, model.Notice);
        Assert.False(model.IsInspecting);
    }

    [Fact]
    public async Task EndingTheInspectionLeavesTheEditorSourceAloneAndClearsTheSavedView()
    {
        store.Seed(experiment, ExecutionOutcome.Success, 1);
        RunHistoryViewModel model = NewModel();
        await model.ShowExperimentAsync(experiment.Id, TestContext.Current.CancellationToken);
        await model.InspectAsync(model.Items[0], TestContext.Current.CancellationToken);

        model.EndInspection();

        Assert.False(model.IsInspecting);
        Assert.Equal(string.Empty, model.InspectedSource);
        Assert.False(model.CanRerun);
        Assert.Equal(ResultsViewModel.NO_RUN_TEXT, results.StatusText);
    }

    [Fact]
    public async Task ListFailureShowsANoticeWithoutTheTechnicalReason()
    {
        store.FailList = "corrupt";
        RunHistoryViewModel model = NewModel();

        await model.ShowExperimentAsync(experiment.Id, TestContext.Current.CancellationToken);

        Assert.Equal(RunHistoryViewModel.LOAD_FAILED_TEXT, model.Notice);
        Assert.Empty(model.Items);
    }

    [Fact]
    public async Task SwitchingExperimentClearsTheInspection()
    {
        store.Seed(experiment, ExecutionOutcome.Success, 1);
        RunHistoryViewModel model = NewModel();
        await model.ShowExperimentAsync(experiment.Id, TestContext.Current.CancellationToken);
        await model.InspectAsync(model.Items[0], TestContext.Current.CancellationToken);

        await model.ShowExperimentAsync(ExperimentId.New(), TestContext.Current.CancellationToken);

        Assert.False(model.IsInspecting);
        Assert.Empty(model.Items);
    }
}
