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
using Codepad.Application.Settings;
using Codepad.Application.Workspace;
using Codepad.Desktop.Editor;
using Codepad.Desktop.History;
using Codepad.Desktop.Json;
using Codepad.Desktop.Library;
using Codepad.Desktop.Results;
using Codepad.Desktop.Settings;
using Codepad.Desktop.Shell;
using Codepad.Desktop.Tests.History;
using Codepad.Desktop.Tests.Library;
using Codepad.Desktop.Tests.Workspace;
using Codepad.Desktop.Workspace;
using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Infrastructure.Startup;
using Codepad.Language;
using Codepad.Tests.Experiments;

namespace Codepad.Desktop.Tests.Settings;

/// <summary>Headless UI tests of the settings window and of the shortcuts in the real main window.</summary>
public sealed class SettingsViewTests : IDisposable
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    private readonly string configFolder = Directory.CreateTempSubdirectory("codepad-ui-settings-").FullName;
    private readonly FakeExperimentRepository experiments = new FakeExperimentRepository();
    private readonly MemoryPreferences preferences = new MemoryPreferences();
    private readonly MemoryHistory runHistory = new MemoryHistory();
    private readonly FakeRunner runner = new FakeRunner();

    public void Dispose() => Directory.Delete(configFolder, recursive: true);

    [AvaloniaFact]
    public async Task ChangingTheFontSizeUpdatesTheOpenEditorsAtOnce()
    {
        (MainWindow window, WorkspaceViewModel workspace, SettingsViewModel settings) = await OpenAsync("light");
        await workspace.NewExperimentAsync(OperationKind.Json, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        CSharpEditorView csharp = window.GetVisualDescendants().OfType<CSharpTabView>().Single().GetVisualDescendants().OfType<CSharpEditorView>().Single();
        JsonToolView json = window.GetVisualDescendants().OfType<JsonToolView>().Single();
        Assert.Equal(14, csharp.TextEditor.FontSize);

        settings.FontSize = 22;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(22, csharp.TextEditor.FontSize);
        Assert.Equal(22, json.Input.FontSize);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ChangingTheIndentationWidthUpdatesTheCSharpEditor()
    {
        (MainWindow window, _, SettingsViewModel settings) = await OpenAsync("light");
        CSharpEditorView csharp = window.GetVisualDescendants().OfType<CSharpTabView>().Single().GetVisualDescendants().OfType<CSharpEditorView>().Single();

        settings.IndentationWidth = 2;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, csharp.TextEditor.Options.IndentationSize);
        Assert.True(csharp.TextEditor.Options.ConvertTabsToSpaces);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ChangingTheThemeSwitchesTheApplicationVariant()
    {
        (MainWindow window, _, SettingsViewModel settings) = await OpenAsync("light");
        App app = (App)Avalonia.Application.Current!;
        settings.Changed += changed => app.ApplyTheme(changed.Theme);

        settings.Theme = ThemePreference.Dark;
        Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);

        settings.Theme = ThemePreference.Light;
        Assert.Equal(ThemeVariant.Light, app.RequestedThemeVariant);

        settings.Theme = ThemePreference.System;
        Assert.Equal(ThemeVariant.Default, app.RequestedThemeVariant);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheSettingsWindowShowsTheValuesAndTypingAnInvalidNumberShowsAMessage()
    {
        (MainWindow window, _, SettingsViewModel settings) = await OpenAsync("light");
        SettingsWindow dialog = new SettingsWindow { DataContext = settings, Width = 560, Height = 640 };
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(14m, Named<NumericUpDown>(dialog, "Editor font size").Value);
        Assert.Equal(10m, Named<NumericUpDown>(dialog, "Run timeout").Value);

        settings.FontSizeInput = 99m;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(14, settings.Current.EditorFontSize);
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), block => block.Text?.Contains("from 8 to 48", StringComparison.Ordinal) == true && block.IsEffectivelyVisible);
        dialog.Close();
        window.Close();
    }

    [AvaloniaFact]
    public async Task EveryControlOfTheSettingsWindowHasAnAccessibleNameAndIsReachableByKeyboard()
    {
        (MainWindow window, _, SettingsViewModel settings) = await OpenAsync("light");
        SettingsWindow dialog = new SettingsWindow { DataContext = settings };
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        foreach (string name in new[] { "Theme", "Editor font size", "Indentation width", "Run timeout", "Saved runs for each experiment", "Clear run history", "Check for updates", "Check for updates now", "Reset all settings to the defaults", "Close settings" })
        {
            Control control = dialog.GetVisualDescendants().OfType<Control>().First(item => AutomationProperties.GetName(item) == name);
            Assert.True(control.Focusable || control is NumericUpDown, $"{name} is not focusable.");
        }

        Assert.False(Named<Button>(dialog, "Check for updates now").IsEnabled);
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), block => block.Text?.Contains("later version", StringComparison.Ordinal) == true);
        dialog.Close();
        window.Close();
    }

    [AvaloniaFact]
    public async Task EscapeClosesTheSettingsWindow()
    {
        (MainWindow window, _, SettingsViewModel settings) = await OpenAsync("light");
        SettingsWindow dialog = new SettingsWindow { DataContext = settings };
        bool closed = false;
        dialog.Closed += (_, _) => closed = true;
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        await WaitAsync(() => closed);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ClearingTheHistoryNeedsAConfirmationAndKeepsTheExperiments()
    {
        (MainWindow window, WorkspaceViewModel workspace, SettingsViewModel settings) = await OpenAsync("light");
        ExperimentTabViewModel tab = workspace.Tabs[0];
        runHistory.Seed(tab.CurrentExperiment, ExecutionOutcome.Success, 1);
        runHistory.Seed(tab.CurrentExperiment, ExecutionOutcome.Success, 2);
        SettingsWindow dialog = new SettingsWindow { DataContext = settings };
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        // The confirmation window opens first. Cancel is the default and keeps the runs.
        ConfirmWindow? shown = null;
        bool answerYes = false;
        dialog.ConfirmRunner = async confirm =>
        {
            shown = confirm;
            confirm.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, runHistory.Records.Count);
            Assert.True(confirm.Cancel.IsDefault);
            Assert.Contains("experiments and their source stay", confirm.GetVisualDescendants().OfType<TextBlock>().Single(block => AutomationProperties.GetName(block) == "Confirmation message").Text, StringComparison.Ordinal);
            (answerYes ? confirm.Confirm : confirm.Cancel).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Task.CompletedTask;
            return confirm.Outcome == true;
        };

        Named<Button>(dialog, "Clear run history").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await WaitAsync(() => shown is not null);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, runHistory.Records.Count);

        answerYes = true;
        shown = null;
        Named<Button>(dialog, "Clear run history").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await WaitAsync(() => runHistory.Records.Count == 0);

        Assert.NotNull(experiments.Stored(tab.Id));
        Assert.Contains("unchanged", settings.HistoryStatus, StringComparison.Ordinal);
        dialog.Close();
        window.Close();
    }

    [AvaloniaFact]
    public async Task ShortcutsWorkFromTheEditorAndFromTheLibraryList()
    {
        (MainWindow window, WorkspaceViewModel workspace, _) = await OpenAsync("light");
        window.GetVisualDescendants().OfType<CSharpTabView>().Single().FocusEditor();
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.Control);
        await WaitAsync(() => workspace.Tabs.Count == 2);
        Assert.Equal(OperationKind.CSharp, workspace.ActiveTab?.Kind);

        window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Control);
        await WaitAsync(() => workspace.Tabs.Count == 1);

        LibraryView library = window.GetVisualDescendants().OfType<LibraryView>().Single();
        library.Search.Focus();
        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        await WaitAsync(() => window.Palette!.IsOpen);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await WaitAsync(() => !window.Palette!.IsOpen);

        // Editing keys stay with the text box: Ctrl+A selects all text and does nothing else.
        library.Search.Text = "abc";
        library.Search.Focus();
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Assert.Equal("abc", library.Search.Text);
        Assert.Single(workspace.Tabs);
        window.Close();
    }

    [AvaloniaFact]
    public async Task CtrlCommaOpensTheSettingsAndThePaletteListsItAsAvailable()
    {
        (MainWindow window, _, SettingsViewModel settings) = await OpenAsync("light");

        SettingsWindow? opened = null;
        window.SettingsRunner = dialog =>
        {
            opened = dialog;
            return Task.CompletedTask;
        };

        window.KeyPressQwerty(PhysicalKey.Comma, RawInputModifiers.Control);
        await WaitAsync(() => opened is not null);
        Assert.Same(settings, opened!.DataContext);

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        await WaitAsync(() => window.Palette!.IsOpen);
        window.Palette!.Query = "settings";
        Assert.True(window.Palette.Items.Single().IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public async Task SettingsSurviveARestartThroughTheRealFileStore()
    {
        JsonUserSettingsStore store = JsonUserSettingsStore.InFolder(configFolder);
        SettingsViewModel first = new SettingsViewModel(store, UserSettings.Default);

        first.FontSize = 19;
        first.Theme = ThemePreference.Dark;
        first.RunTimeoutSeconds = 25;

        SettingsViewModel second = new SettingsViewModel(JsonUserSettingsStore.InFolder(configFolder), JsonUserSettingsStore.InFolder(configFolder).Read().Settings);

        Assert.Equal(19, second.FontSize);
        Assert.Equal(ThemePreference.Dark, second.Theme);
        Assert.Equal(25, second.RunTimeoutSeconds);
        Assert.Equal(19, second.Preferences.FontSize);
        await Task.CompletedTask;
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task CapturesTheSettingsWindow(string theme)
    {
        (MainWindow window, _, SettingsViewModel settings) = await OpenAsync(theme);
        SettingsWindow dialog = new SettingsWindow { DataContext = settings, Width = 560, Height = 760, RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light };
        dialog.Show();
        settings.FontSizeInput = 99m;
        Dispatcher.UIThread.RunJobs();

        WriteableBitmap? frame = dialog.CaptureRenderedFrame();
        Assert.NotNull(frame);
        string folder = Path.Combine(AppContext.BaseDirectory, "screenshots", OperatingSystem.IsWindows() ? "windows" : "linux");
        Directory.CreateDirectory(folder);
        frame.Save(Path.Combine(folder, $"settings-{theme}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        dialog.Close();
        window.Close();
    }

    private async Task<(MainWindow Window, WorkspaceViewModel Workspace, SettingsViewModel Settings)> OpenAsync(string theme)
    {
        SettingsViewModel settings = new SettingsViewModel(JsonUserSettingsStore.InFolder(configFolder), UserSettings.Default, runHistory);
        ResultsViewModel results = new ResultsViewModel();
        RunGate gate = new RunGate();
        RunHistoryService service = new RunHistoryService(runner, runHistory, TimeProvider.System, () => settings.Current.CSharpPolicy);
        RunController runs = new RunController(service, new RunHistoryViewModel(runHistory, service, results, gate), results, action => Dispatcher.UIThread.Post(action), _ => { }, _ => { }, null, gate);
        WorkspaceViewModel workspace = new WorkspaceViewModel(
            experiments,
            new SessionStore(preferences),
            TimeProvider.System,
            () => new CSharpEditorViewModel(new CSharpHighlighter(), new CSharpAnalyzer(), TimeProvider.System, TimeSpan.FromMilliseconds(30), preferences: settings.Preferences),
            () => new JsonToolViewModel(new JsonEvaluator(), TimeProvider.System, TimeSpan.FromMilliseconds(30), preferences: settings.Preferences),
            runs: runs);
        await workspace.RestoreAsync(TestContext.Current.CancellationToken);
        LibraryViewModel library = new LibraryViewModel(new FakeLibrary(experiments), workspace, TimeProvider.System, TimeSpan.FromMilliseconds(30));
        MainWindow window = new MainWindow
        {
            DataContext = new MainWindowViewModel([], null, null, workspace, library, settings),
            Width = 1000,
            Height = 640,
            RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, workspace, settings);
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
        where T : Control => window.GetVisualDescendants().OfType<T>().First(control => AutomationProperties.GetName(control) == name);
}
