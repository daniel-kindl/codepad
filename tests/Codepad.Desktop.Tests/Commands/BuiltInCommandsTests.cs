using Codepad.Application.Commands;
using Codepad.Application.History;
using Codepad.Desktop.Commands;
using Codepad.Desktop.History;
using Codepad.Desktop.Results;
using Codepad.Desktop.Tests.History;
using Codepad.Desktop.Tests.Workspace;
using Codepad.Desktop.Workspace;
using Codepad.Domain.Operations;

namespace Codepad.Desktop.Tests.Commands;

public sealed class BuiltInCommandsTests
{
    private readonly WorkspaceFixtureWithRuns fixture = new WorkspaceFixtureWithRuns();
    private int libraryOpened;
    private int copied;

    private IReadOnlyList<AppCommand> Commands() => BuiltInCommands.Create(fixture.Workspace.Model, () => libraryOpened++, () => { copied++; return Task.CompletedTask; });

    private AppCommand Find(string id) => Commands().Single(command => command.Id == id);

    [Fact]
    public async Task TheListHasTheCoreActionsAndTheKeyHints()
    {
        await fixture.StartAsync();

        IReadOnlyList<AppCommand> commands = Commands();

        string[] expected = ["experiment.new.csharp", "experiment.new.json", "run.csharp", "run.cancel", "library.open", "result.copy", "tab.next", "tab.previous", "tab.close", "settings.open"];
        Assert.Equal(expected, commands.Select(command => command.Id));
        Assert.Equal("Ctrl+Enter", Find("run.csharp").KeyHint);
        Assert.Equal("Ctrl+P", Find("library.open").KeyHint);
        Assert.Equal(commands.Count, commands.Select(command => command.Id).Distinct().Count());
    }

    [Fact]
    public async Task NewCommandsAddAndActivateATab()
    {
        await fixture.StartAsync();

        await Find("experiment.new.json").Execute();
        await Find("experiment.new.csharp").Execute();

        Assert.Equal([OperationKind.CSharp, OperationKind.Json, OperationKind.CSharp], fixture.Workspace.Model.Tabs.Select(tab => tab.Kind));
        Assert.Equal(OperationKind.CSharp, fixture.Workspace.Model.ActiveTab?.Kind);
    }

    [Fact]
    public async Task RunIsEnabledOnACSharpTabAndDisabledOnAJsonTabWithAReason()
    {
        await fixture.StartAsync();
        Assert.True(Find("run.csharp").Availability().Enabled);

        await fixture.Workspace.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);

