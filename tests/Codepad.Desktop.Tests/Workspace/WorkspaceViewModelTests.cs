using Codepad.Application.Experiments;
using Codepad.Desktop.Json;
using Codepad.Desktop.Workspace;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;

namespace Codepad.Desktop.Tests.Workspace;

public sealed class WorkspaceViewModelTests
{
    private static readonly TimeSpan _autosave = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task AnEmptyStoreStartsWithOneNewCSharpTab()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();

        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);

        ExperimentTabViewModel tab = Assert.Single(fixture.Model.Tabs);
        Assert.Equal(OperationKind.CSharp, tab.Kind);
        Assert.Same(tab, fixture.Model.ActiveTab);
        Assert.True(tab.IsActive);
    }

    [Fact]
    public async Task TwoTabsKeepIndependentTextAndKind()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        CSharpTabViewModel csharp = Assert.IsType<CSharpTabViewModel>(fixture.Model.ActiveTab);
        csharp.Editor.OnTextEdited("int a = 1;");

        JsonTabViewModel json = Assert.IsType<JsonTabViewModel>(await fixture.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken));
        json.Tool.OnTextEdited("[1, 2]");
        fixture.Model.Activate(csharp);
        csharp.Editor.OnTextEdited("int a = 2;");
        fixture.Model.Activate(json);

        Assert.Equal("[1, 2]", json.Tool.Text);
        Assert.Equal("int a = 2;", csharp.Editor.Text);
        Assert.Equal("[1, 2]", json.Source);
        Assert.Equal("int a = 2;", csharp.Source);
        Assert.Equal(OperationKind.Json, json.Kind);
        Assert.Equal(OperationKind.CSharp, csharp.Kind);
    }

    [Fact]
    public async Task OnlyOneTabIsActive()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        ExperimentTabViewModel first = fixture.Model.Tabs[0];
        ExperimentTabViewModel second = await fixture.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);

        Assert.False(first.IsActive);
        Assert.True(second.IsActive);

        fixture.Model.Activate(first);

        Assert.True(first.IsActive);
        Assert.False(second.IsActive);
    }

    [Fact]
    public async Task ClosingATabKeepsTheSavedExperiment()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        CSharpTabViewModel tab = Assert.IsType<CSharpTabViewModel>(fixture.Model.ActiveTab);
        tab.Editor.OnTextEdited("keep me");
        await fixture.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);

        Assert.True(await fixture.Model.CloseAsync(tab, TestContext.Current.CancellationToken));

        Assert.Single(fixture.Model.Tabs);
        Experiment? stored = fixture.Experiments.Stored(tab.Id);
        Assert.NotNull(stored);
        Assert.Equal("keep me", stored.Source);
    }

    [Fact]
    public async Task ClosingTheLastTabLeavesAnEmptyWorkspace()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);

        await fixture.Model.CloseAsync(fixture.Model.Tabs[0], TestContext.Current.CancellationToken);

        Assert.Empty(fixture.Model.Tabs);
        Assert.Null(fixture.Model.ActiveTab);
        Assert.False(fixture.Model.HasTabs);
    }

    [Fact]
    public async Task RestartRestoresTheTabOrderTheActiveTabAndTheSavedText()
    {
        WorkspaceFixture first = new WorkspaceFixture();
        await first.Model.RestoreAsync(TestContext.Current.CancellationToken);
        CSharpTabViewModel csharp = Assert.IsType<CSharpTabViewModel>(first.Model.ActiveTab);
        csharp.Editor.OnTextEdited("int a = 1;");
        JsonTabViewModel json = Assert.IsType<JsonTabViewModel>(await first.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken));
        json.Tool.OnTextEdited("{ \"a\": 1 }");
        first.Model.Move(json, 0);
        first.Model.Activate(csharp);
        Assert.True(await first.Model.TryCloseAllAsync(TestContext.Current.CancellationToken));
        await first.Model.SessionSaved;

        WorkspaceFixture second = new WorkspaceFixture();
        foreach (Experiment experiment in await first.Experiments.ListAsync(TestContext.Current.CancellationToken))
        {
            second.Experiments.Seed(experiment);
        }

        second.Preferences.Values[Codepad.Application.Workspace.SessionStore.KEY] = first.Preferences.Values[Codepad.Application.Workspace.SessionStore.KEY];
        await second.Model.RestoreAsync(TestContext.Current.CancellationToken);

        Assert.Equal([OperationKind.Json, OperationKind.CSharp], second.Model.Tabs.Select(tab => tab.Kind));
        Assert.Equal(csharp.Id, second.Model.ActiveTab?.Id);
        Assert.Equal("int a = 1;", Assert.IsType<CSharpTabViewModel>(second.Model.Tabs[1]).Editor.Text);
        Assert.Equal("{ \"a\": 1 }", Assert.IsType<JsonTabViewModel>(second.Model.Tabs[0]).Tool.Text);
    }

    [Fact]
    public async Task AMissingExperimentIsOmittedOnRestore()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        Experiment kept = fixture.Seed(OperationKind.CSharp, "Kept", "1");
        Experiment gone = Experiment.Create(ExperimentId.New(), OperationKind.Json, "Gone", "[]", fixture.Time.GetUtcNow());
        await fixture.Session.SaveAsync(Codepad.Application.Workspace.TabSet.Empty.OpenTab(gone.Id).OpenTab(kept.Id).Activate(gone.Id), TestContext.Current.CancellationToken);

        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);

        Assert.Equal(kept.Id, Assert.Single(fixture.Model.Tabs).Id);
        Assert.Equal(kept.Id, fixture.Model.ActiveTab?.Id);
    }

    [Fact]
    public async Task DamagedSessionDataStartsWithANewTab()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        fixture.Seed(OperationKind.CSharp, "Old", "x");
        fixture.Preferences.Values[Codepad.Application.Workspace.SessionStore.KEY] = "{broken";

        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);

        Assert.Single(fixture.Model.Tabs);
        Assert.Equal(string.Empty, fixture.Model.Tabs[0].Source);
    }

    [Fact]
    public async Task ReorderingByIndexAndByStepWorksAndIsKeptInTheSession()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        ExperimentTabViewModel a = fixture.Model.Tabs[0];
        ExperimentTabViewModel b = await fixture.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);
        ExperimentTabViewModel c = await fixture.Model.NewExperimentAsync(OperationKind.CSharp, TestContext.Current.CancellationToken);

        fixture.Model.Move(a, 2);
        Assert.Equal([b, c, a], fixture.Model.Tabs);

        fixture.Model.MoveBy(a, -1);
        Assert.Equal([b, a, c], fixture.Model.Tabs);

        fixture.Model.MoveBy(b, -1);
        Assert.Equal([b, a, c], fixture.Model.Tabs);
        await fixture.Model.SessionSaved;
        Assert.Contains(b.Id.ToString(), fixture.Preferences.Values[Codepad.Application.Workspace.SessionStore.KEY], StringComparison.Ordinal);
    }

    [Fact]
    public async Task NextAndPreviousCycleTheTabs()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        ExperimentTabViewModel a = fixture.Model.Tabs[0];
        ExperimentTabViewModel b = await fixture.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);

        fixture.Model.Next();
        Assert.Same(a, fixture.Model.ActiveTab);
        fixture.Model.Previous();
        Assert.Same(b, fixture.Model.ActiveTab);
    }

    [Fact]
    public async Task ASaveFailureStaysVisibleOnTheAffectedTabAndDoesNotMarkItClean()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        ExperimentTabViewModel broken = fixture.Model.Tabs[0];
        await broken.FlushAsync(TestContext.Current.CancellationToken);
        ExperimentTabViewModel other = await fixture.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);
        await other.FlushAsync(TestContext.Current.CancellationToken);
        fixture.Experiments.Failure = new ExperimentStorageException("disk_full");

        Assert.IsType<CSharpTabViewModel>(broken).Editor.OnTextEdited("unsaved work");
        await broken.FlushAsync(TestContext.Current.CancellationToken);
        fixture.RunUi();

        Assert.True(broken.HasSaveProblem);
        Assert.Contains("disk is full", broken.SaveText, StringComparison.Ordinal);
        Assert.Contains("Your text is still in the editor", broken.SaveText, StringComparison.Ordinal);
        Assert.Equal("Save failed", broken.SaveBadge);
        Assert.False(other.HasSaveProblem);
        Assert.Equal("unsaved work", ((CSharpTabViewModel)broken).Editor.Text);
    }

    [Fact]
    public async Task ClosingATabWithAFailedSaveKeepsItOpenFirstAndClosesOnTheSecondRequest()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        ExperimentTabViewModel tab = fixture.Model.Tabs[0];
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        fixture.Experiments.Failure = new ExperimentStorageException("disk_full");
        Assert.IsType<CSharpTabViewModel>(tab).Editor.OnTextEdited("precious");

        Assert.False(await fixture.Model.CloseAsync(tab, TestContext.Current.CancellationToken));
        Assert.Single(fixture.Model.Tabs);
        Assert.Contains("Close it again to discard", fixture.Model.Notice, StringComparison.Ordinal);

        fixture.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await fixture.Model.CloseAsync(tab, TestContext.Current.CancellationToken));
        Assert.Empty(fixture.Model.Tabs);
    }

    [Fact]
    public async Task ARetryAfterAFailureSavesTheText()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        ExperimentTabViewModel tab = fixture.Model.Tabs[0];
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        fixture.Experiments.Failure = new ExperimentStorageException("busy");
        Assert.IsType<CSharpTabViewModel>(tab).Editor.OnTextEdited("retry me");
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(tab.HasSaveProblem);

        fixture.Experiments.Failure = null;
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        fixture.RunUi();

        Assert.False(tab.HasSaveProblem);
        Assert.Equal("retry me", fixture.Experiments.Stored(tab.Id)?.Source);
    }

    [Fact]
    public async Task ASessionWriteFailureIsReportedAsATypeNameAndDoesNotStopTheTabs()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        fixture.Preferences.Failure = new InvalidOperationException("secret /home/user");

        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        await fixture.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);
        await fixture.Model.SessionSaved;

        Assert.Equal(2, fixture.Model.Tabs.Count);
        Assert.All(fixture.Failures, failure => Assert.Equal(nameof(InvalidOperationException), failure));
        Assert.NotEmpty(fixture.Failures);
    }

    [Fact]
    public async Task NewTabsGetDistinctTitles()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        await fixture.Model.NewExperimentAsync(OperationKind.CSharp, TestContext.Current.CancellationToken);
        await fixture.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);

        Assert.Equal(3, fixture.Model.Tabs.Select(tab => tab.Title).Distinct().Count());
    }

    [Fact]
    public async Task ATabNeverHoldsTheSameExperimentTwice()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        Experiment stored = fixture.Seed(OperationKind.CSharp, "Seeded", "x");
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        await fixture.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);

        await fixture.Model.OpenAsync(stored.Id, TestContext.Current.CancellationToken);
        await fixture.Model.OpenAsync(stored.Id, TestContext.Current.CancellationToken);

        Assert.Equal(3, fixture.Model.Tabs.Count);
        Assert.Equal(stored.Id, fixture.Model.ActiveTab?.Id);
    }

    [Fact]
    public async Task EditsReachStorageAfterTheAutosavePause()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        ExperimentTabViewModel tab = fixture.Model.Tabs[0];
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        Assert.IsType<CSharpTabViewModel>(tab).Editor.OnTextEdited("typed");
        Assert.Equal("Unsaved changes", tab.SaveText);

        fixture.Time.Advance(_autosave);
        for (int attempt = 0; attempt < 200 && fixture.Experiments.Stored(tab.Id)?.Source != "typed"; attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal("typed", fixture.Experiments.Stored(tab.Id)?.Source);
    }

    [Fact]
    public async Task AnEditAfterTheWarningBringsTheWarningBack()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        CSharpTabViewModel tab = Assert.IsType<CSharpTabViewModel>(fixture.Model.Tabs[0]);
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        fixture.Experiments.Failure = new ExperimentStorageException("disk_full");
        tab.Editor.OnTextEdited("first");
        Assert.False(await fixture.Model.CloseAsync(tab, TestContext.Current.CancellationToken));

        // The user types more. The new text has never had a warning, so the first request must warn again.
        tab.Editor.OnTextEdited("second, more valuable");

        Assert.False(await fixture.Model.CloseAsync(tab, TestContext.Current.CancellationToken));
        Assert.Single(fixture.Model.Tabs);
        fixture.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await fixture.Model.CloseAsync(tab, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AWarningThatAnEarlierSuccessfulSaveClearedComesBackAfterANewFailure()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        CSharpTabViewModel tab = Assert.IsType<CSharpTabViewModel>(fixture.Model.Tabs[0]);
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        fixture.Experiments.Failure = new ExperimentStorageException("busy");
        tab.Editor.OnTextEdited("one");
        Assert.False(await fixture.Model.CloseAsync(tab, TestContext.Current.CancellationToken));
        fixture.Experiments.Failure = null;
        tab.Editor.OnTextEdited("two");
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        fixture.Experiments.Failure = new ExperimentStorageException("busy");
        tab.Editor.OnTextEdited("three");

        Assert.False(await fixture.Model.CloseAsync(tab, TestContext.Current.CancellationToken));
        Assert.Single(fixture.Model.Tabs);
    }

    [Fact]
    public async Task TwoCloseRequestsAtOnceAreNotACloseAgain()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        CSharpTabViewModel tab = Assert.IsType<CSharpTabViewModel>(fixture.Model.Tabs[0]);
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        fixture.Experiments.Failure = new ExperimentStorageException("disk_full");
        tab.Editor.OnTextEdited("precious");

        bool[] results = await Task.WhenAll(
            fixture.Model.CloseAsync(tab, TestContext.Current.CancellationToken),
            fixture.Model.CloseAsync(tab, TestContext.Current.CancellationToken));

        Assert.Equal([false, false], results);
        Assert.Single(fixture.Model.Tabs);
        Assert.Equal("precious", tab.Source);

        // A request that comes later, after the user could read the warning, discards.
        fixture.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await fixture.Model.CloseAsync(tab, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WindowCloseWarnsAgainAfterANewEdit()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        CSharpTabViewModel tab = Assert.IsType<CSharpTabViewModel>(fixture.Model.Tabs[0]);
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        fixture.Experiments.Failure = new ExperimentStorageException("disk_full");
        tab.Editor.OnTextEdited("one");
        Assert.False(await fixture.Model.TryCloseAllAsync(TestContext.Current.CancellationToken));
        fixture.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await fixture.Model.TryCloseAllAsync(TestContext.Current.CancellationToken));

        tab.Editor.OnTextEdited("two");

        Assert.False(await fixture.Model.TryCloseAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFailedReadOfTheExperimentsDoesNotReplaceTheStoredSession()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        Experiment a = fixture.Seed(OperationKind.CSharp, "A", "1");
        Experiment b = fixture.Seed(OperationKind.Json, "B", "[]");
        await fixture.Session.SaveAsync(Codepad.Application.Workspace.TabSet.Empty.OpenTab(a.Id).OpenTab(b.Id), TestContext.Current.CancellationToken);
        string before = fixture.Preferences.Values[Codepad.Application.Workspace.SessionStore.KEY];
        fixture.Experiments.ListFailure = new ExperimentStorageException("busy");

        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        await fixture.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);
        await fixture.Model.SessionSaved;

        Assert.Equal(before, fixture.Preferences.Values[Codepad.Application.Workspace.SessionStore.KEY]);
        Assert.Contains("couldn't read", fixture.Model.Notice, StringComparison.Ordinal);
        Assert.Contains("busy", fixture.Failures);
    }

    [Fact]
    public async Task RenamingTrimsStoresTheTitleAndKeepsTheSource()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        CSharpTabViewModel tab = Assert.IsType<CSharpTabViewModel>(fixture.Model.Tabs[0]);
        tab.Editor.OnTextEdited("int a = 1;");

        Assert.True(tab.Rename("  Invoice parser  "));
        await tab.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Invoice parser", tab.Title);
        Assert.Equal("Invoice parser", fixture.Experiments.Stored(tab.Id)?.Title);
        Assert.Equal("int a = 1;", fixture.Experiments.Stored(tab.Id)?.Source);
        Assert.Equal("Close Invoice parser", tab.CloseName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyTitleIsRefusedAndTheOldTitleStays(string title)
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        ExperimentTabViewModel tab = fixture.Model.Tabs[0];
        string before = tab.Title;

        Assert.False(tab.Rename(title));
        Assert.False(tab.Rename(new string('x', ExperimentTabViewModel.MaxTitleLength + 1)));

        Assert.Equal(before, tab.Title);
    }

    [Fact]
    public async Task RapidEditsDuringAFailedWriteKeepTheLatestTextAndALaterWriteSavesIt()
    {
        WorkspaceFixture fixture = new WorkspaceFixture();
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        CSharpTabViewModel tab = Assert.IsType<CSharpTabViewModel>(fixture.Model.Tabs[0]);
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        fixture.Experiments.Failure = new ExperimentStorageException("busy");

        for (int key = 1; key <= 20; key++)
        {
            tab.Editor.OnTextEdited("text " + key);
            fixture.Time.Advance(TimeSpan.FromMilliseconds(200));
        }

        await tab.FlushAsync(TestContext.Current.CancellationToken);
        fixture.RunUi();

        Assert.True(tab.HasSaveProblem);
        Assert.Equal("text 20", tab.Source);
        Assert.Equal("text 20", tab.Editor.Text);
        Assert.NotEqual("text 20", fixture.Experiments.Stored(tab.Id)?.Source);

        fixture.Experiments.Failure = null;
        tab.Editor.OnTextEdited("text 21");
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        fixture.RunUi();

        Assert.False(tab.HasSaveProblem);
        Assert.Equal("text 21", fixture.Experiments.Stored(tab.Id)?.Source);
    }

    [Fact]
    public async Task AnAutosaveFailureIsReportedOnceWithOnlyTheReasonCode()
    {
        List<string> reports = [];
        WorkspaceFixture fixture = new WorkspaceFixture(reportSaveFailure: reports.Add);
        await fixture.Model.RestoreAsync(TestContext.Current.CancellationToken);
        CSharpTabViewModel tab = Assert.IsType<CSharpTabViewModel>(fixture.Model.Tabs[0]);
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        fixture.RunUi();
        fixture.Experiments.Failure = new ExperimentStorageException("disk_full");

        tab.Editor.OnTextEdited("SECRET_TEXT");
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        fixture.RunUi();

        Assert.Equal(["disk_full"], reports);

        // A save that works ends the failure. A new failure is reported again.
        fixture.Experiments.Failure = null;
        tab.Editor.OnTextEdited("more");
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        fixture.RunUi();
        fixture.Experiments.Failure = new ExperimentStorageException("read_only");
        tab.Editor.OnTextEdited("again");
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        fixture.RunUi();

        Assert.Equal(["disk_full", "read_only"], reports);
    }
}
