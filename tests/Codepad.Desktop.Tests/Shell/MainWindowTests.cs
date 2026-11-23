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

    [AvaloniaFact]
    public void KeyboardWalkThroughReachesEveryControlWithVisibleFocusAndNoTrap()
    {
        MainWindow window = ShowShell([]);
        MenuItem file = Assert.IsType<MenuItem>(FindByName(window, "File"));
        MenuItem exit = Assert.IsType<MenuItem>(FindByName(window, "Exit"));

        // Tab moves focus to the File menu and shows the focus.
        Press(window, PhysicalKey.Tab);
        Assert.Same(file, window.FocusManager?.GetFocusedElement());
        Assert.Contains(":focus-visible", file.Classes);

        // A second Tab moves to the editor. Tab inside the editor inserts a tab, so Escape leaves it for the diagnostics list.
        Press(window, PhysicalKey.Tab);
        Assert.IsType<AvaloniaEdit.Editing.TextArea>(window.FocusManager?.GetFocusedElement());
        Press(window, PhysicalKey.Escape);
        Assert.Same(FindByName(window, "Diagnostics"), window.FocusManager?.GetFocusedElement());

        // Tab from the list goes on through the result panel, reaches its tabs, and wraps around to the File menu.
        // Focus never gets trapped.
        bool reachedResultTabs = false;
        for (int step = 0; step < 8 && !ReferenceEquals(window.FocusManager?.GetFocusedElement(), file); step++)
        {
            Press(window, PhysicalKey.Tab);
            reachedResultTabs |= window.FocusManager?.GetFocusedElement() is TabItem;
        }

        Assert.True(reachedResultTabs);
        Assert.Same(file, window.FocusManager?.GetFocusedElement());

        // Alt+F opens the menu and moves focus to Exit. Escape closes it and gives focus back to File.
        Press(window, PhysicalKey.F, RawInputModifiers.Alt);
        Assert.True(file.IsSubMenuOpen);
        Assert.Same(exit, window.FocusManager?.GetFocusedElement());
        Press(window, PhysicalKey.Escape);
        Assert.False(file.IsSubMenuOpen);
        Assert.Same(file, window.FocusManager?.GetFocusedElement());
    }

    internal static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
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
