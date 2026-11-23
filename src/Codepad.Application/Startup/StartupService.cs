namespace Codepad.Application.Startup;

/// <summary>A problem that CodePad found at startup and recovered from.</summary>
public enum StartupNoticeKind
{
    /// <summary>The settings file exists but is not usable. CodePad uses the default settings.</summary>
    SettingsUnusable,

    /// <summary>CodePad cannot create or write to its data folder.</summary>
    DataFolderUnavailable,
}

/// <summary>One startup problem, with a log-safe reason code.</summary>
/// <param name="Kind">The kind of problem.</param>
/// <param name="Reason">A stable, log-safe reason code. It never holds a path or file content.</param>
public sealed record StartupNotice(StartupNoticeKind Kind, string Reason);

/// <summary>The state that the app starts with.</summary>
/// <param name="Settings">The settings to use.</param>
/// <param name="Notices">The problems that startup recovered from. Empty for a normal start.</param>
public sealed record StartupReport(AppSettings Settings, IReadOnlyList<StartupNotice> Notices);

/// <summary>Prepares the settings and the data folder before the UI starts.</summary>
/// <param name="settingsStore">Reads the settings file.</param>
/// <param name="dataFolder">The per-user data folder.</param>
public sealed class StartupService(ISettingsStore settingsStore, IDataFolder dataFolder)
{
    /// <summary>
    /// Reads the settings and prepares the data folder. A problem does not stop the startup.
    /// It becomes a notice in the report.
    /// </summary>
    /// <returns>The settings to use and the problems that startup recovered from.</returns>
    public StartupReport Prepare()
    {
        List<StartupNotice> notices = new List<StartupNotice>();
        AppSettings settings = AppSettings.Default;

        SettingsReadResult read = settingsStore.Read();
        if (read.Status == SettingsReadStatus.Loaded && read.Settings is not null)
        {
            settings = read.Settings;
        }
        else if (read.Status == SettingsReadStatus.Unusable)
        {
            notices.Add(new StartupNotice(StartupNoticeKind.SettingsUnusable, read.Reason ?? "unknown"));
        }

        DataFolderStatus folder = dataFolder.Prepare();
        if (!folder.IsReady)
        {
            notices.Add(new StartupNotice(StartupNoticeKind.DataFolderUnavailable, folder.Reason ?? "unknown"));
        }

        return new StartupReport(settings, notices);
    }
}
