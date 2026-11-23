using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Codepad.Desktop.Results;
using Codepad.Desktop.Shell;
using Codepad.Domain.Executions;
using Codepad.Domain.Results;
using Avalonia.Automation;

namespace Codepad.Desktop.Tests.Results;

/// <summary>Headless UI tests of the result panel inside the main window.</summary>
public sealed class ResultsViewTests
{
    private static readonly DateTimeOffset _now = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [AvaloniaFact]
    public void StartsWithTheNoRunStateAndAnExplanation()
    {
        (MainWindow window, _, _) = Open("light");

        Assert.Equal("No run yet", Text(window, "Run status"));
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == ResultsViewModel.EMPTY_RESULT_TEXT && block.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void ATableHasOneColumnForEachPropertyAndOneRowForEachObject()
    {
        (MainWindow window, ResultsViewModel model, ResultsView view) = Open("light");

        model.Show(ResultSamples.Success(ResultSamples.People(50)), _now);

        Assert.True(view.Grid.IsEffectivelyVisible);
        Assert.False(view.TreeControl.IsEffectivelyVisible);
        Assert.Equal(["Name", "Age", "City"], view.Grid.Columns.Select(column => column.Header?.ToString()));
        Assert.Equal(50, view.Grid.ItemsSource!.Cast<object>().Count());
        window.Close();
    }

    [AvaloniaFact]
    public void ATreeShowsTheRootAndAnExpandedFirstLevel()
    {
        (MainWindow window, ResultsViewModel model, ResultsView view) = Open("light");

        model.Show(ResultSamples.Success(ResultSamples.Nested()), _now);

        Assert.True(view.TreeControl.IsEffectivelyVisible);
        Assert.False(view.Grid.IsEffectivelyVisible);
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        TreeViewItem root = Assert.IsType<TreeViewItem>(view.TreeControl.ContainerFromIndex(0));
        Assert.True(root.IsExpanded);
        Assert.Equal(5, root.ItemCount);
        window.Close();
    }

    [AvaloniaFact]
    public void ATextValueIsReadOnlyAndSelectable()
    {
        (MainWindow window, ResultsViewModel model, _) = Open("light");

        model.Show(ResultSamples.Success(ResultSamples.Text("hello\nworld")), _now);

        TextBox box = Assert.IsType<TextBox>(window.GetVisualDescendants().OfType<Control>().Single(control => AutomationProperties.GetName(control) == "Result value"));
        Assert.True(box.IsReadOnly);
        Assert.Equal("hello\nworld", box.Text);
        Assert.True(box.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void ATimeoutKeepsThePreviousValueAndShowsABannerAndTheStatus()
    {
        (MainWindow window, ResultsViewModel model, _) = Open("light");
        model.Show(ResultSamples.Success(ResultSamples.Int(7)), _now);

        model.Show(ResultSamples.Report(ExecutionOutcome.TimedOut), _now.AddSeconds(11));

        Assert.Equal("Stopped: time limit reached", Text(window, "Run status"));
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text?.StartsWith("This run has no value of its own", StringComparison.Ordinal) == true && block.IsEffectivelyVisible);
        TextBox box = Assert.IsType<TextBox>(window.GetVisualDescendants().OfType<Control>().Single(control => AutomationProperties.GetName(control) == "Result value"));
        Assert.Equal("7", box.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void TheCopyButtonIsOffWithoutAValueAndOnWithOne()
    {
        (MainWindow window, ResultsViewModel model, _) = Open("light");
        Button copy = Assert.IsType<Button>(window.GetVisualDescendants().OfType<Control>().Single(control => AutomationProperties.GetName(control) == "Copy result as JSON"));
        Assert.False(copy.IsEnabled);

        model.Show(ResultSamples.Success(ResultSamples.Int(1)), _now);

        Assert.True(copy.IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void TheResultTabsAreReachableWithTheKeyboard()
    {
        (MainWindow window, _, _) = Open("light");
        TabControl tabs = Assert.IsType<TabControl>(window.GetVisualDescendants().OfType<Control>().Single(control => AutomationProperties.GetName(control) == "Run output"));
        TabItem first = Assert.IsType<TabItem>(tabs.Items[0]);

        first.Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);

        Assert.Equal(1, tabs.SelectedIndex);
        window.Close();
    }

    public static TheoryData<string, string, int, int> ScreenshotCases()
    {
        TheoryData<string, string, int, int> cases = new();
        foreach (string theme in new[] { "light", "dark" })
        {
            foreach (string state in new[] { "empty", "scalar", "tree", "table", "table-cut", "timeout", "compile-error", "runtime-error", "output" })
            {
                cases.Add(theme, state, 1000, 720);
            }

            cases.Add(theme, "table", 640, 400);
        }

        return cases;
    }

    [AvaloniaTheory]
    [MemberData(nameof(ScreenshotCases))]
    public void CapturesTheResultPanel(string theme, string state, int width, int height)
    {
        (MainWindow window, ResultsViewModel model, ResultsView view) = Open(theme, width, height);
        Prepare(model, state);
        if (state == "compile-error")
        {
            SelectTab(view, 1);
        }
        else if (state == "output")
        {
            SelectTab(view, 2);
        }
        else if (state == "runtime-error")
        {
            SelectTab(view, 3);
        }

        WriteableBitmap? frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        string folder = Path.Combine(AppContext.BaseDirectory, "screenshots", OperatingSystem.IsWindows() ? "windows" : "linux");
        Directory.CreateDirectory(folder);
        frame.Save(Path.Combine(folder, $"results-{theme}-{state}-{width}x{height}.png"), PngBitmapEncoderOptions.Default);
        window.Close();
    }

    private static void Prepare(ResultsViewModel model, string state)
    {
        switch (state)
        {
            case "scalar":
                model.Show(ResultSamples.Success(ResultSamples.Int(42), "Computing...\nDone.\n"), _now);
                break;
            case "tree":
                model.Show(ResultSamples.Success(ResultSamples.Nested()), _now);
                break;
            case "table":
                model.Show(ResultSamples.Success(ResultSamples.People(40)), _now);
                break;
            case "table-cut":
                model.Show(ResultSamples.Success(ResultSamples.People(1_000, TruncationReason.ItemLimit)), _now);
                break;
            case "timeout":
                model.Show(ResultSamples.Success(ResultSamples.People(12)), _now);
                model.Show(ResultSamples.Report(ExecutionOutcome.TimedOut), _now.AddSeconds(11));
                break;
            case "compile-error":
                model.Show(ResultSamples.Report(ExecutionOutcome.CompilationFailed, diagnostics:
                [
                    new Diagnostic(DiagnosticSeverity.Error, "CS1525", "Invalid expression term ';'", new SourceLocation(2, 9)),
                    new Diagnostic(DiagnosticSeverity.Error, "CS0103", "The name 'total' does not exist in the current context", new SourceLocation(4, 1)),
                    new Diagnostic(DiagnosticSeverity.Warning, "CS1717", "Assignment made to same variable", new SourceLocation(3, 1)),
                ]), _now);
                break;
            case "runtime-error":
                model.Show(ResultSamples.Report(ExecutionOutcome.RuntimeFailed, diagnostics: [new Diagnostic(DiagnosticSeverity.Error, "RuntimeException", "System.DivideByZeroException: Attempted to divide by zero.", null)], stderr: "Unhandled: divide by zero\n"), _now);
                break;
            case "output":
                model.Show(ResultSamples.Report(ExecutionOutcome.Success, stdout: string.Join('\n', Enumerable.Range(1, 12).Select(i => $"line {i}")) + "\n", stdoutOriginal: 2_000_000), _now);
                break;
            default:
                break;
        }
    }

    private static void SelectTab(ResultsView view, int index)
    {
        TabControl tabs = view.GetVisualDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = index;
    }

    private static (MainWindow Window, ResultsViewModel Model, ResultsView View) Open(string theme, int width = 1000, int height = 720)
    {
        MainWindowViewModel shell = new MainWindowViewModel([]);
        MainWindow window = new MainWindow
        {
            DataContext = shell,
            Width = width,
            Height = height,
            RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        window.Show();
        return (window, shell.Results, window.GetVisualDescendants().OfType<ResultsView>().Single());
    }

    private static string? Text(MainWindow window, string accessibleName) =>
        window.GetVisualDescendants().OfType<TextBlock>().Single(block => AutomationProperties.GetName(block) == accessibleName).Text;
}
