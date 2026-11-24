using Codepad.Application.Experiments;
using Codepad.Application.Library;
using Codepad.Desktop.Library;
using Codepad.Desktop.Tests.Workspace;
using Codepad.Desktop.Workspace;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;

namespace Codepad.Desktop.Tests.Library;

public sealed class LibraryViewModelTests
{
    private static readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ShowsTheRecentExperimentsWithTheirFacts()
    {
        Fixture fixture = new Fixture();
        fixture.Workspace.Seed(OperationKind.Json, "Payload", "{\n  \"a\": 1\n}");

        await fixture.RefreshAsync();

        LibraryItem item = Assert.Single(fixture.Model.Items);
        Assert.Equal("Payload", item.Title);
        Assert.Equal("JSON", item.KindText);
        Assert.Equal("{ \"a\": 1 }", item.Preview);
        Assert.Contains("Payload, JSON", item.AccessibleName, StringComparison.Ordinal);
        Assert.Equal("1 experiment", fixture.Model.StatusText);
    }

    [Fact]
    public async Task AnEmptyLibraryExplainsHowToStart()
    {
        Fixture fixture = new Fixture();

        await fixture.RefreshAsync();

        Assert.Empty(fixture.Model.Items);
        Assert.Equal(LibraryViewModel.EMPTY_LIBRARY_TEXT, fixture.Model.StatusText);
        Assert.True(fixture.Model.IsEmpty);
    }

    [Fact]
    public async Task TypingSearchesAfterAPauseAndShowsTheMatches()
    {
        Fixture fixture = new Fixture();
        fixture.Workspace.Seed(OperationKind.CSharp, "Invoice", "x");
        fixture.Workspace.Seed(OperationKind.CSharp, "Other", "y");
        await fixture.RefreshAsync();

        fixture.Model.Query = "inv";
        fixture.Workspace.Time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(2, fixture.Model.Items.Count);
        fixture.Workspace.Time.Advance(_debounce);
        await fixture.WaitAsync(() => fixture.Model.Items.Count == 1);

        Assert.Equal("Invoice", fixture.Model.Items[0].Title);
        Assert.Equal("1 match", fixture.Model.StatusText);
    }

    [Fact]
    public async Task NoMatchSaysSoAndNamesTheQuery()
    {
        Fixture fixture = new Fixture();
        fixture.Workspace.Seed(OperationKind.CSharp, "Invoice", "x");

        fixture.Model.Query = "zzz";
        fixture.Workspace.Time.Advance(_debounce);
        await fixture.WaitAsync(() => fixture.Model.StatusText.StartsWith("No experiment matches", StringComparison.Ordinal));

        Assert.Equal("No experiment matches “zzz”.", fixture.Model.StatusText);
        Assert.Empty(fixture.Model.Items);
        Assert.False(fixture.Model.IsEmpty);
    }

    [Fact]
    public async Task AKindFilterSearchesAtOnce()
    {
        Fixture fixture = new Fixture();
        fixture.Workspace.Seed(OperationKind.CSharp, "A", "x");
        fixture.Workspace.Seed(OperationKind.Json, "B", "x");

        fixture.Model.KindFilter = LibraryFilter.Json;
        await fixture.WaitAsync(() => fixture.Model.Items.Count == 1);

        Assert.Equal("B", fixture.Model.Items[0].Title);
    }

    [Fact]
    public async Task ASlowOlderSearchNeverReplacesTheNewestResult()
    {
        Fixture fixture = new Fixture();
        fixture.Workspace.Seed(OperationKind.CSharp, "Old match", "x");
        fixture.Workspace.Seed(OperationKind.CSharp, "New match", "y");
        TaskCompletionSource gate = new TaskCompletionSource();
        fixture.Library.Gate = gate;
        fixture.Model.Query = "old";
        fixture.Workspace.Time.Advance(_debounce);
        await fixture.Library.Started.WaitAsync(_timeout, TestContext.Current.CancellationToken);

        // The user types again while the first search still runs.
        fixture.Library.Gate = null;
        fixture.Model.Query = "new";
        fixture.Workspace.Time.Advance(_debounce);
        await fixture.WaitAsync(() => fixture.Model.Items.Count == 1 && fixture.Model.Items[0].Title == "New match");
        gate.SetResult();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        fixture.RunUi();

        Assert.Equal("New match", Assert.Single(fixture.Model.Items).Title);
    }

