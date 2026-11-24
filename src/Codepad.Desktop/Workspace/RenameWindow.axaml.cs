using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Codepad.Desktop.Workspace;

/// <summary>A small dialog that asks for a new title. It returns the trimmed title, or null when the user cancels.</summary>
public sealed partial class RenameWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="RenameWindow"/> class.</summary>
    public RenameWindow()
    {
        InitializeComponent();
        TitleBox.MaxLength = ExperimentTabViewModel.MaxTitleLength;
        TitleBox.TextChanged += (_, _) => OkButton.IsEnabled = !string.IsNullOrWhiteSpace(TitleBox.Text);
        Opened += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        };
    }

    /// <summary>Gets or sets the title that the box shows.</summary>
    internal string EnteredTitle
    {
        get => TitleBox.Text ?? string.Empty;
        set => TitleBox.Text = value;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(EnteredTitle.Trim());

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
