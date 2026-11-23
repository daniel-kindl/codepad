using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Codepad.Application.Startup;
using Codepad.Desktop.Shell;
using Codepad.Infrastructure.Startup;
using Microsoft.Extensions.Logging;

namespace Codepad.Desktop;

/// <summary>The composition root. It creates the services and starts the Avalonia app.</summary>
internal static partial class Program
{
    /// <summary>Opens the window, then closes the app with exit code 0. CI uses it as the launch smoke test.</summary>
    internal const string SMOKE_TEST_ARGUMENT = "--smoke-test";

    [STAThread]
    public static int Main(string[] args)
    {
        long startedAt = Stopwatch.GetTimestamp();

        AppFolderPaths folders = AppFolderLocator.Locate();
        StartupReport report = new StartupService(JsonSettingsStore.InFolder(folders.ConfigFolder), new FileSystemDataFolder(folders.DataFolder)).Prepare();

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(report.Settings.MinimumLogLevel)
            .AddJsonConsole());
        ILogger logger = loggerFactory.CreateLogger("Codepad.Desktop");

        foreach (StartupNotice notice in report.Notices)
        {
            LogStartupDegraded(logger, notice.Kind, notice.Reason);
        }

        string version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        ShellOptions options = new ShellOptions(
            new MainWindowViewModel(report.Notices),
            () =>
            {
                long startupMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                LogShellOpened(logger, version, startupMs);
            },
            ExitAfterOpen: args.Contains(SMOKE_TEST_ARGUMENT, StringComparer.Ordinal));

        try
        {
            return Configure(new App(options)).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            // Log only the type. The message and the stack can hold personal paths. The runtime
            // still prints the full exception to standard error after the rethrow.
            LogShellCrashed(logger, exception.GetType().FullName ?? exception.GetType().Name);
            throw;
        }
    }

    /// <summary>Builds the app for the Avalonia previewer. Do not remove: the IDE tools call it by name.</summary>
    /// <returns>The app builder.</returns>
    public static AppBuilder BuildAvaloniaApp() => Configure(new App());

    private static AppBuilder Configure(App app) => AppBuilder.Configure(() => app)
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();

    [LoggerMessage(EventId = 1, EventName = "startup.degraded", Level = LogLevel.Warning,
        Message = "Startup recovered from a problem: {NoticeKind}, reason {Reason}")]
    private static partial void LogStartupDegraded(ILogger logger, StartupNoticeKind noticeKind, string reason);

    [LoggerMessage(EventId = 2, EventName = "shell.opened", Level = LogLevel.Information,
        Message = "Shell window opened: version {Version}, startup {StartupMs} ms")]
    private static partial void LogShellOpened(ILogger logger, string version, long startupMs);

    [LoggerMessage(EventId = 3, EventName = "shell.crashed", Level = LogLevel.Critical,
        Message = "Shell stopped on an unhandled exception: {ExceptionType}")]
    private static partial void LogShellCrashed(ILogger logger, string exceptionType);
}
