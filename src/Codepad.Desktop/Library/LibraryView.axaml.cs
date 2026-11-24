using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Codepad.Desktop.Library;

/// <summary>
/// The library panel. Down moves from the search box to the list. Enter in the box opens the first result, and
/// Enter in the list opens the selected row. Escape empties the search, or moves from the list to the box.
/// </summary>
public sealed partial class LibraryView : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="LibraryView"/> class.</summary>
    public LibraryView()
    {
        InitializeComponent();
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        ResultList.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        ResultList.DoubleTapped += OnListDoubleTapped;
    }

    /// <summary>Gets the search box. Tests use it.</summary>
    internal TextBox Search => SearchBox;

    /// <summary>Gets the result list. Tests use it.</summary>
    internal ListBox List => ResultList;

    /// <summary>Moves the focus to the search box.</summary>
    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private LibraryViewModel? Model => DataContext as LibraryViewModel;

    private async void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when ResultList.ItemCount > 0:
                e.Handled = true;
                ResultList.SelectedIndex = Math.Max(ResultList.SelectedIndex, 0);
                (ResultList.ContainerFromIndex(ResultList.SelectedIndex) as Control ?? ResultList).Focus();
                break;
            case Key.Enter when Model is not null && FirstOrSelected() is LibraryItem item:
                e.Handled = true;
                await Model.OpenAsync(item);
                break;
            case Key.Escape when Model is not null && SearchBox.Text?.Length > 0:
                e.Handled = true;
                Model.ClearQuery();
                break;
            default:
                break;
        }
    }

    private async void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when Model is not null && ResultList.SelectedItem is LibraryItem item:
                e.Handled = true;
                await Model.OpenAsync(item);
                break;
            case Key.Escape:
                e.Handled = true;
                SearchBox.Focus();
                break;
            case Key.Up when ResultList.SelectedIndex <= 0:
                e.Handled = true;
                SearchBox.Focus();
                break;
            default:
                break;
        }
    }

    private async void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Model is not null && ResultList.SelectedItem is LibraryItem item)
        {
            await Model.OpenAsync(item);
        }
    }

    private async void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not null)
        {
            await Model.LoadMoreAsync();
        }
    }

    private LibraryItem? FirstOrSelected() => ResultList.SelectedItem as LibraryItem ?? (ResultList.ItemCount > 0 ? ResultList.Items.Cast<LibraryItem>().First() : null);
}
