using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Codepad.Desktop.Shell;

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

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (options is not null && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = new MainWindow { DataContext = options.ViewModel };
            window.Opened += (_, _) =>
            {
                options.OnOpened();
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
