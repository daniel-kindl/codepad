using Codepad.Infrastructure.Startup;

namespace Codepad.Tests.Startup;

public sealed class AppFolderLocatorTests
{
    private static readonly string LocalAppData = Path.Combine(Path.GetTempPath(), "local");
    private static readonly string RoamingAppData = Path.Combine(Path.GetTempPath(), "roaming");

    [Fact]
    public void LocateOnWindowsPutsDataAndConfigInLocalApplicationData()
    {
        AppFolderPaths paths = AppFolderLocator.Locate(isWindows: true, FakeFolders);

        string expected = Path.Combine(LocalAppData, "CodePad");
        Assert.Equal(new AppFolderPaths(expected, expected), paths);
    }

    [Fact]
    public void LocateOnLinuxUsesTheXdgDataAndConfigFolders()
    {
        // On Linux, .NET maps LocalApplicationData to XDG_DATA_HOME and ApplicationData to XDG_CONFIG_HOME.
        AppFolderPaths paths = AppFolderLocator.Locate(isWindows: false, FakeFolders);

        Assert.Equal(new AppFolderPaths(Path.Combine(LocalAppData, "codepad"), Path.Combine(RoamingAppData, "codepad")), paths);
    }

    private static string FakeFolders(Environment.SpecialFolder folder) => folder switch
    {
        Environment.SpecialFolder.LocalApplicationData => LocalAppData,
        Environment.SpecialFolder.ApplicationData => RoamingAppData,
        _ => throw new ArgumentOutOfRangeException(nameof(folder), folder, "The locator asked for an unexpected folder."),
    };
}
