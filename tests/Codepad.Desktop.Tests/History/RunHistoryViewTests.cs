using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Codepad.Application.History;
using Codepad.Desktop.History;
using Codepad.Desktop.Results;
using Codepad.Desktop.Shell;
using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;

namespace Codepad.Desktop.Tests.History;

/// <summary>Headless UI tests of the history panel inside the main window.</summary>
public sealed class RunHistoryViewTests
{
    private readonly MemoryHistory store = new MemoryHistory();
    private readonly FakeRunner runner = new FakeRunner();
    private readonly Experiment experiment = Experiment.Create(ExperimentId.New(), OperationKind.CSharp, "Scratch", "var total = 40 + 2;\ntotal", MemoryHistory.Start);

    [AvaloniaFact]
    public void WithoutHistoryThePanelIsHidden()
    {
        MainWindow window = new MainWindow { DataContext = new MainWindowViewModel([]) };
        window.Show();

        Assert.False(window.GetVisualDescendants().OfType<RunHistoryView>().Single().IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task SelectingARunShowsItsSavedSourceAndRerunNeedsTheButton()
    {
        (MainWindow window, RunHistoryViewModel model, RunHistoryView view) = await OpenAsync("light");
        ListBox list = view.List;

        list.SelectedIndex = 0;
        await Settle(model, () => model.IsInspecting);

        Assert.Empty(runner.Requests);
        TextBox source = Named<TextBox>(window, "Saved source");
        Assert.True(source.IsReadOnly);
        Assert.Contains("total", source.Text, StringComparison.Ordinal);
        Button rerun = Named<Button>(window, "Rerun the saved code");
        Assert.True(rerun.IsEffectivelyEnabled);

        rerun.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Settle(model, () => runner.Requests.Count == 1 && model.Items.Count == 3);

        Assert.Equal(3, model.Items.Count);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheRerunButtonIsReachableWithTheKeyboard()
    {
        (MainWindow window, RunHistoryViewModel model, RunHistoryView view) = await OpenAsync("light");
        view.List.SelectedIndex = 0;
        await Settle(model, () => model.IsInspecting);

        Button rerun = Named<Button>(window, "Rerun the saved code");

        Assert.True(rerun.Focusable);
        Assert.True(rerun.Focus());
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task CapturesTheHistoryPanel(string theme)
    {
        (MainWindow window, RunHistoryViewModel model, RunHistoryView view) = await OpenAsync(theme);
        view.List.SelectedIndex = 0;
        await Settle(model, () => model.IsInspecting);

        WriteableBitmap? frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        string folder = Path.Combine(AppContext.BaseDirectory, "screenshots", OperatingSystem.IsWindows() ? "windows" : "linux");
        Directory.CreateDirectory(folder);
        frame.Save(Path.Combine(folder, $"history-{theme}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        window.Close();
    }

    private async Task<(MainWindow Window, RunHistoryViewModel Model, RunHistoryView View)> OpenAsync(string theme)
    {
        store.Seed(experiment, ExecutionOutcome.RuntimeFailed, 1);
        store.Seed(experiment, ExecutionOutcome.Success, 2);
        ResultsViewModel results = new ResultsViewModel();
        RunHistoryViewModel model = new RunHistoryViewModel(store, new RunHistoryService(runner, store, TimeProvider.System), results);
        await model.ShowExperimentAsync(experiment.Id, TestContext.Current.CancellationToken);
        MainWindow window = new MainWindow
        {
            DataContext = new MainWindowViewModel([], null, model),
            Width = 1100,
            Height = 720,
            RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        window.Show();
        return (window, model, window.GetVisualDescendants().OfType<RunHistoryView>().Single());
    }

    private static async Task Settle(RunHistoryViewModel model, Func<bool> done)
    {
        for (int attempt = 0; attempt < 200 && !done(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Dispatcher.UIThread.RunJobs();
        Assert.True(done());
        Assert.NotNull(model);
    }

    private static T Named<T>(MainWindow window, string name)
        where T : Control => window.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetName(control) == name);
}
