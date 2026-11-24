using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Codepad.Application.Editing;
using Codepad.Application.History;
using Codepad.Application.Json;
using Codepad.Application.Settings;
using Codepad.Application.Updates;
using Codepad.Application.Workspace;
using Codepad.Application.Startup;
using Codepad.Desktop.Editor;
using Codepad.Desktop.History;
using Codepad.Desktop.Json;
using Codepad.Desktop.Library;
using Codepad.Desktop.Results;
using Codepad.Desktop.Settings;
using Codepad.Desktop.Updates;
using Codepad.Desktop.Workspace;
using Codepad.Desktop.Shell;
using Codepad.Infrastructure.Execution;
using Codepad.Infrastructure.Startup;
using Codepad.Infrastructure.Updates;
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

        // The user settings come from the same file as the log level. A bad value falls back to its default, and the status line says so.
        UserSettingsReadResult preferences = JsonUserSettingsStore.InFolder(folders.ConfigFolder).Read();
        if (preferences.Warnings.Count > 0)
        {
            report = report with { Notices = [.. report.Notices, new StartupNotice(StartupNoticeKind.PreferencesRecovered, string.Join(',', preferences.Warnings))] };
        }

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(report.Settings.MinimumLogLevel)
            .AddJsonConsole());
        ILogger logger = loggerFactory.CreateLogger("Codepad.Desktop");

        foreach (StartupNotice notice in report.Notices)
        {
            LogStartupDegraded(logger, notice.Kind, notice.Reason);
        }

        bool storeReady = !report.Notices.Any(notice => notice.Kind is StartupNoticeKind.DataFolderUnavailable or StartupNoticeKind.DataStoreUnavailable);
        SettingsViewModel? settingsHolder = null;
        UpdateViewModel? updatesHolder = null;
        SqliteRunHistoryRepository? historyRepository = storeReady ? new SqliteRunHistoryRepository(database, () => settingsHolder!.Current.HistoryRetention) : null;
        SettingsViewModel settings = new SettingsViewModel(
            JsonUserSettingsStore.InFolder(folders.ConfigFolder),
            preferences.Settings,
            historyRepository,
            reason => LogSettingsFailed(logger, reason),
            storeReady ? new UpdateCheckHandoff(() => updatesHolder) : null,
            "CodePad " + UpdateBootstrap.InstalledVersion());
        settingsHolder = settings;
        WorkspaceViewModel? workspace = report.Notices.Any(notice => notice.Kind is StartupNoticeKind.DataFolderUnavailable or StartupNoticeKind.DataStoreUnavailable)
            ? null
            : OpenWorkspace(database, folders, loggerFactory, logger, settings, historyRepository!);
        LibraryViewModel? library = workspace is null
            ? null
            : new LibraryViewModel(new SqliteLibraryRepository(database), workspace, TimeProvider.System, TimeSpan.FromMilliseconds(250), reportFailure: reason => LogLibraryFailed(logger, reason));

        UpdateViewModel? updates = workspace is null ? null : CreateUpdates(database, folders, logger, settings, workspace);
        updatesHolder = updates;

        string version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        ShellOptions options = new ShellOptions(
            new MainWindowViewModel(report.Notices, new CSharpEditorViewModel(new CSharpHighlighter(), new CSharpAnalyzer(), TimeProvider.System, DiagnosticsCoordinator.DefaultDebounce, reportFailure: reason => LogAnalysisFailed(logger, reason), completion: new CSharpCompletionProvider(), reportCompletionFailure: reason => LogCompletionFailed(logger, reason), preferences: settings.Preferences), workspace: workspace, library: library, settings: settings, updates: updates) { ReportCommandFailure = reason => LogCommandFailed(logger, reason) },
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

    private static UpdateViewModel? CreateUpdates(SqliteDatabase database, AppFolderPaths folders, ILogger logger, SettingsViewModel settings, WorkspaceViewModel workspace)
    {
        SemanticVersion installed = UpdateBootstrap.InstalledVersion();
        string updatesFolder = Path.Combine(folders.DataFolder, "updates");
        string processPath = Environment.ProcessPath ?? string.Empty;
        FileUpdateInstaller installer = new FileUpdateInstaller(new FileUpdateInstallerOptions(
            AppContext.BaseDirectory,
            updatesFolder,
            Path.GetFileName(processPath),
            AppContext.BaseDirectory,
            installed.ToString(),
            Path.Combine(folders.DataFolder, DATABASE_FILE_NAME),
            database.BackupAsync,
            Environment.ProcessId,
            ["--post-update"],
            TimeSpan.FromSeconds(60)));
        UpdateService service = new UpdateService(
            new GitHubUpdateSource(UpdateHttpClient.Create(TimeSpan.FromSeconds(20))),
            new EcdsaManifestVerifier(UpdateBootstrap.PublicKeyPem()),
            new HttpUpdateDownloader(UpdateHttpClient.Create(TimeSpan.FromMinutes(30)), Path.Combine(updatesFolder, "staging")),
            installer,
            new PreferenceUpdateStore(new SqlitePreferenceRepository(database)),
            TimeProvider.System,
            installed,
            UpdatePlatform.Current(),
            item => LogUpdateEvent(logger, item.Name, item.Version ?? "none", item.Code ?? "none"));
        UpdateViewModel model = new UpdateViewModel(
            service,
            new PreferenceUpdateStore(new SqlitePreferenceRepository(database)),
            installed,
            enabled => settings.UpdateChecksEnabled = enabled,
            () => workspace.TrySaveAllAsync(),
            () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown(0);
                }
            }));

        // The helper leaves a result after an update. It tells the user whether the update worked or CodePad went back.
        if (UpdateBootstrap.TakePreviousResult(Path.Combine(updatesFolder, FileUpdateInstaller.ResultFileName)) is (string from, string to, string status))
        {
            model.ShowPreviousResult(from, to, status);
        }

        return model;
    }

    private static WorkspaceViewModel OpenWorkspace(SqliteDatabase database, AppFolderPaths folders, ILoggerFactory loggerFactory, ILogger logger, SettingsViewModel settings, SqliteRunHistoryRepository historyRepository)
    {
        // The worker gets one stable working folder in the data folder. A run can write files there. It cannot reach the database.
        string runFolder = Path.Combine(folders.DataFolder, RUN_FOLDER_NAME);
        Directory.CreateDirectory(runFolder);
        WorkerLaunchOptions? worker = WorkerLaunchOptions.Locate(AppContext.BaseDirectory, runFolder);

        ResultsViewModel results = new ResultsViewModel();
        RunHistoryService? service = worker is null
            ? null
            : new RunHistoryService(new WorkerSupervisor(worker, loggerFactory.CreateLogger<WorkerSupervisor>()), historyRepository, TimeProvider.System, () => settings.Current.CSharpPolicy);
        RunGate gate = new RunGate();
        RunHistoryViewModel? history = service is null ? null : new RunHistoryViewModel(historyRepository, service, results, gate);

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

        settings.HistoryCleared += runs.OnHistoryCleared;
        workspace = new WorkspaceViewModel(
            new SqliteExperimentRepository(database),
            new SessionStore(new SqlitePreferenceRepository(database)),
            TimeProvider.System,
            () => new CSharpEditorViewModel(new CSharpHighlighter(), new CSharpAnalyzer(), TimeProvider.System, DiagnosticsCoordinator.DefaultDebounce, reportFailure: reason => LogAnalysisFailed(logger, reason), completion: new CSharpCompletionProvider(), reportCompletionFailure: reason => LogCompletionFailed(logger, reason), preferences: settings.Preferences),
            () => new JsonToolViewModel(new JsonEvaluator(), TimeProvider.System, LiveJsonCoordinator.DefaultDebounce, reportFailure: reason => LogJsonFailed(logger, reason), preferences: settings.Preferences),
            reportFailure: reason => LogWorkspaceStorageFailed(logger, reason),
            runs: runs,
            reportSaveFailure: reason => LogAutosaveFailed(logger, reason));
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

    [LoggerMessage(EventId = 11, EventName = "settings.failed", Level = LogLevel.Warning,
        Message = "The settings could not be saved or the history could not be cleared: {Reason}")]
    private static partial void LogSettingsFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 12, EventName = "autosave.failed", Level = LogLevel.Warning,
        Message = "An autosave failed: {Reason}. The text stays in the editor.")]
    private static partial void LogAutosaveFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 13, EventName = "update.event", Level = LogLevel.Information,
        Message = "Update: {Name}, version {Version}, code {Code}")]
    private static partial void LogUpdateEvent(ILogger logger, string name, string version, string code);
}
