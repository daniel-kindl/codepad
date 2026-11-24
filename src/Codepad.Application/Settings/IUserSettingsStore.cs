namespace Codepad.Application.Settings;

/// <summary>The result of a read of the stored settings. The settings are always valid.</summary>
/// <param name="Settings">The settings. A missing or invalid value is the default.</param>
/// <param name="Warnings">The reasons for each fallback, for example <c>editorFontSize</c> or <c>future_version</c>. They never hold a value.</param>
public sealed record UserSettingsReadResult(UserSettings Settings, IReadOnlyList<string> Warnings);

/// <summary>The settings could not be written. The reason is safe to log. It never holds a path.</summary>
public sealed class UserSettingsStorageException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="UserSettingsStorageException"/> class.</summary>
    /// <param name="reason">The reason code, for example <c>read_only</c>.</param>
    /// <param name="innerException">The cause.</param>
    public UserSettingsStorageException(string reason, Exception? innerException = null)
        : base($"The settings could not be saved: {reason}.", innerException)
    {
        Reason = reason;
    }

    /// <summary>Gets the reason code.</summary>
    public string Reason { get; }
}

/// <summary>Reads and writes the settings that the user can change.</summary>
public interface IUserSettingsStore
{
    /// <summary>Reads the settings. A damaged file, a future version or a bad value gives the default for what it affects, with a warning. It does not throw for bad data.</summary>
    /// <returns>The valid settings and the warnings.</returns>
    UserSettingsReadResult Read();

    /// <summary>Writes the settings. Other content of the file stays.</summary>
    /// <param name="settings">Valid settings.</param>
    /// <exception cref="UserSettingsStorageException">The write failed. The old file is still whole.</exception>
    void Save(UserSettings settings);
}

/// <summary>
/// The handoff to the updater. The settings window shows the Check for updates action only when the app has one.
/// A check never downloads or installs by itself.
/// </summary>
public interface IUpdateCheckHandoff
{
    /// <summary>Checks for an update now. The user asked for it.</summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A sentence for the user about the result.</returns>
    Task<string> CheckAsync(CancellationToken cancellationToken = default);
}
