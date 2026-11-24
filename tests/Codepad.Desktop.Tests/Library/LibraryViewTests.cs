using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Codepad.Application.Json;
using Codepad.Application.Workspace;
using Codepad.Desktop.Editor;
using Codepad.Desktop.Json;
using Codepad.Desktop.Library;
using Codepad.Desktop.Shell;
using Codepad.Desktop.Tests.Workspace;
using Codepad.Desktop.Workspace;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Language;
using Codepad.Tests.Experiments;

namespace Codepad.Desktop.Tests.Library;

/// <summary>Headless UI tests of the library panel in the real main window.</summary>
public sealed class LibraryViewTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(20);

    private readonly FakeExperimentRepository experiments = new FakeExperimentRepository();
    private readonly MemoryPreferences preferences = new MemoryPreferences();

    [AvaloniaFact]
    public async Task TypingInTheSearchBoxNarrowsTheList()
    {
        (MainWindow window, LibraryView view, _, _) = await OpenAsync("light", 1000, 640);
        Assert.True(view.List.ItemCount >= 5);

        view.Search.Focus();
        window.KeyTextInput("invoice");
        await WaitAsync(() => view.List.ItemCount == 1);

        LibraryItem item = Assert.IsType<LibraryItem>(view.List.Items.Cast<object>().Single());
        Assert.Equal("Invoice parser", item.Title);
        Assert.Equal("1 match", Named<TextBlock>(window, "Library status").Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task NoMatchShowsAnExplanation()
    {
        (MainWindow window, LibraryView view, _, _) = await OpenAsync("light", 1000, 640);

        view.Search.Focus();
        window.KeyTextInput("zzzzzz");
        await WaitAsync(() => view.List.ItemCount == 0);

        Assert.Equal("No experiment matches “zzzzzz”.", Named<TextBlock>(window, "Library status").Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheKeyboardMovesFromTheSearchBoxToTheListOpensARowAndEscapeClears()
    {
        (MainWindow window, LibraryView view, WorkspaceViewModel workspace, LibraryViewModel model) = await OpenAsync("light", 1000, 640);
        view.Search.Focus();
        window.KeyTextInput("payload");
        await WaitAsync(() => view.List.ItemCount == 1);

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.Equal(0, view.List.SelectedIndex);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await WaitAsync(() => workspace.ActiveTab?.Title == "Payload");

        Assert.Equal(OperationKind.Json, workspace.ActiveTab?.Kind);

        view.Search.Focus();
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await WaitAsync(() => view.List.ItemCount >= 5);
        Assert.Equal(string.Empty, model.Query);
        window.Close();
    }

    [AvaloniaFact]
    public async Task OpeningAnOpenExperimentFromTheListFocusesItsTabWithoutADuplicate()
    {
        (MainWindow window, LibraryView view, WorkspaceViewModel workspace, LibraryViewModel model) = await OpenAsync("light", 1000, 640);
        LibraryItem payload = model.Items.Single(item => item.Title == "Payload");
        await model.OpenAsync(payload, TestContext.Current.CancellationToken);
        await workspace.NewExperimentAsync(OperationKind.CSharp, TestContext.Current.CancellationToken);
        int tabs = workspace.Tabs.Count;

        await Task.Delay(300, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        LibraryItem current = model.Items.Single(item => item.Title == "Payload");
        view.List.SelectedItem = current;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(current, view.List.SelectedItem);
        Assert.True(view.List.ContainerFromIndex(view.List.SelectedIndex)!.Focus());
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await WaitAsync(() => workspace.ActiveTab?.Title == "Payload");

        Assert.Equal(tabs, workspace.Tabs.Count);
        Assert.Equal(1, workspace.Tabs.Count(tab => tab.Title == "Payload"));
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheKindFilterButtonsHaveNamesAndFilter()
    {
        (MainWindow window, LibraryView view, _, _) = await OpenAsync("light", 1000, 640);

        RadioButton json = Named<RadioButton>(window, "Show JSON experiments");
        json.IsChecked = true;
        await WaitAsync(() => view.List.ItemCount == 1);

        Assert.Equal("Payload", Assert.IsType<LibraryItem>(view.List.Items.Cast<object>().Single()).Title);
        Assert.NotNull(Named<RadioButton>(window, "Show all kinds"));
        window.Close();
    }

    [AvaloniaFact]
    public async Task ARenameShowsInTheListAfterItIsSaved()
    {
        (MainWindow window, LibraryView view, WorkspaceViewModel workspace, LibraryViewModel model) = await OpenAsync("light", 1000, 640);
        ExperimentTabViewModel tab = (await workspace.OpenAsync(model.Items.Single(item => item.Title == "Payload").Id, TestContext.Current.CancellationToken))!;

        Assert.True(tab.Rename("Payload v2"));
        await tab.FlushAsync(TestContext.Current.CancellationToken);
        await WaitAsync(() => view.List.Items.Cast<LibraryItem>().Any(item => item.Title == "Payload v2"));

        Assert.DoesNotContain(view.List.Items.Cast<LibraryItem>(), item => item.Title == "Payload");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task CapturesTheLibrary(string theme)
    {
        (MainWindow window, LibraryView view, _, _) = await OpenAsync(theme, 1000, 640);
        view.Search.Focus();
        window.KeyTextInput("a");
        await WaitAsync(() => Named<TextBlock>(window, "Library status").Text?.Contains("match", StringComparison.Ordinal) == true);

        Capture(window, $"library-{theme}");
        window.Close();
    }

    private async Task<(MainWindow Window, LibraryView View, WorkspaceViewModel Workspace, LibraryViewModel Library)> OpenAsync(string theme, double width, double height, bool seed = true)
    {
        if (seed)
        {
            string[] titles = ["Invoice parser", "Payload", "Retry policy", "Date maths", "Regex scratch", "LINQ grouping", "Notes"];
            for (int index = 0; index < titles.Length; index++)
            {
                bool json = titles[index] == "Payload";
                experiments.Seed(Experiment.Create(
                    ExperimentId.New(),
                    json ? OperationKind.Json : OperationKind.CSharp,
                    titles[index],
                    json ? "{\n  \"total\": 5\n}" : $"var value = {index};\nConsole.WriteLine(value);",
                    new DateTimeOffset(2026, 11, 20 + index, 9, 30, 0, TimeSpan.Zero)));
            }
        }

        WorkspaceViewModel workspace = new WorkspaceViewModel(
            experiments,
            new SessionStore(preferences),
            TimeProvider.System,
            () => new CSharpEditorViewModel(new CSharpHighlighter(), new CSharpAnalyzer(), TimeProvider.System, TimeSpan.FromMilliseconds(30)),
            () => new JsonToolViewModel(new JsonEvaluator(), TimeProvider.System, TimeSpan.FromMilliseconds(30)));
        await workspace.RestoreAsync(TestContext.Current.CancellationToken);
        LibraryViewModel library = new LibraryViewModel(new FakeLibrary(experiments), workspace, TimeProvider.System, TimeSpan.FromMilliseconds(30));
        MainWindow window = new MainWindow
        {
            DataContext = new MainWindowViewModel([], null, null, workspace, library),
            Width = width,
            Height = height,
            RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        window.Show();
        LibraryView view = window.GetVisualDescendants().OfType<LibraryView>().Single();
        await library.RefreshAsync(TestContext.Current.CancellationToken);
        await WaitAsync(() => library.Items.Count > 0 || !seed);
        return (window, view, workspace, library);
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
