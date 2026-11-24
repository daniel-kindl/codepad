using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Codepad.Application.Editing;
using Codepad.Application.History;
using Codepad.Application.Json;
using Codepad.Application.Workspace;
using Codepad.Application.Startup;
using Codepad.Desktop.Editor;
using Codepad.Desktop.History;
using Codepad.Desktop.Json;
using Codepad.Desktop.Library;
using Codepad.Desktop.Results;
using Codepad.Desktop.Workspace;
using Codepad.Desktop.Shell;
using Codepad.Infrastructure.Execution;
using Codepad.Infrastructure.Startup;
using Codepad.Infrastructure.Storage;
using Codepad.Language;
using Microsoft.Extensions.Logging;

namespace Codepad.Desktop;

/// <summary>The composition root. It creates the services and starts the Avalonia app.</summary>
internal static partial class Program
{
    /// <summary>Opens the window, then closes the app with exit code 0. CI uses it as the launch smoke test.</summary>
    internal const string SMOKE_TEST_ARGUMENT = "--smoke-test";

    /// <summary>The name of the experiment database in the data folder.</summary>
    private const string DATABASE_FILE_NAME = "codepad.db";

    /// <summary>The name of the working folder of the worker in the data folder.</summary>
    private const string RUN_FOLDER_NAME = "runs";

    [STAThread]
    public static int Main(string[] args)
    {
        long startedAt = Stopwatch.GetTimestamp();

        AppFolderPaths folders = AppFolderLocator.Locate();
        SqliteDatabase database = new SqliteDatabase(Path.Combine(folders.DataFolder, DATABASE_FILE_NAME));
        StartupReport report = new StartupService(JsonSettingsStore.InFolder(folders.ConfigFolder), new FileSystemDataFolder(folders.DataFolder), database).Prepare();

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(report.Settings.MinimumLogLevel)
            .AddJsonConsole());
        ILogger logger = loggerFactory.CreateLogger("Codepad.Desktop");

        foreach (StartupNotice notice in report.Notices)
        {
            LogStartupDegraded(logger, notice.Kind, notice.Reason);
        }

        WorkspaceViewModel? workspace = report.Notices.Any(notice => notice.Kind is StartupNoticeKind.DataFolderUnavailable or StartupNoticeKind.DataStoreUnavailable)
            ? null
            : OpenWorkspace(database, folders, loggerFactory, logger);
        LibraryViewModel? library = workspace is null
            ? null
            : new LibraryViewModel(new SqliteLibraryRepository(database), workspace, TimeProvider.System, TimeSpan.FromMilliseconds(250), reportFailure: reason => LogLibraryFailed(logger, reason));

        string version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        ShellOptions options = new ShellOptions(
            new MainWindowViewModel(report.Notices, new CSharpEditorViewModel(new CSharpHighlighter(), new CSharpAnalyzer(), TimeProvider.System, DiagnosticsCoordinator.DefaultDebounce, reportFailure: reason => LogAnalysisFailed(logger, reason), completion: new CSharpCompletionProvider(), reportCompletionFailure: reason => LogCompletionFailed(logger, reason)), workspace: workspace, library: library) { ReportCommandFailure = reason => LogCommandFailed(logger, reason) },
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

    private static WorkspaceViewModel OpenWorkspace(SqliteDatabase database, AppFolderPaths folders, ILoggerFactory loggerFactory, ILogger logger)
    {
        // The worker gets one stable working folder in the data folder. A run can write files there. It cannot reach the database.
        string runFolder = Path.Combine(folders.DataFolder, RUN_FOLDER_NAME);
        Directory.CreateDirectory(runFolder);
        WorkerLaunchOptions? worker = WorkerLaunchOptions.Locate(AppContext.BaseDirectory, runFolder);

        ResultsViewModel results = new ResultsViewModel();
        RunHistoryService? service = worker is null
            ? null
            : new RunHistoryService(new WorkerSupervisor(worker, loggerFactory.CreateLogger<WorkerSupervisor>()), new SqliteRunHistoryRepository(database), TimeProvider.System);
        RunGate gate = new RunGate();
        RunHistoryViewModel? history = service is null ? null : new RunHistoryViewModel(new SqliteRunHistoryRepository(database), service, results, gate);

        WorkspaceViewModel? workspace = null;
        RunController runs = new RunController(
            service,
            history,
            results,
            action => Avalonia.Threading.Dispatcher.UIThread.Post(action),
            text => workspace?.ShowNotice(text),
            reason => LogRunFailed(logger, reason),
            worker is null ? "The worker program is missing, so CodePad can't run code. Reinstall CodePad." : null,
            gate);

        workspace = new WorkspaceViewModel(
            new SqliteExperimentRepository(database),
            new SessionStore(new SqlitePreferenceRepository(database)),
            TimeProvider.System,
            () => new CSharpEditorViewModel(new CSharpHighlighter(), new CSharpAnalyzer(), TimeProvider.System, DiagnosticsCoordinator.DefaultDebounce, reportFailure: reason => LogAnalysisFailed(logger, reason), completion: new CSharpCompletionProvider(), reportCompletionFailure: reason => LogCompletionFailed(logger, reason)),
            () => new JsonToolViewModel(new JsonEvaluator(), TimeProvider.System, LiveJsonCoordinator.DefaultDebounce, reportFailure: reason => LogJsonFailed(logger, reason)),
            reportFailure: reason => LogWorkspaceStorageFailed(logger, reason),
            runs: runs);
        return workspace;
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

    [LoggerMessage(EventId = 4, EventName = "editor.analysis_failed", Level = LogLevel.Warning,
        Message = "The C# analysis failed: {ExceptionType}")]
    private static partial void LogAnalysisFailed(ILogger logger, string exceptionType);

    [LoggerMessage(EventId = 5, EventName = "editor.completion_failed", Level = LogLevel.Warning,
        Message = "A C# completion request failed: {ExceptionType}")]
    private static partial void LogCompletionFailed(ILogger logger, string exceptionType);

    [LoggerMessage(EventId = 6, EventName = "json.evaluation_failed", Level = LogLevel.Warning,
        Message = "The JSON evaluation failed: {ExceptionType}")]
    private static partial void LogJsonFailed(ILogger logger, string exceptionType);

    [LoggerMessage(EventId = 7, EventName = "workspace.storage_failed", Level = LogLevel.Warning,
        Message = "The tab session could not be read or saved: {Reason}")]
    private static partial void LogWorkspaceStorageFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 8, EventName = "library.search_failed", Level = LogLevel.Warning,
        Message = "A library search failed: {Reason}")]
    private static partial void LogLibraryFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 9, EventName = "run.failed", Level = LogLevel.Warning,
        Message = "A run could not be completed or stored: {Reason}")]
    private static partial void LogRunFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 10, EventName = "command.failed", Level = LogLevel.Warning,
        Message = "A command handler failed: {ExceptionType}")]
    private static partial void LogCommandFailed(ILogger logger, string exceptionType);
}
