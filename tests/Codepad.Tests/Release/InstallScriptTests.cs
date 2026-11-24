using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Codepad.Tests.Release;

/// <summary>
/// Runs the real install and uninstall scripts of the package with a temporary home folder. The Linux script runs with bash on Linux. The
/// PowerShell script runs on Windows (and where pwsh exists). Both leave user data alone.
/// </summary>
public sealed class InstallScriptTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("codepad-install-script-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static string Packaging
    {
        get
        {
            string? folder = AppContext.BaseDirectory;
            while (folder is not null && !Directory.Exists(Path.Combine(folder, "packaging")))
            {
                folder = Path.GetDirectoryName(folder);
            }

            return folder is null ? throw new InvalidOperationException("The packaging folder was not found.") : Path.Combine(folder, "packaging");
        }
    }

    private string Home => Path.Combine(root, "home");

    private string MakeTarGz(string label)
    {
        string archive = Path.Combine(root, $"codepad-{label}-linux-x64.tar.gz");
        using FileStream file = File.Create(archive);
        using GZipStream gzip = new GZipStream(file, CompressionLevel.Fastest);
        using TarWriter writer = new TarWriter(gzip, TarEntryFormat.Ustar, leaveOpen: false);
        foreach ((string name, string content) in new[] { ("Codepad.Desktop", $"#!/bin/sh\necho desktop {label}\n"), ("codepad", $"#!/bin/sh\necho cli {label}\n"), ("docs/readme.md", "docs") })
        {
            UstarTarEntry entry = new UstarTarEntry(TarEntryType.RegularFile, name) { Mode = name.StartsWith("docs", StringComparison.Ordinal) ? (UnixFileMode)0b110_100_100 : (UnixFileMode)0b111_101_101, DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)) };
            writer.WriteEntry(entry);
        }

        return archive;
    }

    private (int ExitCode, string Output) Bash(string script, params string[] arguments)
    {
        ProcessStartInfo info = new ProcessStartInfo("bash") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(Path.Combine(Packaging, script));
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["HOME"] = Home;
        info.Environment["XDG_DATA_HOME"] = Path.Combine(Home, ".local", "share");
        info.Environment["XDG_CONFIG_HOME"] = Path.Combine(Home, ".config");
        foreach (string name in new[] { "CODEPAD_INSTALL_DIR", "CODEPAD_BIN_DIR", "CODEPAD_APPLICATIONS_DIR" })
        {
            info.Environment.Remove(name);
        }

        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private string Install => Path.Combine(Home, ".local", "share", "CodePad", "app");

    [Fact]
    public void LinuxInstallPutsTheAppInTheUserFolderLinksTheCommandAndWritesALauncher()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        (int exit, string output) = Bash("install.sh", MakeTarGz("0.1.0"));

        Assert.True(exit == 0, output);
        Assert.True(File.GetUnixFileMode(Path.Combine(Install, "Codepad.Desktop")).HasFlag(UnixFileMode.UserExecute));
        string link = Path.Combine(Home, ".local", "bin", "codepad");
        Assert.True(new FileInfo(link).LinkTarget is not null);
        Assert.Equal(Path.Combine(Install, "codepad"), new FileInfo(link).LinkTarget);
        string desktop = File.ReadAllText(Path.Combine(Home, ".local", "share", "applications", "codepad.desktop"));
        Assert.Contains("Exec=\"" + Path.Combine(Install, "Codepad.Desktop") + "\"", desktop, StringComparison.Ordinal);
        Assert.Contains("Terminal=false", desktop, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Install + ".new"));
    }

    [Fact]
    public void LinuxInstallAgainUpdatesTheFilesAndLeavesTheUserDataAlone()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        Bash("install.sh", MakeTarGz("0.1.0"));
        string data = Path.Combine(Home, ".local", "share", "codepad");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "codepad.db"), "my experiments");

        (int exit, string output) = Bash("install.sh", MakeTarGz("0.2.0"));

        Assert.True(exit == 0, output);
        Assert.Contains("desktop 0.2.0", File.ReadAllText(Path.Combine(Install, "Codepad.Desktop")), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Install + ".old"));
        Assert.Equal("my experiments", File.ReadAllText(Path.Combine(data, "codepad.db")));
    }

    [Fact]
    public void LinuxInstallRefusesAWrongDigestAndChangesNothing()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        Bash("install.sh", MakeTarGz("0.1.0"));

        (int exit, string output) = Bash("install.sh", MakeTarGz("0.2.0"), "--sha256", new string('0', 64));

        Assert.NotEqual(0, exit);
        Assert.Contains("digest", output, StringComparison.Ordinal);
        Assert.Contains("desktop 0.1.0", File.ReadAllText(Path.Combine(Install, "Codepad.Desktop")), StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxInstallChecksTheSha256sumsFileNextToTheArchiveAndAcceptsTheRightDigest()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string archive = MakeTarGz("0.3.0");
        string digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive)));
        File.WriteAllText(Path.Combine(root, "SHA256SUMS"), digest + "  " + Path.GetFileName(archive) + "\n");

        Assert.Equal(0, Bash("install.sh", archive).ExitCode);

        File.WriteAllText(Path.Combine(root, "SHA256SUMS"), new string('1', 64) + "  " + Path.GetFileName(archive) + "\n");
        Assert.NotEqual(0, Bash("install.sh", archive).ExitCode);
    }

    [Fact]
    public void LinuxInstallRefusesAnArchiveWithoutTheProgramsAndAMissingFile()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string archive = Path.Combine(root, "bad.tar.gz");
        using (FileStream file = File.Create(archive))
        using (GZipStream gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (TarWriter writer = new TarWriter(gzip, TarEntryFormat.Ustar, leaveOpen: false))
        {
            writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "readme.txt") { DataStream = new MemoryStream([1]) });
        }

        Assert.NotEqual(0, Bash("install.sh", archive).ExitCode);
        Assert.False(Directory.Exists(Install));
        Assert.NotEqual(0, Bash("install.sh", Path.Combine(root, "missing.tar.gz")).ExitCode);
    }

    [Fact]
    public void LinuxScriptsRefuseAnInstallFolderThatIsTheHomeFolderAndTouchNothing()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        Directory.CreateDirectory(Path.Combine(Home, "app"));
        File.WriteAllText(Path.Combine(Home, "app", "precious.txt"), "keep");
        ProcessStartInfo info = new ProcessStartInfo("bash") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(Path.Combine(Packaging, "uninstall.sh"));
        info.Environment["HOME"] = Home;
        info.Environment["CODEPAD_INSTALL_DIR"] = Home;
        using Process process = Process.Start(info)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.NotEqual(0, process.ExitCode);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(Home, "app", "precious.txt")));
    }

    [Fact]
    public void LinuxInstallFailsWhenTheSha256sumsFileDoesNotListTheArchiveAndMatchesWholeNames()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string archive = MakeTarGz("0.4.0");
        string digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive)));
        File.WriteAllText(Path.Combine(root, "SHA256SUMS"), new string('2', 64) + "  other.tar.gz\n" + digest + "  " + Path.GetFileName(archive) + ".sig\n");

        (int exit, string output) = Bash("install.sh", archive);

        Assert.NotEqual(0, exit);
        Assert.Contains("does not list", output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Install));
    }

    [Fact]
    public void LinuxInstallKeepsAnExistingProgramThatIsNotOurLink()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "codepad"), "another program");

        (int exit, string output) = Bash("install.sh", MakeTarGz("0.1.0"));

        Assert.True(exit == 0, output);
        Assert.Equal("another program", File.ReadAllText(Path.Combine(bin, "codepad")));
        Assert.Contains("not a link", output, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxUninstallRemovesTheAppButKeepsTheDataUnlessAskedToPurge()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        Bash("install.sh", MakeTarGz("0.1.0"));
        string data = Path.Combine(Home, ".local", "share", "codepad");
        string config = Path.Combine(Home, ".config", "codepad");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(data, "codepad.db"), "mine");
        File.WriteAllText(Path.Combine(config, "settings.json"), "{}");

        (int exit, string output) = Bash("uninstall.sh");

        Assert.True(exit == 0, output);
        Assert.False(Directory.Exists(Install));
        Assert.False(File.Exists(Path.Combine(Home, ".local", "bin", "codepad")));
        Assert.False(File.Exists(Path.Combine(Home, ".local", "share", "applications", "codepad.desktop")));
        Assert.True(File.Exists(Path.Combine(data, "codepad.db")));
        Assert.True(File.Exists(Path.Combine(config, "settings.json")));

        Assert.Equal(0, Bash("uninstall.sh", "--purge-data").ExitCode);
        Assert.False(Directory.Exists(data));
        Assert.False(Directory.Exists(config));
    }

    [Fact]
    public void LinuxUninstallNeverRemovesALinkThatIsNotOurs()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "codepad"), "another program with this name");

        Assert.Equal(0, Bash("uninstall.sh").ExitCode);

        Assert.True(File.Exists(Path.Combine(bin, "codepad")));
    }

    [Fact]
    public void WindowsScriptsInstallUpdateAndRemoveWithTheDataKept()
    {
        string? pwsh = FindPowerShell();
        if (pwsh is null || !OperatingSystem.IsWindows())
        {
            return;
        }

        string archive = Path.Combine(root, "codepad-0.1.0-win-x64.zip");
        string payload = Path.Combine(root, "payload");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "Codepad.Desktop.exe"), "desktop");
        File.WriteAllText(Path.Combine(payload, "codepad.exe"), "cli");
        ZipFile.CreateFromDirectory(payload, archive);
        string install = Path.Combine(root, "win-install");
        string local = Path.Combine(root, "local");
        Directory.CreateDirectory(Path.Combine(local, "CodePad"));
        File.WriteAllText(Path.Combine(local, "CodePad", "codepad.db"), "mine");

        (int exit, string output) = RunPowerShell(pwsh, "install.ps1", install, local, "-Archive", archive);
        Assert.True(exit == 0, output);
        Assert.True(File.Exists(Path.Combine(install, "app", "Codepad.Desktop.exe")));

        File.WriteAllText(Path.Combine(payload, "Codepad.Desktop.exe"), "desktop v2");
        string second = Path.Combine(root, "codepad-0.2.0-win-x64.zip");
        ZipFile.CreateFromDirectory(payload, second);
        Assert.Equal(0, RunPowerShell(pwsh, "install.ps1", install, local, "-Archive", second).ExitCode);
        Assert.Equal("desktop v2", File.ReadAllText(Path.Combine(install, "app", "Codepad.Desktop.exe")));

        Assert.NotEqual(0, RunPowerShell(pwsh, "install.ps1", install, local, "-Archive", second, "-Sha256", new string('0', 64)).ExitCode);

        Assert.Equal(0, RunPowerShell(pwsh, "uninstall.ps1", install, local).ExitCode);
        Assert.False(Directory.Exists(Path.Combine(install, "app")));
        Assert.True(File.Exists(Path.Combine(local, "CodePad", "codepad.db")));

        Assert.Equal(0, RunPowerShell(pwsh, "uninstall.ps1", install, local, "-PurgeData").ExitCode);
        Assert.False(Directory.Exists(Path.Combine(local, "CodePad")));
    }

    private static string? FindPowerShell()
    {
        foreach (string name in new[] { "pwsh", "powershell" })
        {
            try
            {
                using Process? probe = Process.Start(new ProcessStartInfo(name, "-NoProfile -Command exit 0") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
                if (probe is not null)
                {
                    probe.WaitForExit();
                    if (probe.ExitCode == 0)
                    {
                        return name;
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // This name is not installed. Try the next one.
            }
        }

        return null;
    }

    private (int ExitCode, string Output) RunPowerShell(string shell, string script, string installDir, string localAppData, params string[] arguments)
    {
        ProcessStartInfo info = new ProcessStartInfo(shell) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(Path.Combine(Packaging, script));
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["CODEPAD_INSTALL_DIR"] = installDir;
        info.Environment["LOCALAPPDATA"] = localAppData;
        info.Environment["APPDATA"] = Path.Combine(root, "roaming");
        info.Environment["CODEPAD_SKIP_SHORTCUT"] = "1";
        info.Environment["CODEPAD_SKIP_PATH"] = "1";
        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