    [Fact]
    public async Task LoadMoreAddsTheNextPage()
    {
        Fixture fixture = new Fixture(pageSize: 2);
        for (int index = 0; index < 5; index++)
        {
            fixture.Workspace.Seed(OperationKind.CSharp, $"E{index}", "x");
        }

        await fixture.RefreshAsync();
        Assert.Equal(2, fixture.Model.Items.Count);
        Assert.True(fixture.Model.HasMore);

        await fixture.LoadMoreAsync();
        await fixture.LoadMoreAsync();

        Assert.Equal(5, fixture.Model.Items.Count);
        Assert.False(fixture.Model.HasMore);
        Assert.Equal(5, fixture.Model.Items.Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public async Task OpeningAnOpenExperimentFocusesItsExistingTab()
    {
        Fixture fixture = new Fixture();
        Experiment stored = fixture.Workspace.Seed(OperationKind.CSharp, "Seeded", "x");
        await fixture.Workspace.Model.RestoreAsync(TestContext.Current.CancellationToken);
        await fixture.RefreshAsync();
        LibraryItem item = fixture.Model.Items.Single(row => row.Id == stored.Id);

        await fixture.Model.OpenAsync(item, TestContext.Current.CancellationToken);
        await fixture.Workspace.Model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);
        await fixture.Model.OpenAsync(item, TestContext.Current.CancellationToken);

        Assert.Equal(stored.Id, fixture.Workspace.Model.ActiveTab?.Id);
        Assert.Equal(1, fixture.Workspace.Model.Tabs.Count(tab => tab.Id == stored.Id));
    }

    [Fact]
    public async Task OpeningAnExperimentThatIsGoneShowsTheWorkspaceNotice()
    {
        Fixture fixture = new Fixture();
        fixture.Workspace.Seed(OperationKind.CSharp, "Here", "x");
        await fixture.RefreshAsync();
        LibraryItem ghost = fixture.Model.Items[0] with { Id = ExperimentId.New() };

        await fixture.Model.OpenAsync(ghost, TestContext.Current.CancellationToken);

        Assert.Equal("That experiment no longer exists.", fixture.Workspace.Model.Notice);
    }

    [Fact]
    public async Task AFailedSearchKeepsTheListAndSaysSoWithoutLoggingTheQuery()
    {
        Fixture fixture = new Fixture();
        fixture.Workspace.Seed(OperationKind.CSharp, "Kept", "x");
        await fixture.RefreshAsync();
        fixture.Library.Failure = new ExperimentStorageException("busy");

        fixture.Model.Query = "secret words";
        fixture.Workspace.Time.Advance(_debounce);
        await fixture.WaitAsync(() => fixture.Model.StatusText == LibraryViewModel.SEARCH_FAILED_TEXT);

        Assert.Equal("Kept", Assert.Single(fixture.Model.Items).Title);
        Assert.Equal(["busy"], fixture.Failures);
    }

    [Fact]
    public async Task AnEditThatIsSavedRefreshesTheList()
    {
        Fixture fixture = new Fixture();
        await fixture.Workspace.Model.RestoreAsync(TestContext.Current.CancellationToken);
        await fixture.RefreshAsync();
        ExperimentTabViewModel tab = fixture.Workspace.Model.Tabs[0];
        Assert.Equal("New C# experiment", Assert.Single(fixture.Model.Items).Title);

        Assert.True(tab.Rename("Renamed thing"));
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        fixture.Workspace.RunUi();
        fixture.Workspace.Time.Advance(_debounce);
        await fixture.WaitAsync(() => fixture.Model.Items.Count == 1 && fixture.Model.Items[0].Title == "Renamed thing");

        Assert.Equal("Renamed thing", fixture.Model.Items[0].Title);
    }

    [Fact]
    public async Task ClearQueryShowsTheRecentListAgain()
    {
        Fixture fixture = new Fixture();
        fixture.Workspace.Seed(OperationKind.CSharp, "A", "x");
        fixture.Workspace.Seed(OperationKind.CSharp, "B", "y");
        fixture.Model.Query = "a";
        fixture.Workspace.Time.Advance(_debounce);
        await fixture.WaitAsync(() => fixture.Model.Items.Count == 1);

        fixture.Model.ClearQuery();
        await fixture.WaitAsync(() => fixture.Model.Items.Count == 2);

        Assert.Equal(string.Empty, fixture.Model.Query);
    }

    private sealed class Fixture
    {
        public Fixture(int? pageSize = null)
        {
            Workspace = new WorkspaceFixture();
            Library = new FakeLibrary(Workspace.Experiments);
            Model = new LibraryViewModel(Library, Workspace.Model, Workspace.Time, _debounce, Workspace.Post, Failures.Add, pageSize);
        }

        public WorkspaceFixture Workspace { get; }

        public FakeLibrary Library { get; }

        public LibraryViewModel Model { get; }

        public List<string> Failures { get; } = [];

        public void RunUi() => Workspace.RunUi();

        public async Task RefreshAsync()
        {
            await Model.RefreshAsync(TestContext.Current.CancellationToken);
            RunUi();
        }

        public async Task LoadMoreAsync()
        {
            await Model.LoadMoreAsync(TestContext.Current.CancellationToken);
            RunUi();
        }

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

    [Fact]
    public async Task ARefreshKeepsTheSelectedRowWhenItIsStillInTheList()
    {
        Fixture fixture = new Fixture();
        fixture.Workspace.Seed(OperationKind.CSharp, "A", "x");
        fixture.Workspace.Seed(OperationKind.CSharp, "B", "y");
        await fixture.RefreshAsync();
        LibraryItem chosen = fixture.Model.Items[1];
        fixture.Model.SelectedItem = chosen;

        await fixture.RefreshAsync();

        Assert.Equal(chosen.Id, fixture.Model.SelectedItem?.Id);
    }

    [Fact]
    public async Task ARefreshDropsTheSelectionWhenTheRowLeavesTheList()
    {
        Fixture fixture = new Fixture();
        fixture.Workspace.Seed(OperationKind.CSharp, "Keep", "x");
        fixture.Workspace.Seed(OperationKind.CSharp, "Other", "y");
        await fixture.RefreshAsync();
        fixture.Model.SelectedItem = fixture.Model.Items.Single(item => item.Title == "Other");

        fixture.Model.Query = "keep";
        await fixture.Model.RefreshAsync(TestContext.Current.CancellationToken);
        fixture.RunUi();

        Assert.Null(fixture.Model.SelectedItem);
    }

    [Fact]
    public async Task LoadMoreNeverRepeatsARowThatMovedBetweenPages()
    {
        Fixture fixture = new Fixture(pageSize: 2);
        for (int index = 0; index < 4; index++)
        {
            Experiment seeded = Experiment.Create(ExperimentId.New(), OperationKind.CSharp, $"E{index}", "x", fixture.Workspace.Time.GetUtcNow().AddMinutes(index));
            fixture.Workspace.Experiments.Seed(seeded);
        }

        await fixture.RefreshAsync();
        Assert.Equal(["E3", "E2"], fixture.Model.Items.Select(item => item.Title));

        // An edit moves E0 to the top before the user asks for more.
        Experiment e0 = (await fixture.Workspace.Experiments.ListAsync(TestContext.Current.CancellationToken)).Single(item => item.Title == "E0");
        fixture.Workspace.Experiments.Seed(e0.WithSource("edited", fixture.Workspace.Time.GetUtcNow().AddHours(1)));
        await fixture.LoadMoreAsync();

        Assert.Equal(fixture.Model.Items.Count, fixture.Model.Items.Select(item => item.Id).Distinct().Count());
    }
}
