using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Codepad.Desktop.Commands;

/// <summary>
/// The command palette overlay. The focus stays in the search box. Up and Down move the selection, Enter runs the
/// selected command and Escape closes the palette. A click on a row runs it. A click outside the card closes it.
/// </summary>
public sealed partial class CommandPaletteView : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="CommandPaletteView"/> class.</summary>
    public CommandPaletteView()
    {
        InitializeComponent();
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        CommandList.AddHandler(PointerReleasedEvent, OnListReleased, RoutingStrategies.Tunnel);
    }

    /// <summary>Gets the search box. Tests use it.</summary>
    internal TextBox Search => SearchBox;

    /// <summary>Gets the list. Tests use it.</summary>
    internal ListBox List => CommandList;

    /// <summary>Moves the focus to the search box and selects its text.</summary>
    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private CommandPaletteViewModel? Model => DataContext as CommandPaletteViewModel;

    private async void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                e.Handled = true;
                Model.MoveSelection(1);
                break;
            case Key.Up:
                e.Handled = true;
                Model.MoveSelection(-1);
                break;
            case Key.PageDown:
                e.Handled = true;
                Model.MoveSelection(5);
                break;
            case Key.PageUp:
                e.Handled = true;
                Model.MoveSelection(-5);
                break;
            case Key.Enter:
                e.Handled = true;
                await Model.InvokeAsync();
                break;
            case Key.Escape:
                e.Handled = true;
                Model.Close();
                break;
            default:
                break;
        }
    }

    private async void OnListReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Model is not null && e.InitialPressMouseButton == MouseButton.Left && (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>() is ListBoxItem item)
        {
            Model.SelectedIndex = CommandList.IndexFromContainer(item);
            await Model.InvokeAsync();
            e.Handled = true;
        }
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.Handled)
        {
            Model?.Close();
        }
    }

    // A press inside the card must not reach the backdrop, which would close the palette.
    private void OnCardPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;
}
