using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Codepad.Application.Experiments;
using Codepad.Application.Json;
using Codepad.Application.Workspace;
using Codepad.Desktop.Editor;
using Codepad.Desktop.Json;
using Codepad.Desktop.Shell;
using Codepad.Desktop.Workspace;
using Codepad.Domain.Operations;
using Codepad.Language;
using Codepad.Tests.Experiments;

namespace Codepad.Desktop.Tests.Workspace;

/// <summary>Headless UI tests of the tab strip and the tab views in the real main window.</summary>
public sealed class WorkspaceViewTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(20);

    private readonly FakeExperimentRepository experiments = new FakeExperimentRepository();
    private readonly MemoryPreferences preferences = new MemoryPreferences();

    [AvaloniaFact]
    public async Task SwitchingBetweenACSharpAndAJsonTabKeepsEachEditorTheCaretAndTheScroll()
    {
        (MainWindow window, WorkspaceViewModel model) = await OpenAsync("light", 1000, 640);
        CSharpTabViewModel csharp = Assert.IsType<CSharpTabViewModel>(model.ActiveTab);
        Dispatcher.UIThread.RunJobs();
        CSharpEditorView csharpView = window.GetVisualDescendants().OfType<CSharpTabView>().Single().GetVisualDescendants().OfType<CSharpEditorView>().Single();
        csharpView.TextEditor.Text = string.Join('\n', Enumerable.Range(1, 200).Select(line => $"int v{line} = {line};"));
        Dispatcher.UIThread.RunJobs();
        csharpView.TextEditor.CaretOffset = 40;
        csharpView.TextEditor.ScrollToVerticalOffset(300);
        Dispatcher.UIThread.RunJobs();
        double scrolled = csharpView.TextEditor.VerticalOffset;

        JsonTabViewModel json = Assert.IsType<JsonTabViewModel>(await model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken));
        Dispatcher.UIThread.RunJobs();
        JsonToolView jsonView = window.GetVisualDescendants().OfType<JsonToolView>().Single();
        jsonView.Input.Text = "{\"a\":1}";
        jsonView.Input.CaretIndex = 3;
        Dispatcher.UIThread.RunJobs();
        Assert.False(csharpView.IsEffectivelyVisible);

        model.Activate(csharp);
        Dispatcher.UIThread.RunJobs();
        Assert.True(csharpView.IsEffectivelyVisible);
        Assert.False(jsonView.IsEffectivelyVisible);
        Assert.Equal(40, csharpView.TextEditor.CaretOffset);
        Assert.Equal(scrolled, csharpView.TextEditor.VerticalOffset);
        Assert.Contains("int v1 = 1;", csharp.Source, StringComparison.Ordinal);

        model.Activate(json);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("{\"a\":1}", jsonView.Input.Text);
        Assert.Equal(3, jsonView.Input.CaretIndex);
        Assert.Equal("{\"a\":1}", json.Source);
        window.Close();
    }

    [AvaloniaFact]
    public async Task CtrlTabAndCtrlShiftTabCycleAndCtrlWClosesTheActiveTab()
    {
        (MainWindow window, WorkspaceViewModel model) = await OpenAsync("light", 1000, 640);
        ExperimentTabViewModel first = model.Tabs[0];
        ExperimentTabViewModel second = await model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(second, model.ActiveTab);

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        Assert.Same(first, model.ActiveTab);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Same(second, model.ActiveTab);

        window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Control);
        await WaitAsync(() => model.Tabs.Count == 1);

        Assert.Same(first, model.ActiveTab);
        Assert.NotNull(experiments.Stored(second.Id));
        window.Close();
    }

    [AvaloniaFact]
    public async Task CtrlShiftPageDownMovesTheActiveTabRight()
    {
        (MainWindow window, WorkspaceViewModel model) = await OpenAsync("light", 1000, 640);
        ExperimentTabViewModel first = model.Tabs[0];
        ExperimentTabViewModel second = await model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);
        model.Activate(first);

        window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.Control | RawInputModifiers.Shift);

        Assert.Equal([second, first], model.Tabs);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheCloseButtonClosesATabAndKeepsTheSavedExperiment()
    {
        (MainWindow window, WorkspaceViewModel model) = await OpenAsync("light", 1000, 640);
        ExperimentTabViewModel second = await model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();

        Button close = window.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == second.CloseName);
        close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await WaitAsync(() => model.Tabs.Count == 1);

        Assert.NotNull(experiments.Stored(second.Id));
        window.Close();
    }

    [AvaloniaFact]
    public async Task DraggingATabWithTheMouseMovesIt()
    {
        (MainWindow window, WorkspaceViewModel model) = await OpenAsync("light", 1000, 640);
        ExperimentTabViewModel first = model.Tabs[0];
        ExperimentTabViewModel second = await model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);
        await model.NewExperimentAsync(OperationKind.CSharp, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        TabStripView strip = window.GetVisualDescendants().OfType<TabStripView>().Single();
        List<ListBoxItem> items = [.. strip.List.GetVisualDescendants().OfType<ListBoxItem>()];
        Point from = Center(items[0], window);
        Point to = Center(items[2], window);

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(first, model.Tabs[2]);
        Assert.Equal(second, model.Tabs[0]);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ASaveFailureShowsTheWordOnTheAffectedTabAndTheReasonInTheStatusRegion()
    {
        (MainWindow window, WorkspaceViewModel model) = await OpenAsync("light", 1000, 640);
        CSharpTabViewModel broken = Assert.IsType<CSharpTabViewModel>(model.ActiveTab);
        ExperimentTabViewModel other = await model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);
        model.Activate(broken);
        experiments.Failure = new ExperimentStorageException("disk_full");

        broken.Editor.OnTextEdited("int precious = 1;");
        await broken.FlushAsync(TestContext.Current.CancellationToken);
        await WaitAsync(() => broken.HasSaveProblem && Dispatcher.UIThread.CheckAccess());

        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Save failed" && block.IsEffectivelyVisible);
        Assert.Contains("The disk is full", Named<TextBlock>(window, "Save status").Text, StringComparison.Ordinal);
        Assert.Equal(1, window.GetVisualDescendants().OfType<TextBlock>().Count(block => block.Text == "Save failed"));
        Assert.False(other.HasSaveProblem);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ClosingTheWindowSavesTheUnsavedTextFirst()
    {
        (MainWindow window, WorkspaceViewModel model) = await OpenAsync("light", 1000, 640);
        CSharpTabViewModel tab = Assert.IsType<CSharpTabViewModel>(model.ActiveTab);
        tab.Editor.OnTextEdited("int last = 1;");
        bool closed = false;
        window.Closed += (_, _) => closed = true;

        window.Close();
        await WaitAsync(() => closed);

        Assert.Equal("int last = 1;", experiments.Stored(tab.Id)?.Source);
        Assert.Contains(tab.Id.ToString(), preferences.Values[SessionStore.KEY], StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AWindowWithAFailedSaveStaysOpenOnTheFirstCloseAndClosesOnTheSecond()
    {
        (MainWindow window, WorkspaceViewModel model) = await OpenAsync("light", 1000, 640);
        CSharpTabViewModel tab = Assert.IsType<CSharpTabViewModel>(model.ActiveTab);
        experiments.Failure = new ExperimentStorageException("disk_full");
        tab.Editor.OnTextEdited("int unsaved = 1;");
        bool closed = false;
        window.Closed += (_, _) => closed = true;

        window.Close();
        await WaitAsync(() => model.Notice.Length > 0);
        Assert.False(closed);
        Assert.Contains("Close CodePad again", Named<TextBlock>(window, "Tab notice").Text, StringComparison.Ordinal);

        // The second request must come after the user could read the warning.
        await Task.Delay(1100, TestContext.Current.CancellationToken);
        window.Close();
        await WaitAsync(() => closed);
    }

    [AvaloniaFact]
    public async Task ManyLongTabsInANarrowWindowScrollAndTheActiveTabStaysReachable()
    {
        (MainWindow window, WorkspaceViewModel model) = await OpenAsync("light", 560, 520);
        for (int count = 0; count < 7; count++)
        {
            await model.NewExperimentAsync(count % 2 == 0 ? OperationKind.Json : OperationKind.CSharp, TestContext.Current.CancellationToken);
        }

        Dispatcher.UIThread.RunJobs();
        TabStripView strip = window.GetVisualDescendants().OfType<TabStripView>().Single();
        ScrollViewer scroller = strip.List.GetVisualDescendants().OfType<ScrollViewer>().First();

        Assert.True(scroller.Extent.Width > scroller.Viewport.Width);
        model.Activate(model.Tabs[0]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, scroller.Offset.X);
        model.Activate(model.Tabs[^1]);
        Dispatcher.UIThread.RunJobs();
        Assert.True(scroller.Offset.X > 0);
        Capture(window, "tabs-overflow-light");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task CapturesTheWorkspace(string theme)
    {
        (MainWindow window, WorkspaceViewModel model) = await OpenAsync(theme, 1000, 640);
        CSharpTabViewModel csharp = Assert.IsType<CSharpTabViewModel>(model.ActiveTab);
        csharp.Editor.Load("var total = 40 + 2;\ntotal");
        JsonTabViewModel json = Assert.IsType<JsonTabViewModel>(await model.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken));
        json.Tool.Load("{\"name\":\"CodePad\",\"tags\":[\"a\",\"b\"]}");
        await model.NewExperimentAsync(OperationKind.CSharp, TestContext.Current.CancellationToken);
        experiments.Failure = new ExperimentStorageException("disk_full");
        csharp.Editor.OnTextEdited("var total = 40 + 3;\ntotal");
        await csharp.FlushAsync(TestContext.Current.CancellationToken);
        model.Activate(json);
        await WaitAsync(() => json.Tool.FormattedText.Length > 0 && csharp.HasSaveProblem);

        Capture(window, $"tabs-{theme}");
        window.Close();
    }

    private async Task<(MainWindow Window, WorkspaceViewModel Model)> OpenAsync(string theme, double width, double height)
    {
        WorkspaceViewModel model = new WorkspaceViewModel(
            experiments,
            new SessionStore(preferences),
            TimeProvider.System,
            () => new CSharpEditorViewModel(new CSharpHighlighter(), new CSharpAnalyzer(), TimeProvider.System, TimeSpan.FromMilliseconds(30)),
            () => new JsonToolViewModel(new JsonEvaluator(), TimeProvider.System, TimeSpan.FromMilliseconds(30)));
        await model.RestoreAsync(TestContext.Current.CancellationToken);
        MainWindow window = new MainWindow
        {
            DataContext = new MainWindowViewModel([], null, null, model),
            Width = width,
            Height = height,
            RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, model);
    }

    private static Point Center(Control control, Visual relativeTo)
    {
        Rect bounds = control.Bounds;
        return control.TranslatePoint(new Point(bounds.Width / 2, bounds.Height / 2), relativeTo) ?? default;
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
