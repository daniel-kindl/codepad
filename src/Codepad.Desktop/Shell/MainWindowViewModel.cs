using Codepad.Application.Startup;
using Codepad.Desktop.Editor;
using Codepad.Desktop.History;
using Codepad.Desktop.Results;
using Codepad.Language;

namespace Codepad.Desktop.Shell;

/// <summary>The presentation state of the main window.</summary>
/// <param name="notices">The problems that startup recovered from.</param>
/// <param name="editor">The C# editor. Null creates the editor with the Roslyn language service.</param>
/// <param name="history">The history panel, or null when run history is not available. The window then hides the panel.</param>
public sealed class MainWindowViewModel(IReadOnlyList<StartupNotice> notices, CSharpEditorViewModel? editor = null, RunHistoryViewModel? history = null)
{
    /// <summary>The status text when the settings file is not usable.</summary>
    internal const string SETTINGS_UNUSABLE_TEXT = "Your settings file couldn't be read, so CodePad is using the default settings.";

    /// <summary>The status text when the data folder is not usable.</summary>
    internal const string DATA_FOLDER_UNAVAILABLE_TEXT = "CodePad can't write to its data folder, so your work won't be saved.";

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
            _ => throw new ArgumentOutOfRangeException(nameof(notices), notice.Kind, "No status text for this notice kind."),
        }));

    /// <summary>Gets a value indicating whether the status region shows a startup problem.</summary>
    public bool HasWarning { get; } = notices.Count > 0;
}
