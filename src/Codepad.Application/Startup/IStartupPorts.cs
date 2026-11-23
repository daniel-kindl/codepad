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
