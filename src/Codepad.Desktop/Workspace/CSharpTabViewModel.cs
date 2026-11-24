using Codepad.Application.Experiments;
using Codepad.Desktop.Editor;
using Codepad.Application.History;
using Codepad.Desktop.History;
using Codepad.Desktop.Results;

namespace Codepad.Desktop.Workspace;

/// <summary>A tab with a C# experiment: the editor and the result panel.</summary>
public sealed class CSharpTabViewModel : ExperimentTabViewModel
{
    /// <summary>Initializes a new instance of the <see cref="CSharpTabViewModel"/> class.</summary>
    /// <param name="session">The autosave session of the experiment.</param>
    /// <param name="editor">The editor. The tab loads the saved source into it.</param>
    /// <param name="timeProvider">Supplies the edit time.</param>
    /// <param name="post">Runs an action on the UI thread.</param>
    /// <param name="results">The result panel that all C# tabs share, or null to give this tab its own panel.</param>
    /// <param name="history">The history panel that all C# tabs share, or null when run history is not available.</param>
    public CSharpTabViewModel(AutosaveSession session, CSharpEditorViewModel editor, TimeProvider timeProvider, Action<Action> post, ResultsViewModel? results = null, RunHistoryViewModel? history = null)
        : base(session, timeProvider, post)
    {
        ArgumentNullException.ThrowIfNull(editor);
        Editor = editor;
        Results = results ?? new ResultsViewModel();
        History = history;
        editor.Load(session.Current.Source);
        editor.ContentChanged += Edited;
    }

    /// <summary>Gets the editor.</summary>
    public CSharpEditorViewModel Editor { get; }

    /// <summary>Gets the result panel. All C# tabs show the same panel. It shows the last run of the active tab.</summary>
    public ResultsViewModel Results { get; }

    /// <summary>Gets the history panel, or null when run history is not available. All C# tabs share it.</summary>
    public RunHistoryViewModel? History { get; }

    /// <summary>Gets a value indicating whether the tab shows a history panel.</summary>
    public bool HasHistory => History is not null;

    /// <summary>Gets or sets the last run of this tab in this session, or null. Switching to the tab shows it again.</summary>
    public RunRecord? LastRun { get; set; }

    /// <inheritdoc />
    protected override void DisposeEditor()
    {
        Editor.ContentChanged -= Edited;
        Editor.Dispose();
    }
}
