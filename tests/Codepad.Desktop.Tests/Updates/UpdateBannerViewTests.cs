using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Codepad.Application.Updates;
using Codepad.Desktop.Shell;
using Codepad.Desktop.Updates;

namespace Codepad.Desktop.Tests.Updates;

/// <summary>Headless UI tests of the update notice in the main window: every state in words, buttons that do one step, and screenshots.</summary>
public sealed class UpdateBannerViewTests
{
    private readonly FakeSource source = new FakeSource();
    private readonly FakeVerifier verifier = new FakeVerifier();
    private readonly FakeDownloader downloader = new FakeDownloader();
    private readonly FakeInstaller installer = new FakeInstaller();
    private readonly MemoryUpdateStore store = new MemoryUpdateStore { PromptShown = true };
    private int exits;

    private (MainWindow Window, UpdateViewModel Model, UpdateService Service) Open(string theme, double width = 1000)
    {
        UpdateService service = new UpdateService(source, verifier, downloader, installer, store, TimeProvider.System, UpdateSamples.Version("0.1.0"), "linux-x64");
        UpdateViewModel model = new UpdateViewModel(service, store, UpdateSamples.Version("0.1.0"), _ => { }, () => Task.FromResult(true), () => exits++);
        MainWindow window = new MainWindow
        {
            DataContext = new MainWindowViewModel([], null, null, null, null, null, model),
            Width = width,
            Height = 420,
            RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, model, service);
    }

    private static T Named<T>(Window window, string name)
        where T : Control => window.GetVisualDescendants().OfType<T>().First(control => AutomationProperties.GetName(control) == name);

    private static bool Shown(Window window, string name) => window.GetVisualDescendants().OfType<Control>().Any(control => AutomationProperties.GetName(control) == name && control.IsEffectivelyVisible);

    private void Publish(string version = "0.2.0") => source.Next = new SignedManifest(UpdateSamples.Manifest(version), [1]);

