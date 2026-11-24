using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Codepad.Desktop.Commands;
using Codepad.Desktop.Workspace;
using Codepad.Domain.Operations;

namespace Codepad.Desktop.Shell;

/// <summary>
/// The main window: a menu, a workspace region and a status region. Ctrl+Tab and Ctrl+Shift+Tab cycle the tabs,
/// Ctrl+W closes the active tab and Ctrl+Shift+PageUp or PageDown moves it. Ctrl+K opens the command palette,
/// Ctrl+P opens the library search and Ctrl+Enter runs the active C# tab. A close of the window first saves every tab.
/// </summary>
public sealed partial class MainWindow : Window
{
    private bool saved;
    private CommandPaletteViewModel? palette;
    private Avalonia.Input.IInputElement? focusBeforePalette;

    /// <summary>Initializes a new instance of the <see cref="MainWindow"/> class.</summary>
    public MainWindow()
    {
        InitializeComponent();

        // The tunnel route runs before the editor sees the key, so the editor cannot take Ctrl+Tab as a tab character.
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    private WorkspaceViewModel? Workspace => (DataContext as MainWindowViewModel)?.Workspace;

    /// <summary>Gets the command palette, or null when the window has no workspace. Tests use it.</summary>
    internal CommandPaletteViewModel? Palette => palette;

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (palette is not null)
        {
            palette.Opened -= OnPaletteOpened;
            palette.Closed -= OnPaletteClosed;
            palette = null;
        }

        if (DataContext is MainWindowViewModel { Workspace: WorkspaceViewModel workspace } model)
        {
            palette = new CommandPaletteViewModel(
                () => BuiltInCommands.Create(workspace, model.HasLibrary ? ShowLibrary : null, CopyResultAsync),
                model.ReportCommandFailure);
            palette.Opened += OnPaletteOpened;
            palette.Closed += OnPaletteClosed;
        }

        PaletteView.DataContext = palette;
    }

    private void OnPaletteOpened()
    {
        // The window remembers the focus before the palette takes it, so Escape can give it back.
        focusBeforePalette = FocusManager?.GetFocusedElement();
        Dispatcher.UIThread.Post(PaletteView.FocusSearch, DispatcherPriority.Loaded);
    }

    // The restore runs inside Close. A command that moves the focus runs after it, so the command wins.
    private void OnPaletteClosed()
    {
        Avalonia.Input.IInputElement? previous = focusBeforePalette;
        focusBeforePalette = null;
        if (previous is Avalonia.Controls.Control { IsEffectivelyVisible: true } control && control.IsAttachedToVisualTree() && control.Focusable)
        {
            control.Focus();
        }
        else
        {
            Focus();
        }
    }

    private void ShowLibrary()
    {
        LibraryPanel.IsVisible = true;
        LibraryContent.FocusSearch();
    }

    private async Task CopyResultAsync()
    {
        Codepad.Desktop.Results.ResultsViewModel? results = Workspace?.ActiveResults;
        string? text = results?.GetCopyText();
        if (results is null || text is null)
        {
            return;
        }

        try
        {
            IClipboard? clipboard = Clipboard;
            if (clipboard is null)
            {
                results.ReportCopyFailed();
                return;
            }

            await clipboard.SetTextAsync(text);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A clipboard failure is common on Linux, for example without a clipboard manager. It must not stop the app.
            results.ReportCopyFailed();
        }
    }

    private static async Task RunActiveAsync(WorkspaceViewModel workspace)
    {
        if (workspace.Runs is not RunController runs || workspace.ActiveTab is not ExperimentTabViewModel tab)
        {
            workspace.ShowNotice("Open a C# experiment to run it.");
            return;
        }

        Codepad.Application.Commands.CommandAvailability availability = runs.Availability(tab);
        if (!availability.Enabled)
        {
            // The key must say why nothing ran. The palette shows the same reason.
            workspace.ShowNotice(availability.Reason ?? string.Empty);
            return;
        }

        await runs.RunAsync(tab);
    }

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
        if (e.Key == Key.K && !shift)
        {
            e.Handled = true;
            palette?.Open();
            return;
        }

        // While the palette is open, it owns the keys.
        if (palette?.IsOpen == true)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.P when !shift && DataContext is MainWindowViewModel { HasLibrary: true }:
                e.Handled = true;
                ShowLibrary();
                break;
            case Key.Enter when !shift:
                e.Handled = true;
                await RunActiveAsync(workspace);
                break;
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

    private void OnOpenPaletteClick(object? sender, RoutedEventArgs e) => palette?.Open();

    private async void OnRunClick(object? sender, RoutedEventArgs e)
    {
        if (Workspace is WorkspaceViewModel workspace)
        {
            await RunActiveAsync(workspace);
        }
    }

    private void OnCancelRunClick(object? sender, RoutedEventArgs e) => Workspace?.Runs?.Cancel();

    private void OnToggleLibraryClick(object? sender, RoutedEventArgs e)
    {
        LibraryPanel.IsVisible = !LibraryPanel.IsVisible;
        if (LibraryPanel.IsVisible)
        {
            LibraryContent.FocusSearch();
        }
    }

    private void OnNextTabClick(object? sender, RoutedEventArgs e) => Workspace?.Next();

    private void OnPreviousTabClick(object? sender, RoutedEventArgs e) => Workspace?.Previous();

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();
}
