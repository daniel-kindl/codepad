using Codepad.Application.Startup;
using Codepad.Desktop.Editor;
using Codepad.Desktop.History;
using Codepad.Desktop.Library;
using Codepad.Desktop.Results;
using Codepad.Desktop.Settings;
using Codepad.Desktop.Workspace;
using Codepad.Language;

namespace Codepad.Desktop.Shell;

/// <summary>The presentation state of the main window.</summary>
/// <param name="notices">The problems that startup recovered from.</param>
/// <param name="editor">The C# editor. Null creates the editor with the Roslyn language service.</param>
/// <param name="history">The history panel, or null when run history is not available. The window then hides the panel.</param>
/// <param name="workspace">The open tabs, or null when storage is not available. The window then shows one editor that does not save.</param>
/// <param name="library">The library panel, or null when storage is not available. The window then hides the panel.</param>
/// <param name="settings">The settings, or null. The window then has no settings command.</param>
public sealed class MainWindowViewModel(IReadOnlyList<StartupNotice> notices, CSharpEditorViewModel? editor = null, RunHistoryViewModel? history = null, WorkspaceViewModel? workspace = null, LibraryViewModel? library = null, SettingsViewModel? settings = null)
{
    /// <summary>The status text when the settings file is not usable.</summary>
    internal const string SETTINGS_UNUSABLE_TEXT = "Your settings file couldn't be read, so CodePad is using the default settings.";

    /// <summary>The status text when the data folder is not usable.</summary>
    internal const string DATA_FOLDER_UNAVAILABLE_TEXT = "CodePad can't write to its data folder, so your work won't be saved.";

    /// <summary>The status text when the experiment database is not usable.</summary>
    internal const string PREFERENCES_RECOVERED_TEXT = "Some of your preferences were not valid, so CodePad used the defaults for them. Open the settings to check them.";

    /// <summary>The status text when the experiment database is not usable.</summary>
    internal const string DATA_STORE_UNAVAILABLE_TEXT = "CodePad can't open its experiment database, so your work won't be saved.";

    private const string READY_TEXT = "Ready";

    /// <summary>Gets the C# editor.</summary>
    public CSharpEditorViewModel Editor { get; } = editor ?? new CSharpEditorViewModel(new CSharpHighlighter(), new CSharpAnalyzer(), TimeProvider.System, Codepad.Application.Editing.DiagnosticsCoordinator.DefaultDebounce, completion: new CSharpCompletionProvider());

    /// <summary>Gets the result panel.</summary>
    public ResultsViewModel Results { get; } = history?.Results ?? new ResultsViewModel();

    /// <summary>Gets the history panel, or null when history is not available (for example, when the database cannot open).</summary>
    public RunHistoryViewModel? History { get; } = history;

    /// <summary>Gets a value indicating whether the window shows the history panel.</summary>
    public bool HasHistory => History is not null;

    /// <summary>Gets the open tabs, or null when storage is not available.</summary>
    public WorkspaceViewModel? Workspace { get; } = workspace;

    /// <summary>Gets the function that receives the exception type name when a command handler fails. Null ignores it.</summary>
    public Action<string>? ReportCommandFailure { get; init; }

    /// <summary>Gets the settings, or null.</summary>
    public SettingsViewModel? Settings { get; } = settings;

    /// <summary>Gets a value indicating whether the window has settings.</summary>
    public bool HasSettings => Settings is not null;

    /// <summary>Gets the library panel, or null when storage is not available.</summary>
    public LibraryViewModel? Library { get; } = library;

    /// <summary>Gets a value indicating whether the window has a library panel.</summary>
    public bool HasLibrary => Library is not null;

    /// <summary>Gets a value indicating whether the window shows tabs.</summary>
    public bool HasWorkspace => Workspace is not null;

    /// <summary>Gets a value indicating whether the window shows the single editor that does not save.</summary>
    public bool HasNoWorkspace => Workspace is null;

    /// <summary>Gets the window title.</summary>
    public string Title { get; } = "CodePad";

    /// <summary>Gets the text of the status region. It never shows the technical reason of a notice.</summary>
    public string StatusText { get; } = notices.Count == 0
        ? READY_TEXT
        : string.Join(" ", notices.Select(notice => notice.Kind switch
        {
            StartupNoticeKind.SettingsUnusable => SETTINGS_UNUSABLE_TEXT,
            StartupNoticeKind.DataFolderUnavailable => DATA_FOLDER_UNAVAILABLE_TEXT,
            StartupNoticeKind.DataStoreUnavailable => DATA_STORE_UNAVAILABLE_TEXT,
            StartupNoticeKind.PreferencesRecovered => PREFERENCES_RECOVERED_TEXT,
            _ => throw new ArgumentOutOfRangeException(nameof(notices), notice.Kind, "No status text for this notice kind."),
        }));

    /// <summary>Gets a value indicating whether the status region shows a startup problem.</summary>
    public bool HasWarning { get; } = notices.Count > 0;
}
