using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Codepad.Desktop.Workspace;
using Codepad.Domain.Operations;

namespace Codepad.Desktop.Shell;

/// <summary>
/// The main window: a menu, a workspace region and a status region. Ctrl+Tab and Ctrl+Shift+Tab cycle the tabs,
/// Ctrl+W closes the active tab and Ctrl+Shift+PageUp or PageDown moves it. A close of the window first saves every tab.
/// </summary>
public sealed partial class MainWindow : Window
{
    private bool saved;

    /// <summary>Initializes a new instance of the <see cref="MainWindow"/> class.</summary>
    public MainWindow()
    {
        InitializeComponent();

        // The tunnel route runs before the editor sees the key, so the editor cannot take Ctrl+Tab as a tab character.
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    private WorkspaceViewModel? Workspace => (DataContext as MainWindowViewModel)?.Workspace;

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        WorkspaceViewModel? workspace = Workspace;
        if (workspace is null || saved || e.CloseReason == WindowCloseReason.ApplicationShutdown || e.CloseReason == WindowCloseReason.OSShutdown)
        {
            return;
        }

        // Saving is asynchronous. The window stays open until every tab has saved, or the user closes it a second time.
        e.Cancel = true;
        _ = SaveThenCloseAsync(workspace);
    }

    private async Task SaveThenCloseAsync(WorkspaceViewModel workspace)
    {
        if (await workspace.TryCloseAllAsync())
        {
            saved = true;
            Close();
        }
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        WorkspaceViewModel? workspace = Workspace;
        if (workspace is null || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.Tab:
                e.Handled = true;
                if (shift)
                {
                    workspace.Previous();
                }
                else
                {
                    workspace.Next();
                }

                break;
            case Key.W when !shift:
                e.Handled = true;
                if (workspace.ActiveTab is ExperimentTabViewModel tab)
                {
                    await workspace.CloseAsync(tab);
                }

                break;
            case Key.PageUp when shift:
            case Key.PageDown when shift:
                e.Handled = true;
                if (workspace.ActiveTab is ExperimentTabViewModel active)
                {
                    workspace.MoveBy(active, e.Key == Key.PageUp ? -1 : 1);
                }

                break;
            default:
                break;
        }
    }

    private async void OnNewCSharpClick(object? sender, RoutedEventArgs e)
    {
        if (Workspace is WorkspaceViewModel workspace)
        {
            await workspace.NewExperimentAsync(OperationKind.CSharp);
        }
    }

    private async void OnNewJsonClick(object? sender, RoutedEventArgs e)
    {
        if (Workspace is WorkspaceViewModel workspace)
        {
            await workspace.NewExperimentAsync(OperationKind.Json);
        }
    }

    private async void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        if (Workspace is WorkspaceViewModel workspace && workspace.ActiveTab is ExperimentTabViewModel tab)
        {
            await workspace.CloseAsync(tab);
        }
    }

    private void OnNextTabClick(object? sender, RoutedEventArgs e) => Workspace?.Next();

    private void OnPreviousTabClick(object? sender, RoutedEventArgs e) => Workspace?.Previous();

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();
}
