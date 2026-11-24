using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Codepad.Desktop.Settings;

/// <summary>
/// A small dialog that asks the user to confirm an action that cannot be undone. Cancel is the default button and has
/// the focus at first, so Enter does not confirm. The dialog returns true only for the confirm button.
/// </summary>
public sealed partial class ConfirmWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="ConfirmWindow"/> class.</summary>
    /// <param name="title">The title of the dialog.</param>
    /// <param name="message">The question. It says what the action deletes and what it keeps.</param>
    /// <param name="confirmText">The text of the confirm button, for example "Delete runs".</param>
    public ConfirmWindow(string title, string message, string confirmText)
        : this()
    {
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        AutomationProperties.SetName(ConfirmButton, confirmText);
        AutomationProperties.SetName(this, title);
    }

    /// <summary>Initializes a new instance of the <see cref="ConfirmWindow"/> class for the XAML loader.</summary>
    public ConfirmWindow()
    {
        InitializeComponent();
        Opened += (_, _) => CancelButton.Focus();
    }

    /// <summary>Gets the confirm button. Tests use it.</summary>
    internal Button Confirm => ConfirmButton;

    /// <summary>Gets the cancel button. Tests use it.</summary>
    internal Button Cancel => CancelButton;

    /// <summary>Gets the answer of the user: true after the confirm button, false after Cancel or Escape, null while the dialog is open.</summary>
    internal bool? Outcome { get; private set; }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        Outcome = true;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Outcome = false;
        Close(false);
    }
}
