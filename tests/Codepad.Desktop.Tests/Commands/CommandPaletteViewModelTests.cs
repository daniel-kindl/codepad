using Codepad.Application.Commands;
using Codepad.Desktop.Commands;

namespace Codepad.Desktop.Tests.Commands;

public sealed class CommandPaletteViewModelTests
{
    private int runs;
    private bool runEnabled = true;

    private AppCommand Run() => new AppCommand("run", "Run C#", () => runEnabled ? CommandAvailability.Available : new CommandAvailability(false, "Open a C# experiment to run it."), () => { runs++; return Task.CompletedTask; }, "Ctrl+Enter");

    private static AppCommand Simple(string id, string label, Func<Task>? execute = null) => new AppCommand(id, label, () => CommandAvailability.Available, execute ?? (() => Task.CompletedTask));

    private CommandPaletteViewModel Create(params AppCommand[] extra) => new CommandPaletteViewModel(() => [Run(), Simple("new", "New C# experiment"), .. extra]);

    [Fact]
    public void ItIsClosedAtFirstAndOpenListsEveryCommandWithTheFirstSelected()
    {
        CommandPaletteViewModel palette = Create();
        Assert.False(palette.IsOpen);

        palette.Open();

        Assert.True(palette.IsOpen);
        Assert.Equal(["Run C#", "New C# experiment"], palette.Items.Select(item => item.Label));
        Assert.Equal(0, palette.SelectedIndex);
        Assert.Equal("2 commands", palette.StatusText);
        Assert.Equal("Ctrl+Enter", palette.Items[0].KeyHint);
    }

    [Fact]
    public void TypingFiltersAndSelectsTheBestMatch()
    {
        CommandPaletteViewModel palette = Create();
        palette.Open();

        palette.Query = "new";

        Assert.Equal(["New C# experiment"], palette.Items.Select(item => item.Label));
        Assert.Equal(0, palette.SelectedIndex);
        Assert.Equal("1 command", palette.StatusText);
    }

    [Fact]
    public void NoMatchShowsAnEmptyStateAndSelectsNothing()
    {
        CommandPaletteViewModel palette = Create();
        palette.Open();

        palette.Query = "zzzz";

        Assert.Empty(palette.Items);
        Assert.Equal(-1, palette.SelectedIndex);
        Assert.Equal("No command matches “zzzz”.", palette.StatusText);
        Assert.True(palette.IsEmpty);
    }

    [Fact]
    public async Task InvokeRunsTheSelectedCommandOnceAndClosesThePalette()
    {
        CommandPaletteViewModel palette = Create();
        palette.Open();
        palette.Query = "run";

        bool first = await palette.InvokeAsync();
        bool second = await palette.InvokeAsync();

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, runs);
        Assert.False(palette.IsOpen);
    }

    [Fact]
    public async Task ADisabledCommandIsListedWithItsReasonAndNeverFires()
    {
        runEnabled = false;
        CommandPaletteViewModel palette = Create();
        palette.Open();
        palette.Query = "run";

        PaletteItem item = Assert.Single(palette.Items);
        Assert.False(item.IsEnabled);
        Assert.Equal("Open a C# experiment to run it.", item.Reason);
        Assert.Contains("unavailable", item.AccessibleName, StringComparison.Ordinal);
        Assert.Equal("Open a C# experiment to run it.", palette.StatusText);

        bool invoked = await palette.InvokeAsync();

        Assert.False(invoked);
        Assert.Equal(0, runs);
        Assert.True(palette.IsOpen);
        Assert.Equal("Run C# is unavailable. Open a C# experiment to run it.", palette.StatusText);
    }

    [Fact]
    public async Task ACommandThatBecameDisabledAfterTheListWasShownDoesNotFire()
    {
        CommandPaletteViewModel palette = Create();
        palette.Open();
        palette.Query = "run";
        runEnabled = false;

        bool invoked = await palette.InvokeAsync();

        Assert.False(invoked);
        Assert.Equal(0, runs);
        Assert.True(palette.IsOpen);
    }

    [Fact]
    public void UpAndDownMoveTheSelectionAndStopAtTheEnds()
    {
        CommandPaletteViewModel palette = Create(Simple("c", "Third"));
        palette.Open();

        palette.MoveSelection(1);
        palette.MoveSelection(1);
        palette.MoveSelection(1);
        Assert.Equal(2, palette.SelectedIndex);

        palette.MoveSelection(-5);
        Assert.Equal(0, palette.SelectedIndex);
    }

    [Fact]
    public async Task OpeningAgainStartsWithAnEmptyQueryAndANewList()
    {
        CommandPaletteViewModel palette = Create();
        palette.Open();
        palette.Query = "zzz";
        palette.Close();

        palette.Open();

        Assert.Equal(string.Empty, palette.Query);
        Assert.Equal(2, palette.Items.Count);
        await Task.CompletedTask;
    }

    [Fact]
    public void CloseRaisesClosedOnceAndAClosedPaletteStaysClosed()
    {
        CommandPaletteViewModel palette = Create();
        int closed = 0;
        palette.Closed += () => closed++;
        palette.Open();

        palette.Close();
        palette.Close();

        Assert.Equal(1, closed);
        Assert.False(palette.IsOpen);
    }

    [Fact]
    public async Task AFailingHandlerClosesThePaletteAndReportsOnlyTheType()
    {
        List<string> failures = [];
        CommandPaletteViewModel palette = new CommandPaletteViewModel(() => [Simple("bad", "Bad", () => throw new InvalidOperationException("secret path"))], failures.Add);
        palette.Open();

        bool invoked = await palette.InvokeAsync();

        Assert.True(invoked);
        Assert.Equal([nameof(InvalidOperationException)], failures);
        Assert.False(palette.IsOpen);
    }

    [Fact]
    public async Task InvokeWithNothingSelectedDoesNothing()
    {
        CommandPaletteViewModel palette = Create();
        palette.Open();
        palette.Query = "zzzz";

        Assert.False(await palette.InvokeAsync());
        Assert.True(palette.IsOpen);
    }

    [Fact]
    public void OpenWhileOpenKeepsTheQuery()
    {
        CommandPaletteViewModel palette = Create();
        palette.Open();
        palette.Query = "new";

        palette.Open();

        Assert.Equal("new", palette.Query);
    }
}
