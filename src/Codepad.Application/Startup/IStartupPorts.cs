namespace Codepad.Application.Startup;

/// <summary>Reads the user settings file.</summary>
public interface ISettingsStore
{
    /// <summary>Reads the settings file. Does not throw for a missing, unreadable or invalid file.</summary>
    /// <returns>The settings, or the reason that the file has no usable settings.</returns>
    SettingsReadResult Read();
}

/// <summary>The per-user folder that holds the CodePad data.</summary>
public interface IDataFolder
{
    /// <summary>
    /// Creates the folder if it does not exist and checks that CodePad can write to it.
    /// Does not throw for a folder that CodePad cannot use.
    /// </summary>
    /// <returns>The state of the folder.</returns>
    DataFolderStatus Prepare();
}

/// <summary>The state of the data folder after <see cref="IDataFolder.Prepare"/>.</summary>
/// <param name="IsReady">True when CodePad can write to the folder.</param>
/// <param name="Reason">A stable, log-safe reason code when the folder is not ready. It never holds a path.</param>
public sealed record DataFolderStatus(bool IsReady, string? Reason)
{
    /// <summary>Gets the state of a folder that CodePad can use.</summary>
    public static DataFolderStatus Ready { get; } = new(true, null);

    /// <summary>Makes the state of a folder that CodePad cannot use.</summary>
    /// <param name="reason">A stable, log-safe reason code.</param>
    /// <returns>A state that is not ready.</returns>
    public static DataFolderStatus Unavailable(string reason) => new(false, reason);
}

/// <summary>Prepares the experiment database: it creates the file, applies migrations and checks the schema version.</summary>
public interface IDataStoreInitializer
{
    /// <summary>Makes the database ready. Does not throw for a database that CodePad cannot use.</summary>
    /// <returns>The state of the database.</returns>
    DataStoreStatus Initialize();
}

/// <summary>The state of the database after <see cref="IDataStoreInitializer.Initialize"/>.</summary>
/// <param name="IsReady">True when CodePad can read and write the database.</param>
/// <param name="Reason">A stable, log-safe reason code when the database is not ready. It never holds a path.</param>
public sealed record DataStoreStatus(bool IsReady, string? Reason)
{
    /// <summary>Gets the state of a database that CodePad can use.</summary>
    public static DataStoreStatus Ready { get; } = new(true, null);

    /// <summary>Makes the state of a database that CodePad cannot use.</summary>
    /// <param name="reason">A stable, log-safe reason code.</param>
    /// <returns>A state that is not ready.</returns>
    public static DataStoreStatus Unavailable(string reason) => new(false, reason);
}
