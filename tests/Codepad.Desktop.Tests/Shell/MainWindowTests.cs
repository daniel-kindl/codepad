using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Codepad.Application.Startup;
using Codepad.Desktop.Shell;

namespace Codepad.Desktop.Tests.Shell;

/// <summary>Headless UI tests of the shell window. They find controls by accessible name.</summary>
public sealed class MainWindowTests
{
    [AvaloniaFact]
    public void ColdLaunchOpensOneWindowWithMenuWorkspaceAndStatus()
    {
        MainWindow window = ShowShell([]);

        Assert.True(window.IsVisible);
        Assert.Equal("CodePad", window.Title);
        Assert.IsType<Menu>(FindByName(window, "Main menu"));
        Assert.NotNull(FindByName(window, "Workspace"));
        TextBlock status = Assert.IsType<TextBlock>(FindByName(window, "Status"));
        Assert.Equal("Ready", status.Text);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
    }

    [AvaloniaFact]
    public void StartupProblemShowsInTheStatusRegion()
    {
        MainWindow window = ShowShell([new StartupNotice(StartupNoticeKind.SettingsUnusable, "invalid_json")]);

        TextBlock status = Assert.IsType<TextBlock>(FindByName(window, "Status"));
        Assert.Equal(MainWindowViewModel.SETTINGS_UNUSABLE_TEXT, status.Text);
        Assert.Contains("warning", status.Classes);
    }

    [AvaloniaFact]
    public void ExitFromTheFileMenuWithTheKeyboardClosesTheWindow()
    {
        MainWindow window = ShowShell([]);

        // Alt+F opens the File menu. X activates its Exit item. The window closes on the key press,
        // so the test sends no key release after it.
        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Alt);
        window.KeyReleaseQwerty(PhysicalKey.F, RawInputModifiers.Alt);
        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.None);

        Assert.False(window.IsVisible);
    }

    internal static MainWindow ShowShell(IReadOnlyList<StartupNotice> notices)
    {
        MainWindow window = new MainWindow { DataContext = new MainWindowViewModel(notices) };
        window.Show();
        return window;
    }

    internal static Control? FindByName(Window window, string accessibleName) =>
        window.GetLogicalDescendants().OfType<Control>().SingleOrDefault(control => AutomationProperties.GetName(control) == accessibleName);
}