        CommandAvailability availability = Find("run.csharp").Availability();
        Assert.False(availability.Enabled);
        Assert.Equal(RunController.NO_CSHARP_REASON, availability.Reason);
    }

    [Fact]
    public async Task RunNeverStartsLiveJsonAndRunsTheCSharpSourceOnlyOnTheCommand()
    {
        await fixture.StartAsync();
        CSharpTabViewModel tab = Assert.IsType<CSharpTabViewModel>(fixture.Workspace.Model.ActiveTab);
        tab.Editor.OnTextEdited("40 + 2");
        Assert.Empty(fixture.Runner.Requests);

        await Find("run.csharp").Execute();

        Assert.Equal("40 + 2", Assert.Single(fixture.Runner.Requests).Source);
    }

    [Fact]
    public async Task CancelIsDisabledWhenNothingRuns()
    {
        await fixture.StartAsync();

        CommandAvailability availability = Find("run.cancel").Availability();

        Assert.False(availability.Enabled);
        Assert.Equal("No run is in progress.", availability.Reason);
    }

    [Fact]
    public async Task TabCommandsNeedMoreThanOneTab()
    {
        await fixture.StartAsync();
        Assert.False(Find("tab.next").Availability().Enabled);
        Assert.Equal("Only one tab is open.", Find("tab.previous").Availability().Reason);

        await fixture.Workspace.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);

        Assert.True(Find("tab.next").Availability().Enabled);
    }

    [Fact]
    public async Task SwitchCommandsListTheOtherOpenTabsAndActivateThem()
    {
        await fixture.StartAsync();
        ExperimentTabViewModel first = fixture.Workspace.Model.Tabs[0];
        await fixture.Workspace.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);

        AppCommand switchCommand = Assert.Single(Commands(), command => command.Id.StartsWith("tab.switch.", StringComparison.Ordinal));
        Assert.Equal($"Switch to {first.Title}", switchCommand.Label);

        await switchCommand.Execute();

        Assert.Same(first, fixture.Workspace.Model.ActiveTab);
    }

    [Fact]
    public async Task CopyResultIsDisabledWithoutAResultAndEnabledAfterARun()
    {
        await fixture.StartAsync();
        Assert.False(Find("result.copy").Availability().Enabled);

        await Find("run.csharp").Execute();
        fixture.Workspace.RunUi();

        Assert.True(Find("result.copy").Availability().Enabled);
        await Find("result.copy").Execute();
        Assert.Equal(1, copied);
    }

    [Fact]
    public async Task OpenLibraryCallsTheHostAndIsDisabledWithoutALibrary()
    {
        await fixture.StartAsync();

        await Find("library.open").Execute();

        Assert.Equal(1, libraryOpened);
        AppCommand without = BuiltInCommands.Create(fixture.Workspace.Model, null, () => Task.CompletedTask).Single(command => command.Id == "library.open");
        Assert.False(without.Availability().Enabled);
    }

    [Fact]
    public async Task SettingsIsListedButDisabledWithAnExplanation()
    {
        await fixture.StartAsync();

        CommandAvailability availability = Find("settings.open").Availability();

        Assert.False(availability.Enabled);
        Assert.Equal("Settings are not available yet.", availability.Reason);
    }

    [Fact]
    public async Task CloseTabClosesTheActiveTabAndKeepsTheExperiment()
    {
        await fixture.StartAsync();
        ExperimentTabViewModel second = await fixture.Workspace.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);

        await Find("tab.close").Execute();

        Assert.Single(fixture.Workspace.Model.Tabs);
        Assert.NotNull(fixture.Workspace.Experiments.Stored(second.Id));
    }

    [Fact]
    public async Task WithoutAWorkerRunIsDisabledWithTheReason()
    {
        WorkspaceFixtureWithRuns noWorker = new WorkspaceFixtureWithRuns(workerReason: "The worker program is missing.");
        await noWorker.StartAsync();

        CommandAvailability availability = BuiltInCommands.Create(noWorker.Workspace.Model, null, () => Task.CompletedTask).Single(command => command.Id == "run.csharp").Availability();

        Assert.False(availability.Enabled);
        Assert.Equal("The worker program is missing.", availability.Reason);
    }

    private sealed class WorkspaceFixtureWithRuns
    {
        private readonly string? workerReason;

        public WorkspaceFixtureWithRuns(string? workerReason = null)
        {
            this.workerReason = workerReason;
            Workspace = new WorkspaceFixture(CreateRuns);
        }

        public WorkspaceFixture Workspace { get; }

        public FakeRunner Runner { get; } = new FakeRunner();

        public MemoryHistory History { get; } = new MemoryHistory();

        public ResultsViewModel Results { get; } = new ResultsViewModel();

        public Task StartAsync() => Workspace.Model.RestoreAsync(TestContext.Current.CancellationToken);

        private RunController CreateRuns(WorkspaceFixture owner)
        {
            RunHistoryService service = new RunHistoryService(Runner, History, owner.Time);
            RunHistoryViewModel history = new RunHistoryViewModel(History, service, Results);
            return new RunController(workerReason is null ? service : null, history, Results, owner.Post, text => owner.Model.ShowNotice(text), owner.Failures.Add, workerReason);
        }
    }
}
