using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Codepad.Desktop.Json;

namespace Codepad.Desktop.Workspace;

/// <summary>The workspace: the tab strip and the view of each open tab. Only the active tab is visible.</summary>
public sealed partial class WorkspaceView : UserControl
{
    private WorkspaceViewModel? model;

    /// <summary>Initializes a new instance of the <see cref="WorkspaceView"/> class.</summary>
    public WorkspaceView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (model is not null)
        {
            model.PropertyChanged -= OnModelChanged;
        }

        model = DataContext as WorkspaceViewModel;
        if (model is not null)
        {
            model.PropertyChanged += OnModelChanged;
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceViewModel.ActiveTab))
        {
            // The new tab must be visible before it can take the focus. So the focus moves after the layout.
            Dispatcher.UIThread.Post(FocusActiveEditor, DispatcherPriority.Loaded);
        }
    }

    private void FocusActiveEditor()
    {
        ExperimentTabViewModel? active = model?.ActiveTab;
        if (active is null)
        {
            return;
        }

        foreach (Control view in this.GetVisualDescendants().OfType<Control>())
        {
            if (view is CSharpTabView csharp && ReferenceEquals(csharp.DataContext, active))
            {
                csharp.FocusEditor();
                return;
            }

            if (view is JsonToolView tool && active is JsonTabViewModel json && ReferenceEquals(tool.DataContext, json.Tool))
            {
                tool.Input.Focus();
                return;
            }
        }
    }
}
