using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Codepad.Application.History;
using Codepad.Application.Json;
using Codepad.Application.Workspace;
using Codepad.Desktop.Commands;
using Codepad.Desktop.Editor;
using Codepad.Desktop.History;
using Codepad.Desktop.Json;
using Codepad.Desktop.Library;
using Codepad.Desktop.Results;
using Codepad.Desktop.Shell;
using Codepad.Desktop.Tests.History;
using Codepad.Desktop.Tests.Library;
using Codepad.Desktop.Tests.Workspace;
using Codepad.Desktop.Workspace;
using Codepad.Domain.Operations;
using Codepad.Infrastructure.Execution;
using Codepad.Language;
using Codepad.Tests.Experiments;

namespace Codepad.Desktop.Tests.Commands;

/// <summary>Headless UI tests of the command palette in the real main window.</summary>
public sealed class CommandPaletteViewTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    private readonly FakeExperimentRepository experiments = new FakeExperimentRepository();
    private readonly MemoryPreferences preferences = new MemoryPreferences();
    private readonly FakeRunner runner = new FakeRunner();
    private readonly MemoryHistory runHistory = new MemoryHistory();

    [AvaloniaFact]
    public async Task CtrlKOpensThePaletteWithTheSearchBoxFocusedAndEscapeGivesTheFocusBack()
    {
        (MainWindow window, WorkspaceViewModel workspace) = await OpenAsync("light");
        CSharpTabView tabView = window.GetVisualDescendants().OfType<CSharpTabView>().Single();
        tabView.FocusEditor();
        Dispatcher.UIThread.RunJobs();
        Avalonia.Input.IInputElement? before = window.FocusManager?.GetFocusedElement();
        Assert.NotNull(before);

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        await WaitAsync(() => window.Palette!.IsOpen && PaletteView(window).Search.IsFocused);

        Assert.True(PaletteView(window).IsEffectivelyVisible);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await WaitAsync(() => !window.Palette!.IsOpen);

        Assert.False(PaletteView(window).IsEffectivelyVisible);
        Assert.Same(before, window.FocusManager?.GetFocusedElement());
        Assert.NotNull(workspace);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TypingAPartOfANameAndPressingEnterRunsTheRightCommandWithTheKeyboardOnly()
    {
        (MainWindow window, WorkspaceViewModel workspace) = await OpenAsync("light");
        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        await WaitAsync(() => PaletteView(window).Search.IsFocused);

        window.KeyTextInput("new js");
        await WaitAsync(() => window.Palette!.Items.Count == 1);
        Assert.Equal("New JSON experiment", window.Palette!.Items[0].Label);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await WaitAsync(() => workspace.Tabs.Count == 2);

        Assert.Equal(OperationKind.Json, workspace.ActiveTab?.Kind);
        Assert.False(window.Palette.IsOpen);
        window.Close();
    }

    [AvaloniaFact]
    public async Task RunFromThePaletteRunsTheActiveCSharpTabOnceAndShowsTheResult()
    {
        (MainWindow window, WorkspaceViewModel workspace) = await OpenAsync("light");
        CSharpTabViewModel tab = Assert.IsType<CSharpTabViewModel>(workspace.ActiveTab);
        tab.Editor.OnTextEdited("1 + 1");

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        await WaitAsync(() => PaletteView(window).Search.IsFocused);
        window.KeyTextInput("run c#");
        await WaitAsync(() => window.Palette!.Items.Count > 0 && window.Palette.Items[0].Label == "Run C#");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await WaitAsync(() => workspace.Runs!.Results.StatusText == "Succeeded");

        Assert.Equal("1 + 1", Assert.Single(runner.Requests).Source);
        window.Close();
    }

    [AvaloniaFact]
    public async Task CtrlEnterRunsTheActiveCSharpTab()
    {
        (MainWindow window, WorkspaceViewModel workspace) = await OpenAsync("light");
        CSharpTabViewModel tab = Assert.IsType<CSharpTabViewModel>(workspace.ActiveTab);
        tab.Editor.OnTextEdited("2 + 2");
        window.GetVisualDescendants().OfType<CSharpTabView>().Single().FocusEditor();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
        await WaitAsync(() => workspace.Runs!.Results.StatusText == "Succeeded");

        Assert.Equal("2 + 2", Assert.Single(runner.Requests).Source);
        window.Close();
    }

    [AvaloniaFact]
    public async Task CtrlEnterOnAJsonTabRunsNothingAndSaysWhy()
    {
        (MainWindow window, WorkspaceViewModel workspace) = await OpenAsync("light");
        await workspace.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
        await WaitAsync(() => workspace.Notice.Length > 0);

        Assert.Empty(runner.Requests);
        Assert.Equal("Open a C# experiment to run it.", workspace.Notice);
        Assert.Equal("Open a C# experiment to run it.", Named<TextBlock>(window, "Tab notice").Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ADisabledCommandShowsItsReasonAndEnterDoesNotFireIt()
    {
        (MainWindow window, WorkspaceViewModel workspace) = await OpenAsync("light");
        await workspace.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);
        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        await WaitAsync(() => PaletteView(window).Search.IsFocused);

        window.KeyTextInput("run c#");
        await WaitAsync(() => window.Palette!.SelectedItem?.Label == "Run C#");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await WaitAsync(() => window.Palette!.StatusText.Contains("unavailable", StringComparison.Ordinal));

        Assert.True(window.Palette!.IsOpen);
        Assert.Empty(runner.Requests);
        Assert.Contains(PaletteView(window).GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Unavailable" && block.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ACommandThatChangesTheFocusWinsOverTheRestore()
    {
        (MainWindow window, _) = await OpenAsync("light");
        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        await WaitAsync(() => PaletteView(window).Search.IsFocused);

        window.KeyTextInput("open library");
        await WaitAsync(() => window.Palette!.Items.Count == 1);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await WaitAsync(() => window.GetVisualDescendants().OfType<LibraryView>().Single().Search.IsFocused);
        window.Close();
    }

    [AvaloniaFact]
    public async Task CtrlPFocusesTheLibrarySearchAndCtrlKIgnoresAKeyWhileOpen()
    {
        (MainWindow window, _) = await OpenAsync("light");

        window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
        await WaitAsync(() => window.GetVisualDescendants().OfType<LibraryView>().Single().Search.IsFocused);

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        await WaitAsync(() => window.Palette!.IsOpen);
        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        Assert.True(window.Palette!.IsOpen);
        window.Close();
    }

    [AvaloniaFact]
    public async Task AnEmptyResultShowsAnExplanation()
    {
        (MainWindow window, _) = await OpenAsync("light");
        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        await WaitAsync(() => PaletteView(window).Search.IsFocused);

        window.KeyTextInput("qqqq");
        await WaitAsync(() => window.Palette!.IsEmpty);

        Assert.Equal("No command matches “qqqq”.", Named<TextBlock>(window, "Command status").Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task AClickOnTheBackdropClosesThePalette()
    {
        (MainWindow window, _) = await OpenAsync("light");
        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        await WaitAsync(() => window.Palette!.IsOpen);
        Dispatcher.UIThread.RunJobs();

        window.MouseDown(new Avalonia.Point(900, 600), MouseButton.Left);
        window.MouseUp(new Avalonia.Point(900, 600), MouseButton.Left);

        await WaitAsync(() => !window.Palette!.IsOpen);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("light", "matches")]
    [InlineData("dark", "matches")]
    [InlineData("light", "disabled")]
    [InlineData("dark", "disabled")]
    [InlineData("light", "empty")]
    public async Task CapturesThePalette(string theme, string state)
    {
        (MainWindow window, WorkspaceViewModel workspace) = await OpenAsync(theme);
        if (state == "disabled")
        {
            await workspace.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);
        }

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        await WaitAsync(() => PaletteView(window).Search.IsFocused);
        if (state == "empty")
        {
            window.KeyTextInput("qqqq");
            await WaitAsync(() => window.Palette!.IsEmpty);
        }
        else if (state == "disabled")
        {
            window.KeyTextInput("r");
            await WaitAsync(() => window.Palette!.Items.Count > 0);
        }

        Dispatcher.UIThread.RunJobs();
        Capture(window, $"palette-{state}-{theme}");
        window.Close();
    }

    [AvaloniaFact]
    public async Task RunWithTheRealWorkerGivesTheValueInTheResultPanel()
    {
        WorkerLaunchOptions? options = WorkerLaunchOptions.Locate(AppContext.BaseDirectory, Path.Combine(Path.GetTempPath(), "codepad-ui-run-" + Guid.NewGuid().ToString("N")));
        Assert.NotNull(options);
        ResultsViewModel results = new ResultsViewModel();
        RunHistoryService service = new RunHistoryService(new WorkerSupervisor(options), runHistory, TimeProvider.System);
        RunController runs = new RunController(service, new RunHistoryViewModel(runHistory, service, results), results, action => Dispatcher.UIThread.Post(action), _ => { }, _ => { });
        (MainWindow window, WorkspaceViewModel workspace) = await OpenAsync("light", runs);
        CSharpTabViewModel tab = Assert.IsType<CSharpTabViewModel>(workspace.ActiveTab);
        tab.Editor.OnTextEdited("int total = 40;\ntotal + 2");

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
        await WaitAsync(() => results.StatusText == "Succeeded");

        Assert.Equal("42", results.ValueText);
        Assert.Single(runHistory.Records);
        Capture(window, "run-real-worker-light");
        window.Close();
    }

    private static CommandPaletteView PaletteView(MainWindow window) => window.GetVisualDescendants().OfType<CommandPaletteView>().Single();

    private async Task<(MainWindow Window, WorkspaceViewModel Workspace)> OpenAsync(string theme, RunController? runs = null)
    {
        if (runs is null)
        {
            ResultsViewModel results = new ResultsViewModel();
            RunHistoryService service = new RunHistoryService(runner, runHistory, TimeProvider.System);
            runs = new RunController(service, new RunHistoryViewModel(runHistory, service, results), results, action => Dispatcher.UIThread.Post(action), _ => { }, _ => { });
        }

        WorkspaceViewModel? workspace = null;
        workspace = new WorkspaceViewModel(
            experiments,
            new SessionStore(preferences),
            TimeProvider.System,
            () => new CSharpEditorViewModel(new CSharpHighlighter(), new CSharpAnalyzer(), TimeProvider.System, TimeSpan.FromMilliseconds(30)),
            () => new JsonToolViewModel(new JsonEvaluator(), TimeProvider.System, TimeSpan.FromMilliseconds(30)),
            runs: runs);
        await workspace.RestoreAsync(TestContext.Current.CancellationToken);
        LibraryViewModel library = new LibraryViewModel(new FakeLibrary(experiments), workspace, TimeProvider.System, TimeSpan.FromMilliseconds(30));
        MainWindow window = new MainWindow
        {
            DataContext = new MainWindowViewModel([], null, null, workspace, library),
            Width = 1000,
            Height = 640,
            RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, workspace);
    }

    private static void Capture(MainWindow window, string name)
    {
        WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        string folder = Path.Combine(AppContext.BaseDirectory, "screenshots", OperatingSystem.IsWindows() ? "windows" : "linux");
        Directory.CreateDirectory(folder);
        frame.Save(Path.Combine(folder, $"{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + _timeout;
        while (true)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return;
            }

            Assert.True(DateTime.UtcNow < deadline, "The condition did not become true.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static T Named<T>(Window window, string name)
        where T : Control => window.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetName(control) == name);
}
