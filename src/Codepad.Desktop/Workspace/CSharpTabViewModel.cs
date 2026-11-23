using Codepad.Application.Experiments;
using Codepad.Desktop.Editor;
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
    public CSharpTabViewModel(AutosaveSession session, CSharpEditorViewModel editor, TimeProvider timeProvider, Action<Action> post)
        : base(session, timeProvider, post)
    {
        ArgumentNullException.ThrowIfNull(editor);
        Editor = editor;
        editor.Load(session.Current.Source);
        editor.ContentChanged += Edited;
    }

    /// <summary>Gets the editor.</summary>
    public CSharpEditorViewModel Editor { get; }

    /// <summary>Gets the result panel of this tab.</summary>
    public ResultsViewModel Results { get; } = new ResultsViewModel();

    /// <inheritdoc />
    protected override void DisposeEditor()
    {
        Editor.ContentChanged -= Edited;
        Editor.Dispose();
    }
}
