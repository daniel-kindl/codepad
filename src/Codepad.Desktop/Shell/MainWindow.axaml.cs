using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Codepad.Desktop.Shell;

/// <summary>The main window: a menu, a workspace region and a status region.</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="MainWindow"/> class.</summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();
}
