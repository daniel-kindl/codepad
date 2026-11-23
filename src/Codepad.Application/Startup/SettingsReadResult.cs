namespace Codepad.Application.Startup;

/// <summary>The result of one read of the settings file.</summary>
public enum SettingsReadStatus
{
    /// <summary>The file exists and holds valid settings.</summary>
    Loaded,

    /// <summary>The file does not exist. This is the normal state before the first settings change.</summary>
    Missing,

    /// <summary>The file exists, but CodePad cannot read it or its content is not valid.</summary>
    Unusable,
}

/// <summary>The settings that a read found, or the reason that it found none.</summary>
/// <param name="Status">The result of the read.</param>
/// <param name="Settings">The settings. Not null only when <paramref name="Status"/> is <see cref="SettingsReadStatus.Loaded"/>.</param>
/// <param name="Reason">
/// A stable, log-safe code for an unusable file, for example <c>invalid_json</c>.
/// It never holds a path or file content.
/// </param>
public sealed record SettingsReadResult(SettingsReadStatus Status, AppSettings? Settings, string? Reason)
{
    /// <summary>Makes a result for a file with valid settings.</summary>
    /// <param name="settings">The settings in the file.</param>
    /// <returns>A result with the status <see cref="SettingsReadStatus.Loaded"/>.</returns>
    public static SettingsReadResult Loaded(AppSettings settings) => new(SettingsReadStatus.Loaded, settings, null);

    /// <summary>Makes a result for a missing file.</summary>
    /// <returns>A result with the status <see cref="SettingsReadStatus.Missing"/>.</returns>
    public static SettingsReadResult Missing() => new(SettingsReadStatus.Missing, null, null);

    /// <summary>Makes a result for a file that CodePad cannot use.</summary>
    /// <param name="reason">A stable, log-safe reason code.</param>
    /// <returns>A result with the status <see cref="SettingsReadStatus.Unusable"/>.</returns>
    public static SettingsReadResult Unusable(string reason) => new(SettingsReadStatus.Unusable, null, reason);
}
