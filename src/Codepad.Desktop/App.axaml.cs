using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Codepad.Application.Settings;
using Codepad.Desktop.Settings;
using Codepad.Desktop.Shell;
using Codepad.Desktop.Updates;

namespace Codepad.Desktop;

/// <summary>The options that the composition root gives to the app.</summary>
/// <param name="ViewModel">The state of the main window.</param>
/// <param name="OnOpened">Called once after the main window opens.</param>
/// <param name="ExitAfterOpen">True to close the app after the main window opens. The launch smoke test uses it.</param>
public sealed record ShellOptions(MainWindowViewModel ViewModel, Action OnOpened, bool ExitAfterOpen);

/// <summary>The Avalonia application. It loads the theme and opens the main window.</summary>
public sealed partial class App : Avalonia.Application
{
    private readonly ShellOptions? options;

    /// <summary>Initializes a new instance of the <see cref="App"/> class with no main window, for tests and the previewer.</summary>
    public App()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="App"/> class that opens the main window.</summary>
    /// <param name="options">The options from the composition root.</param>
    public App(ShellOptions options)
    {
        this.options = options;
    }

    /// <summary>Applies the theme that the user chose. "System" follows the operating system.</summary>
    /// <param name="theme">The theme.</param>
    internal void ApplyTheme(ThemePreference theme) => RequestedThemeVariant = theme switch
    {
        ThemePreference.Light => Avalonia.Styling.ThemeVariant.Light,
        ThemePreference.Dark => Avalonia.Styling.ThemeVariant.Dark,
        _ => Avalonia.Styling.ThemeVariant.Default,
    };

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (options is not null && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The tabs open before the window does. The restore starts here, after the UI thread owns its dispatcher: a
            // timer thread that touched the dispatcher earlier would take it. Task.Run keeps the wait free of the UI context.
            if (options.ViewModel.Workspace is not null)
            {
                Task.Run(() => options.ViewModel.Workspace.RestoreAsync()).GetAwaiter().GetResult();
            }

            // The saved theme applies before the window shows. A later change applies at once.
            if (options.ViewModel.Settings is not null)
            {
                ApplyTheme(options.ViewModel.Settings.Current.Theme);
                options.ViewModel.Settings.Changed += settings => ApplyTheme(settings.Theme);
            }

            MainWindow window = new MainWindow { DataContext = options.ViewModel };
            window.Opened += (_, _) =>
            {
                options.OnOpened();

                // The new version proves that it started. The update helper waits for this file, and without it the helper goes back.
                UpdateBootstrap.WriteHealthFileIfRequested();
                if (!options.ExitAfterOpen && options.ViewModel.Updates is UpdateViewModel updates && options.ViewModel.Settings is SettingsViewModel settingsModel)
                {
                    _ = updates.RunStartupAsync(() => settingsModel.UpdateChecksEnabled, TimeSpan.FromSeconds(8));
                }

                if (options.ExitAfterOpen)
                {
                    // Background priority lets the first layout and render finish before the exit.
                    Dispatcher.UIThread.Post(() => desktop.Shutdown(0), DispatcherPriority.Background);
                }
            };
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
