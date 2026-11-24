using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Codepad.Domain.Operations;

namespace Codepad.Desktop.Workspace;

/// <summary>
/// The strip of tabs. A click selects a tab. The close button, the context menu and Ctrl+W close it. A drag moves it,
/// and the context menu moves it left or right. The strip scrolls when the tabs do not fit.
/// </summary>
public sealed partial class TabStripView : UserControl
{
    private const double DRAG_DISTANCE = 8;

    private ExperimentTabViewModel? dragged;
    private Point dragStart;

    /// <summary>Initializes a new instance of the <see cref="TabStripView"/> class.</summary>
    public TabStripView()
    {
        InitializeComponent();
        TabList.SelectionChanged += OnSelectionChanged;
        TabList.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        TabList.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
    }

    /// <summary>Gets the tab list. Tests use it.</summary>
    internal ListBox List => TabList;

    private WorkspaceViewModel? Model => DataContext as WorkspaceViewModel;

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TabList.SelectedItem is ExperimentTabViewModel tab)
        {
            Model?.Activate(tab);
            TabList.ScrollIntoView(tab);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        dragged = e.GetCurrentPoint(TabList).Properties.IsLeftButtonPressed ? TabAt(e.Source) : null;
        dragStart = e.GetPosition(TabList);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ExperimentTabViewModel? source = dragged;
        dragged = null;
        Point end = e.GetPosition(TabList);
        if (source is null || Model is null || Math.Abs(end.X - dragStart.X) < DRAG_DISTANCE)
        {
            return;
        }

        // The tab under the pointer when the button goes up is the target position.
        ExperimentTabViewModel? target = TabAt(TabList.InputHitTest(end) as Visual);
        if (target is not null && !ReferenceEquals(target, source))
        {
            Model.Move(source, Model.Tabs.IndexOf(target));
        }
    }

    private static ExperimentTabViewModel? TabAt(object? source) =>
        (source as Visual)?.FindAncestorOfType<ListBoxItem>()?.DataContext as ExperimentTabViewModel;

    private async void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as Control)?.DataContext is ExperimentTabViewModel tab && Model is not null)
        {
            await Model.CloseAsync(tab);
        }
    }

    private async void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ExperimentTabViewModel tab || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        RenameWindow dialog = new RenameWindow { EnteredTitle = tab.Title };
        string? title = await dialog.ShowDialog<string?>(owner);
        if (title is not null)
        {
            tab.Rename(title);
        }
    }

    private void OnMoveLeftClick(object? sender, RoutedEventArgs e) => MoveBy(sender, -1);

    private void OnMoveRightClick(object? sender, RoutedEventArgs e) => MoveBy(sender, 1);

    private void MoveBy(object? sender, int offset)
    {
        if ((sender as Control)?.DataContext is ExperimentTabViewModel tab)
        {
            Model?.MoveBy(tab, offset);
        }
    }

    private async void OnCloseMenuClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ExperimentTabViewModel tab && Model is not null)
        {
            await Model.CloseAsync(tab);
        }
    }

    private async void OnNewCSharpClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not null)
        {
            await Model.NewExperimentAsync(OperationKind.CSharp);
        }
    }

    private async void OnNewJsonClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not null)
        {
            await Model.NewExperimentAsync(OperationKind.Json);
        }
    }
}
