using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Codepad.Desktop.History;

/// <summary>The history panel: the saved runs of the experiment, the saved source of one run, and the Rerun command.</summary>
public sealed partial class RunHistoryView : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="RunHistoryView"/> class.</summary>
    public RunHistoryView()
    {
        InitializeComponent();
        RunList.SelectionChanged += OnSelectionChanged;
    }

    /// <summary>Gets the list. Tests use it to select a run.</summary>
    internal ListBox List => RunList;

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is RunHistoryViewModel model && RunList.SelectedItem is RunHistoryItem item)
        {
            try
            {
                await model.InspectAsync(item);
            }
            catch (OperationCanceledException)
            {
                // The panel closed while the read ran. There is nothing to show.
            }
        }
    }

    private async void OnRerunClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RunHistoryViewModel model)
        {
            try
            {
                await model.RerunAsync();
            }
            catch (OperationCanceledException)
            {
                // The user closed the panel while the run was active.
            }
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as RunHistoryViewModel)?.EndInspection();
        RunList.SelectedItem = null;
    }
}
