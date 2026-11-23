namespace Codepad.Infrastructure.Startup;

/// <summary>The per-user folders of CodePad.</summary>
/// <param name="DataFolder">The folder for user data, such as the experiment database.</param>
/// <param name="ConfigFolder">The folder for the settings file.</param>
public sealed record AppFolderPaths(string DataFolder, string ConfigFolder);

/// <summary>
/// Platform adapter that finds the per-user folders. Windows uses <c>LocalApplicationData</c>.
/// Linux uses the XDG data and config folders. Issue #3 owns the final XDG fallback rules (V-07).
/// </summary>
public static class AppFolderLocator
{
    /// <summary>Finds the folders for the current OS and user.</summary>
    /// <returns>The data folder and the config folder.</returns>
    public static AppFolderPaths Locate() =>
        Locate(OperatingSystem.IsWindows(), folder => Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify));

    /// <summary>Finds the folders from the given OS and special folder lookup.</summary>
    /// <param name="isWindows">True for Windows. False for Linux.</param>
    /// <param name="getFolder">Gets the path of an OS special folder.</param>
    /// <returns>The data folder and the config folder.</returns>
    internal static AppFolderPaths Locate(bool isWindows, Func<Environment.SpecialFolder, string> getFolder)
    {
        if (isWindows)
        {
            string folder = Path.Combine(getFolder(Environment.SpecialFolder.LocalApplicationData), "CodePad");
            return new AppFolderPaths(folder, folder);
        }

        // .NET maps these folders to XDG_DATA_HOME and XDG_CONFIG_HOME, with the XDG defaults
        // ~/.local/share and ~/.config when a variable is not set.
        return new AppFolderPaths(
            Path.Combine(getFolder(Environment.SpecialFolder.LocalApplicationData), "codepad"),
            Path.Combine(getFolder(Environment.SpecialFolder.ApplicationData), "codepad"));
    }
}
