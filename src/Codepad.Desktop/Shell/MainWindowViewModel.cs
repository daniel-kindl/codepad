using Codepad.Application.Startup;

namespace Codepad.Desktop.Shell;

/// <summary>The presentation state of the main window.</summary>
/// <param name="notices">The problems that startup recovered from.</param>
public sealed class MainWindowViewModel(IReadOnlyList<StartupNotice> notices)
{
    /// <summary>The status text when the settings file is not usable.</summary>
    internal const string SETTINGS_UNUSABLE_TEXT = "Your settings file couldn't be read, so CodePad is using the default settings.";

    /// <summary>The status text when the data folder is not usable.</summary>
    internal const string DATA_FOLDER_UNAVAILABLE_TEXT = "CodePad can't write to its data folder, so your work won't be saved.";

    private const string READY_TEXT = "Ready";

    /// <summary>Gets the window title.</summary>
    public string Title { get; } = "CodePad";

    /// <summary>Gets the text of the status region. It never shows the technical reason of a notice.</summary>
    public string StatusText { get; } = notices.Count == 0
        ? READY_TEXT
        : string.Join(" ", notices.Select(notice => notice.Kind switch
        {
            StartupNoticeKind.SettingsUnusable => SETTINGS_UNUSABLE_TEXT,
            StartupNoticeKind.DataFolderUnavailable => DATA_FOLDER_UNAVAILABLE_TEXT,
            _ => throw new ArgumentOutOfRangeException(nameof(notices), notice.Kind, "No status text for this notice kind."),
        }));

    /// <summary>Gets a value indicating whether the status region shows a startup problem.</summary>
    public bool HasWarning { get; } = notices.Count > 0;
}
