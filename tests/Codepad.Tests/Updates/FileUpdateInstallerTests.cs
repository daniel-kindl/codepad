using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using Codepad.Application.Updates;
using Codepad.Infrastructure.Updates;

namespace Codepad.Tests.Updates;

/// <summary>
/// An upgrade from an installed version to a new one, with the real installer, the real helper process and small
/// scripts as the apps. It runs on Linux and Windows. It covers success, a new version that does not start, a failing step
/// before the swap, and the rule that the old installation is not touched until the new one is proven.
/// </summary>
public sealed class FileUpdateInstallerTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("codepad-install-").FullName;

    public FileUpdateInstallerTests()
    {
        InstallDir = Path.Combine(root, "app");
        UpdatesDir = Path.Combine(root, "data", "updates");
        DatabasePath = Path.Combine(root, "data", "codepad.db");
        Directory.CreateDirectory(InstallDir);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        WriteApp(InstallDir, "old", healthy: true);
        File.WriteAllText(DatabasePath, "schema 2");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // A helper process can hold a folder for a moment. The folder is temporary.
        }
    }

    private string InstallDir { get; }

    private string UpdatesDir { get; }

    private string DatabasePath { get; }

    private static string ExecutableName => OperatingSystem.IsWindows() ? "Codepad.Desktop.cmd" : "Codepad.Desktop";

    private string StartedMarker(string label) => Path.Combine(root, "started-" + label + ".txt");

    // A tiny app. It records that it started. A healthy one also creates the health file.
    private void WriteApp(string directory, string label, bool healthy, bool exitAtOnce = false)
    {
        string path = Path.Combine(directory, ExecutableName);
        string marker = StartedMarker(label);
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(path, $"@echo off\r\necho started> \"{marker}\"\r\n{(healthy ? "if defined CODEPAD_UPDATE_HEALTH_FILE echo ok> \"%CODEPAD_UPDATE_HEALTH_FILE%\"\r\n" : string.Empty)}{(exitAtOnce ? "exit /b 3\r\n" : "ping -n 6 127.0.0.1 > NUL\r\n")}");
        }
        else
        {
            File.WriteAllText(path, $"#!/bin/sh\necho started > '{marker}'\n{(healthy ? "if [ -n \"$CODEPAD_UPDATE_HEALTH_FILE\" ]; then echo ok > \"$CODEPAD_UPDATE_HEALTH_FILE\"; fi\n" : string.Empty)}{(exitAtOnce ? "exit 3\n" : "sleep 5\n")}");
            File.SetUnixFileMode(path, (UnixFileMode)0b111_101_101);
        }

        File.WriteAllText(Path.Combine(directory, "version.txt"), label);
    }

    private StagedUpdate Archive(string label, bool healthy, bool exitAtOnce = false, bool withExecutable = true)
    {
        string source = Path.Combine(root, "payload-" + label);
        Directory.CreateDirectory(source);
        if (withExecutable)
        {
            WriteApp(source, label, healthy, exitAtOnce);
        }
        else
        {
            File.WriteAllText(Path.Combine(source, "version.txt"), label);
        }

        string archive = Path.Combine(root, "codepad-" + label + (OperatingSystem.IsWindows() ? "-win-x64.zip" : "-linux-x64.tar.gz"));
        if (OperatingSystem.IsWindows())
        {
            ZipFile.CreateFromDirectory(source, archive);
        }
        else
        {
            using FileStream file = File.Create(archive);
            using GZipStream gzip = new GZipStream(file, CompressionLevel.Fastest);
            using TarWriter writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false);
            foreach (string path in Directory.GetFiles(source))
            {
                PaxTarEntry entry = new PaxTarEntry(TarEntryType.RegularFile, Path.GetFileName(path)) { Mode = File.GetUnixFileMode(path), DataStream = File.OpenRead(path) };
                writer.WriteEntry(entry);
                entry.DataStream.Dispose();
            }
        }

        byte[] bytes = File.ReadAllBytes(archive);
        string platform = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        UpdateAsset asset = new UpdateAsset(platform, Path.GetFileName(archive), bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), new Uri("https://github.com/daniel-kindl/codepad/releases/download/v0.2.0/" + Path.GetFileName(archive)));
        UpdateOffer offer = new UpdateOffer(SemanticVersion.TryParse("0.2.0", out SemanticVersion? version) ? version! : throw new InvalidOperationException(), new Uri("https://github.com/daniel-kindl/codepad/releases/tag/v0.2.0"), "notes", asset);
        return new StagedUpdate(offer, archive);
    }

    private FileUpdateInstaller Installer(int appPid, Func<string, Task>? backup = null, TimeSpan? health = null, string? helperDir = null) => new FileUpdateInstaller(new FileUpdateInstallerOptions(
        InstallDir,
        UpdatesDir,
        ExecutableName,
        helperDir ?? AppContext.BaseDirectory,
        "0.1.0",
        DatabasePath,
        backup ?? (path => { File.Copy(DatabasePath, path, overwrite: true); return Task.CompletedTask; }),
        appPid,
        ["--post-update"],
        health ?? TimeSpan.FromSeconds(8)));

    // A process that stands for the running app. It exits after a moment, like the app does after the hand-off.
    private static Process StartStandIn()
    {
        ProcessStartInfo info = OperatingSystem.IsWindows() ? new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 > NUL") : new ProcessStartInfo("sleep", "2");
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        return Process.Start(info)!;
    }

    private async Task<string> WaitForResultAsync()
    {
        string file = Path.Combine(UpdatesDir, "result.json");
        DateTime deadline = DateTime.UtcNow.AddSeconds(60);
        while (!File.Exists(file) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(file), "The helper wrote no result.");
        await Task.Delay(300, TestContext.Current.CancellationToken);
        return await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
    }

    private string Version() => File.ReadAllText(Path.Combine(InstallDir, "version.txt"));

    [Fact]
    public void AnInstallationThatTheUserCanWriteIsSupported()
    {
        Assert.Null(Installer(1).CheckSupported());
    }

    [Fact]
    public void AMissingHelperOrProgramIsNotSupportedAndSaysWhy()
    {
        string empty = Path.Combine(root, "no-helper");
        Directory.CreateDirectory(empty);

        Assert.Contains("helper is missing", Installer(1, helperDir: empty).CheckSupported(), StringComparison.Ordinal);
        File.Delete(Path.Combine(InstallDir, ExecutableName));
        Assert.Contains("cannot find its own files", Installer(1).CheckSupported(), StringComparison.Ordinal);
    }

    [Fact]
    public void AFolderThatTheUserCannotWriteIsNotSupportedAndTheMessageSaysToUseTheOriginalWayToUpdate()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root" || geteuid() == 0)
        {
            return;
        }

        File.SetUnixFileMode(InstallDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.Contains("cannot change", Installer(1).CheckSupported(), StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(InstallDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern uint geteuid();

    [Fact]
    public async Task AnUpgradeReplacesTheInstallationStartsTheNewVersionAndKeepsTheDataAndTheBackup()
    {
        StagedUpdate staged = Archive("new", healthy: true);
        using Process app = StartStandIn();

        await Installer(app.Id).HandOffAsync(staged, TestContext.Current.CancellationToken);

        // Until the app exits, the installation is untouched.
        Assert.Equal("old", Version());
        string result = await WaitForResultAsync();

        Assert.Contains("\"updated\"", result, StringComparison.Ordinal);
        Assert.Equal("new", Version());
        Assert.True(File.Exists(StartedMarker("new")), "The new version did not start.");
        Assert.False(Directory.Exists(InstallDir + ".old"));
        Assert.False(Directory.Exists(InstallDir + ".new"));
        Assert.Equal("schema 2", File.ReadAllText(DatabasePath));
        Assert.True(File.Exists(Path.Combine(UpdatesDir, "pre-update-0.1.0.db")), "The database backup is missing.");
    }

    [Fact]
    public async Task ANewVersionThatDoesNotStartIsRolledBackTheOldOneRestartsAndTheDatabaseIsRestored()
    {
        StagedUpdate staged = Archive("new", healthy: false, exitAtOnce: true);
        using Process app = StartStandIn();
        await Installer(app.Id, health: TimeSpan.FromSeconds(8)).HandOffAsync(staged, TestContext.Current.CancellationToken);

        // The "new version" changes the schema before it fails. The rollback must put the copy back.
        await Task.Delay(500, TestContext.Current.CancellationToken);
        string result = await WaitForResultAsync();

        Assert.Contains("rolled_back", result, StringComparison.Ordinal);
        Assert.Equal("old", Version());
        Assert.False(Directory.Exists(InstallDir + ".failed"));
        Assert.Equal("schema 2", File.ReadAllText(DatabasePath));

        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!File.Exists(StartedMarker("old")) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(StartedMarker("old")), "The old version was not started again.");
    }

    [Fact]
    public async Task ANewVersionThatStartsButNeverReportsHealthIsRolledBack()
    {
        StagedUpdate staged = Archive("new", healthy: false);
        using Process app = StartStandIn();

        await Installer(app.Id, health: TimeSpan.FromSeconds(3)).HandOffAsync(staged, TestContext.Current.CancellationToken);
        string result = await WaitForResultAsync();

        Assert.Contains("health_timeout", result, StringComparison.Ordinal);
        Assert.Equal("old", Version());
    }

    [Fact]
    public async Task AnArchiveWithoutTheProgramIsRefusedBeforeAnythingChanges()
    {
        StagedUpdate staged = Archive("broken", healthy: true, withExecutable: false);

        UpdateInstallException error = await Assert.ThrowsAsync<UpdateInstallException>(() => Installer(1).HandOffAsync(staged, TestContext.Current.CancellationToken));

        Assert.Equal("archive_incomplete", error.Code);
        Assert.Equal("old", Version());
        Assert.False(Directory.Exists(InstallDir + ".new"));
    }

    [Fact]
    public async Task AnArchiveThatChangedAfterTheDownloadIsRefused()
    {
        StagedUpdate staged = Archive("new", healthy: true);
        await File.AppendAllTextAsync(staged.FilePath, "tampered", TestContext.Current.CancellationToken);

        UpdateInstallException error = await Assert.ThrowsAsync<UpdateInstallException>(() => Installer(1).HandOffAsync(staged, TestContext.Current.CancellationToken));

        Assert.Equal("archive_changed", error.Code);
        Assert.Equal("old", Version());
    }

    [Fact]
    public async Task AFailedDatabaseBackupStopsTheUpdateAndLeavesTheInstallationAlone()
    {
        StagedUpdate staged = Archive("new", healthy: true);

        UpdateInstallException error = await Assert.ThrowsAsync<UpdateInstallException>(() => Installer(1, backup: _ => throw new IOException("disk full")).HandOffAsync(staged, TestContext.Current.CancellationToken));

        Assert.Equal("backup_failed", error.Code);
        Assert.Equal("old", Version());
        Assert.False(Directory.Exists(InstallDir + ".new"));
        Assert.False(File.Exists(Path.Combine(UpdatesDir, "plan.json")));
    }

    [Fact]
    public async Task ABackupThatWritesNoFileIsAFailure()
    {
        StagedUpdate staged = Archive("new", healthy: true);

        UpdateInstallException error = await Assert.ThrowsAsync<UpdateInstallException>(() => Installer(1, backup: _ => Task.CompletedTask).HandOffAsync(staged, TestContext.Current.CancellationToken));

        Assert.Equal("backup_failed", error.Code);
    }

    [Fact]
    public async Task ACanceledHandOffLeavesNoFolderBehind()
    {
        StagedUpdate staged = Archive("new", healthy: true);
        using CancellationTokenSource cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Installer(1).HandOffAsync(staged, cancel.Token));

        Assert.Equal("old", Version());
        Assert.False(Directory.Exists(InstallDir + ".new"));
    }

    [Fact]
    public async Task TheLogCarriesNoPathsInTheFailureCodes()
    {
        StagedUpdate staged = Archive("broken", healthy: true, withExecutable: false);

        UpdateInstallException error = await Assert.ThrowsAsync<UpdateInstallException>(() => Installer(1).HandOffAsync(staged, TestContext.Current.CancellationToken));

        Assert.DoesNotContain(root, error.Message, StringComparison.Ordinal);
    }
}
