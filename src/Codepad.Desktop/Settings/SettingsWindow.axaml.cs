using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Codepad.Desktop.Settings;

/// <summary>
/// The settings window. A valid change applies at once. An invalid number shows a message and changes nothing. Clearing
/// the run history asks for a confirmation that names what it keeps. Escape and Close close the window.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="SettingsWindow"/> class.</summary>
    public SettingsWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ThemeBox.Focus();
    }

    /// <summary>Gets the font size box. Tests use it.</summary>
    internal NumericUpDown FontSizeInput => FontSizeBox;

    /// <summary>Gets the clear history button. Tests use it.</summary>
    internal Button ClearHistory => ClearHistoryButton;

    /// <summary>Gets or sets how the window shows the confirmation. Null shows a modal dialog. Tests set it to see the dialog.</summary>
    internal Func<ConfirmWindow, Task<bool>>? ConfirmRunner { get; set; }

    private SettingsViewModel? Model => DataContext as SettingsViewModel;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnResetClick(object? sender, RoutedEventArgs e) => Model?.ResetToDefaults();

    private async void OnClearHistoryClick(object? sender, RoutedEventArgs e)
    {
        if (Model is null)
        {
            return;
        }

        ConfirmWindow dialog = new ConfirmWindow(
            "Clear run history",
            "This deletes all saved runs of all experiments. Your experiments and their source stay. You cannot undo this.",
            "Delete runs");
        if (await (ConfirmRunner?.Invoke(dialog) ?? dialog.ShowDialog<bool>(this)))
        {
            await Model.ClearHistoryAsync();
        }
    }

    private async void OnCheckUpdatesClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not null)
        {
            await Model.CheckForUpdatesAsync();
        }
    }
}
