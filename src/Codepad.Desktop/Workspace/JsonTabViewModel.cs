using Codepad.Application.Experiments;
using Codepad.Desktop.Json;

namespace Codepad.Desktop.Workspace;

/// <summary>A tab with a JSON experiment: the live JSON tool.</summary>
public sealed class JsonTabViewModel : ExperimentTabViewModel
{
    /// <summary>Initializes a new instance of the <see cref="JsonTabViewModel"/> class.</summary>
    /// <param name="session">The autosave session of the experiment.</param>
    /// <param name="tool">The JSON tool. The tab loads the saved source into it.</param>
    /// <param name="timeProvider">Supplies the edit time.</param>
    /// <param name="post">Runs an action on the UI thread.</param>
    public JsonTabViewModel(AutosaveSession session, JsonToolViewModel tool, TimeProvider timeProvider, Action<Action> post)
        : base(session, timeProvider, post)
    {
        ArgumentNullException.ThrowIfNull(tool);
        Tool = tool;
        tool.Load(session.Current.Source);
        tool.ContentChanged += Edited;
    }

    /// <summary>Gets the JSON tool.</summary>
    public JsonToolViewModel Tool { get; }

    /// <inheritdoc />
    protected override void DisposeEditor()
    {
        Tool.ContentChanged -= Edited;
        Tool.Dispose();
    }
}
