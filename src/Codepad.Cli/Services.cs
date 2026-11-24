using Codepad.Application.Experiments;
using Codepad.Application.History;
using Codepad.Application.Library;
using Codepad.Application.Settings;
using Codepad.Application.Startup;
using Codepad.Infrastructure.Execution;
using Codepad.Infrastructure.Startup;
using Codepad.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace Codepad.Cli;

/// <summary>The storage of the app is not usable. The reason is a code that is safe to show.</summary>
internal sealed class StorageUnavailableException(string reason) : Exception($"CodePad cannot open its database: {reason}.")
{
    public string Reason { get; } = reason;
}

/// <summary>
/// The composition root of the command line. It uses the same folders, the same database, the same worker, the same
/// limits and the same use cases as the desktop app. The CLI and a running desktop app share the database file. SQLite
/// locks writes, and the revision of an experiment stops a lost write.
/// </summary>
internal sealed class Services : IDisposable
{
    private readonly ILoggerFactory loggerFactory;

    private Services(SqliteDatabase database, string dataFolder, UserSettings settings, ILoggerFactory loggerFactory, string? workerFolder)
    {
        Database = database;
        DataFolder = dataFolder;
        Settings = settings;
        this.loggerFactory = loggerFactory;
        WorkerFolder = workerFolder;
        Experiments = new SqliteExperimentRepository(database);
        History = new SqliteRunHistoryRepository(database, () => Settings.HistoryRetention);
        Library = new SqliteLibraryRepository(database);
    }

    public SqliteDatabase Database { get; }

    public string DataFolder { get; }

    public UserSettings Settings { get; }

    public string? WorkerFolder { get; }

    public SqliteExperimentRepository Experiments { get; }

    public SqliteRunHistoryRepository History { get; }

    public SqliteLibraryRepository Library { get; }

    /// <summary>Opens the data folder and the database. The schema is created or upgraded as the desktop app does.</summary>
    /// <param name="environment">The surroundings.</param>
    /// <returns>The services.</returns>
    /// <exception cref="StorageUnavailableException">The folder or the database cannot be used.</exception>
    public static Services Open(CliEnvironment environment)
    {
        AppFolderPaths located = AppFolderLocator.Locate();
        string dataFolder = environment.DataFolder ?? located.DataFolder;
        string configFolder = environment.ConfigFolder ?? located.ConfigFolder;

        DataFolderStatus folder = new FileSystemDataFolder(dataFolder).Prepare();
        if (!folder.IsReady)
        {
            throw new StorageUnavailableException(folder.Reason ?? "data_folder");
        }

        SqliteDatabase database = new SqliteDatabase(Path.Combine(dataFolder, "codepad.db"));
        DataStoreStatus store = database.Initialize();
        if (!store.IsReady)
        {
            throw new StorageUnavailableException(store.Reason ?? "data_store");
        }

        // The limits come from the settings of the desktop app, so a run has the same time limit in both. A bad value is the default.
        UserSettings settings = JsonUserSettingsStore.InFolder(configFolder).Read().Settings;

        // Logs go to the standard error. The standard output holds only the JSON result.
        ILoggerFactory factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Warning)
            .AddJsonConsole()
            .AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace));
        return new Services(database, dataFolder, settings, factory, environment.WorkerFolder);
    }

    /// <summary>Makes the run use case.</summary>
    /// <param name="timeoutSeconds">A time limit for this run, or null to use the setting.</param>
    /// <param name="clock">The time source.</param>
    /// <returns>The use case, or null when the worker program is missing.</returns>
    public ExperimentRunService? CreateRunService(int? timeoutSeconds, TimeProvider clock)
    {
        string runFolder = Path.Combine(DataFolder, "runs");
        WorkerLaunchOptions? worker = WorkerLaunchOptions.Locate(WorkerFolder ?? AppContext.BaseDirectory, runFolder);
        if (worker is null)
        {
            return null;
        }

        UserSettings effective = timeoutSeconds is int seconds ? Settings with { RunTimeoutSeconds = seconds } : Settings;
        RunHistoryService runs = new RunHistoryService(new WorkerSupervisor(worker, loggerFactory.CreateLogger<WorkerSupervisor>()), History, clock, () => effective.CSharpPolicy);
        return new ExperimentRunService(new ExperimentCatalog(Experiments, clock), runs);
    }

    public ExperimentCatalog CreateCatalog(TimeProvider clock) => new ExperimentCatalog(Experiments, clock);

    public void Dispose() => loggerFactory.Dispose();
}
