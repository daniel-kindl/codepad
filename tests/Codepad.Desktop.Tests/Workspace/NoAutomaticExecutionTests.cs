using Codepad.Application.Experiments;
using Codepad.Application.History;
using Codepad.Application.Workspace;
using Codepad.Desktop.Commands;
using Codepad.Desktop.History;
using Codepad.Desktop.Library;
using Codepad.Desktop.Results;
using Codepad.Desktop.Tests.History;
using Codepad.Desktop.Tests.Library;
using Codepad.Desktop.Workspace;
using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;

namespace Codepad.Desktop.Tests.Workspace;

/// <summary>
/// The audit of the rule "CodePad never runs C# code by itself". The runner counts each request. Edit, autosave,
/// restore after a crash, opening, switching, the library, the history, the palette and closing must leave the count at
/// zero. Only the Run command, and Rerun in the history, may start a run.
/// </summary>
public sealed class NoAutomaticExecutionTests
{
    private const string DANGEROUS = "System.IO.File.WriteAllText(\"/tmp/codepad-should-never-exist\", \"x\"); 1";

    private readonly WorkspaceFixture fixture;
    private readonly FakeRunner runner = new FakeRunner();
    private readonly MemoryHistory history = new MemoryHistory();
    private readonly ResultsViewModel results = new ResultsViewModel();

    public NoAutomaticExecutionTests()
    {
        fixture = new WorkspaceFixture(owner =>
        {
            RunGate gate = new RunGate();
            RunHistoryService service = new RunHistoryService(runner, history, owner.Time);
            RunHistoryViewModel historyModel = new RunHistoryViewModel(history, service, results, gate);
            return new RunController(service, historyModel, results, owner.Post, text => owner.Model.ShowNotice(text), owner.Failures.Add, null, gate);
        });
    }

    [Fact]
    public async Task RestoringARecoveredSessionAfterACrashRunsNothing()
    {
        Experiment first = fixture.Seed(OperationKind.CSharp, "Recovered one", DANGEROUS);
        Experiment second = fixture.Seed(OperationKind.CSharp, "Recovered two", DANGEROUS);
        history.Seed(first, ExecutionOutcome.Success, 1);
        await fixture.Session.SaveAsync(TabSet.Empty.OpenTab(first.Id).OpenTab(second.Id), TestContext.Current.CancellationToken);

        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        fixture.RunUi();

        Assert.Equal(2, fixture.Model.Tabs.Count);
        Assert.Empty(runner.Requests);
        Assert.Equal(ResultsViewModel.NO_RUN_TEXT, results.StatusText);
    }

    [Fact]
    public async Task EditingAutosavingSwitchingNewTabsAndClosingRunNothing()
    {
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        CSharpTabViewModel tab = Assert.IsType<CSharpTabViewModel>(fixture.Model.Tabs[0]);

        tab.Editor.OnTextEdited(DANGEROUS);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        ExperimentTabViewModel json = await fixture.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);
        ExperimentTabViewModel second = await fixture.Model.NewExperimentAsync(OperationKind.CSharp, TestContext.Current.CancellationToken);
        fixture.Model.Activate(tab);
        fixture.Model.Next();
        fixture.Model.Previous();
        fixture.Model.Move(second, 0);
        Assert.True(await fixture.Model.CloseAsync(json, TestContext.Current.CancellationToken));
        Assert.True(await fixture.Model.TryCloseAllAsync(TestContext.Current.CancellationToken));
        fixture.RunUi();

        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task OpeningFromTheLibraryAndTheHistoryRunsNothingButRerunNeedsTheButton()
    {
        Experiment stored = fixture.Seed(OperationKind.CSharp, "Seeded", DANGEROUS);
        RunRecord record = history.Seed(stored, ExecutionOutcome.Success, 1);
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        LibraryViewModel library = new LibraryViewModel(new FakeLibrary(fixture.Experiments), fixture.Model, fixture.Time, TimeSpan.FromMilliseconds(100), fixture.Post);
        await library.RefreshAsync(TestContext.Current.CancellationToken);
        fixture.RunUi();

        await library.OpenAsync(library.Items.Single(item => item.Id == stored.Id), TestContext.Current.CancellationToken);
        RunHistoryViewModel historyModel = fixture.Model.Runs!.History!;
        await historyModel.ShowExperimentAsync(stored.Id, TestContext.Current.CancellationToken);
        await historyModel.InspectAsync(historyModel.Items.Single(item => item.Id == record.Request.Id), TestContext.Current.CancellationToken);

        Assert.Empty(runner.Requests);
        Assert.True(historyModel.IsInspecting);

        await historyModel.RerunAsync(TestContext.Current.CancellationToken);

        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task OpeningThePaletteTypingAndFilteringRunNothing()
    {
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        CommandPaletteViewModel palette = new CommandPaletteViewModel(() => BuiltInCommands.Create(fixture.Model, null, () => Task.CompletedTask));

        palette.Open();
        foreach (string text in new[] { "r", "ru", "run", "run c#", "run c# x" })
        {
            palette.Query = text;
        }

        palette.MoveSelection(1);
        palette.Close();

        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task OnlyTheRunCommandStartsARunAndOnlyForTheActiveCSharpTab()
    {
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        CSharpTabViewModel tab = Assert.IsType<CSharpTabViewModel>(fixture.Model.Tabs[0]);
        tab.Editor.OnTextEdited("1 + 1");
        Assert.Empty(runner.Requests);

        await fixture.Model.Runs!.RunAsync(tab, TestContext.Current.CancellationToken);

        Assert.Equal("1 + 1", Assert.Single(runner.Requests).Source);
    }

    [Fact]
    public async Task ARunOfAJsonTabIsRefusedWhateverTheCaller()
    {
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        ExperimentTabViewModel json = await fixture.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);

        await fixture.Model.Runs!.RunAsync(json, TestContext.Current.CancellationToken);

        Assert.Empty(runner.Requests);
    }
}