    private static async Task SettleAsync()
    {
        for (int index = 0; index < 20; index++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static void Capture(MainWindow window, string name)
    {
        WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        string folder = Path.Combine(AppContext.BaseDirectory, "screenshots", OperatingSystem.IsWindows() ? "windows" : "linux");
        Directory.CreateDirectory(folder);
        frame.Save(Path.Combine(folder, $"{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    [AvaloniaFact]
    public void WithoutAnUpdateNoticeTheWindowShowsNoBanner()
    {
        MainWindow window = new MainWindow { DataContext = new MainWindowViewModel([]) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(window.GetVisualDescendants().OfType<UpdateBannerView>(), view => view.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void AWindowWithoutAWorkspaceShowsNoPaletteOverlay()
    {
        MainWindow window = new MainWindow { DataContext = new MainWindowViewModel([]) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Codepad.Desktop.Commands.CommandPaletteView>(), view => view.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void WhileThereIsNothingToSayTheBannerIsHidden()
    {
        (MainWindow window, _, _) = Open("light");

        Assert.False(window.GetVisualDescendants().OfType<UpdateBannerView>().Single().IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task AnAvailableUpdateShowsTwoButtonsAndTheDownloadDoesNotStartByItself()
    {
        Publish();
        (MainWindow window, UpdateViewModel model, UpdateService service) = Open("light");

        await service.CheckAsync(false, TestContext.Current.CancellationToken);
        await SettleAsync();

        Assert.True(Shown(window, "Download and install the update"));
        Assert.True(Shown(window, "Decide later"));
        Assert.False(Shown(window, "Save my work, restart CodePad and install the update"));
        Assert.Equal(UpdateState.Available, service.Status.State);
        Assert.Equal(0, installer.Calls);
        Assert.NotNull(model);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ClickingDownloadStagesTheUpdateButDoesNotRestartAndRestartNeedsItsOwnClick()
    {
        Publish();
        (MainWindow window, _, UpdateService service) = Open("light");
        await service.CheckAsync(false, TestContext.Current.CancellationToken);
        await SettleAsync();

        Named<Button>(window, "Download and install the update").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await SettleAsync();

        Assert.Equal(UpdateState.ReadyToInstall, service.Status.State);
        Assert.Equal(0, installer.Calls);
        Assert.Equal(0, exits);
        Assert.True(Shown(window, "Save my work, restart CodePad and install the update"));

        Named<Button>(window, "Save my work, restart CodePad and install the update").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await SettleAsync();

        Assert.Equal(1, installer.Calls);
        Assert.Equal(1, exits);
        window.Close();
    }

    [AvaloniaFact]
    public async Task LaterHidesTheNoticeWithAClick()
    {
        Publish();
        (MainWindow window, _, UpdateService service) = Open("light");
        await service.CheckAsync(false, TestContext.Current.CancellationToken);
        await SettleAsync();

        Named<Button>(window, "Decide later").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await SettleAsync();

        Assert.False(window.GetVisualDescendants().OfType<UpdateBannerView>().Single().IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheButtonsAreReachableWithTheKeyboard()
    {
        Publish();
        (MainWindow window, _, UpdateService service) = Open("light");
        await service.CheckAsync(false, TestContext.Current.CancellationToken);
        await SettleAsync();

        foreach (string name in new[] { "Download and install the update", "Decide later" })
        {
            Button button = Named<Button>(window, name);
            Assert.True(button.Focusable, $"{name} is not focusable.");
            Assert.True(button.Focus());
        }

        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task CapturesTheAvailableState(string theme)
    {
        Publish();
        (MainWindow window, _, UpdateService service) = Open(theme);
        await service.CheckAsync(false, TestContext.Current.CancellationToken);
        await SettleAsync();

        Capture(window, $"update-available-{theme}");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task CapturesTheDownloadingState(string theme)
    {
        Publish();
        (MainWindow window, UpdateViewModel model, UpdateService service) = Open(theme);
        await service.CheckAsync(false, TestContext.Current.CancellationToken);
        downloader.Steps = [new UpdateProgress(5_700_000, 12_900_000)];
        downloader.Hold = new TaskCompletionSource();

        Task download = model.DownloadAsync();
        await SettleAsync();
        Capture(window, $"update-downloading-{theme}");
        model.CancelDownload();
        await download;
        window.Close();
    }

    [AvaloniaFact]
    public async Task CapturesTheVerificationFailureState()
    {
        Publish();
        verifier.Valid = false;
        (MainWindow window, UpdateViewModel model, _) = Open("light");

        await model.CheckNowAsync(TestContext.Current.CancellationToken);
        await SettleAsync();

        Assert.True(Shown(window, "Close this notice"));
        Capture(window, "update-verification-failed-light");
        window.Close();
    }

    [AvaloniaFact]
    public async Task CapturesTheDigestFailureStateInDark()
    {
        Publish();
        (MainWindow window, UpdateViewModel model, UpdateService service) = Open("dark");
        await service.CheckAsync(false, TestContext.Current.CancellationToken);
        downloader.Failure = new UpdateDownloadException("digest_mismatch");

        await model.DownloadAsync();
        await SettleAsync();

        Assert.True(Shown(window, "Try the update again"));
        Capture(window, "update-digest-failed-dark");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task CapturesTheRestartState(string theme)
    {
        Publish();
        (MainWindow window, UpdateViewModel model, UpdateService service) = Open(theme);
        await service.CheckAsync(false, TestContext.Current.CancellationToken);
        await model.DownloadAsync();
        await SettleAsync();

        Capture(window, $"update-restart-{theme}");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void CapturesTheRecoveryState(string theme)
    {
        (MainWindow window, UpdateViewModel model, _) = Open(theme);

        model.ShowPreviousResult("0.1.0", "0.2.0", "rolled_back");
        Dispatcher.UIThread.RunJobs();

        Assert.True(Shown(window, "Close this notice"));
        Capture(window, $"update-recovered-{theme}");
        window.Close();
    }

    [AvaloniaFact]
    public async Task CapturesTheFirstRunExplanationAndTheManualInstallState()
    {
        store.PromptShown = false;
        (MainWindow window, UpdateViewModel model, _) = Open("light");
        await model.ShowFirstRunPromptIfNeededAsync();
        await SettleAsync();
        Assert.True(Shown(window, "Keep checking for updates once a day"));
        Capture(window, "update-first-run-light");
        window.Close();

        Publish();
        installer.Unsupported = "CodePad is installed in a folder that you cannot change. Update it the same way you installed it.";
        (MainWindow second, _, UpdateService service) = Open("light");
        await service.CheckAsync(false, TestContext.Current.CancellationToken);
        await SettleAsync();
        Assert.False(Shown(second, "Download and install the update"));
        Capture(second, "update-manual-light");
        second.Close();
    }

    [AvaloniaFact]
    public async Task ANarrowWindowKeepsTheButtonsReachable()
    {
        Publish();
        (MainWindow window, _, UpdateService service) = Open("light", width: 520);
        await service.CheckAsync(false, TestContext.Current.CancellationToken);
        await SettleAsync();

        Capture(window, "update-available-narrow-light");
        Assert.True(Shown(window, "Download and install the update"));
        window.Close();
    }
}
