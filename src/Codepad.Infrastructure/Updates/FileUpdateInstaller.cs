using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Codepad.Application.Updates;
using Codepad.Infrastructure.Execution;

namespace Codepad.Infrastructure.Updates;

/// <summary>The facts that the installer needs about this installation.</summary>
/// <param name="InstallDir">The folder with the files of the running app.</param>
/// <param name="UpdatesDir">The folder for plans, results and helper copies. It is outside the installation.</param>
/// <param name="ExecutableName">The file name of the program inside the installation, for example <c>Codepad.Desktop</c>.</param>
/// <param name="HelperSourceDir">The folder that holds the helper files (<c>Codepad.Updater.dll</c> and its runtime files).</param>
/// <param name="FromVersion">The installed version.</param>
/// <param name="DatabasePath">The database file, or null.</param>
/// <param name="BackupDatabase">Writes a consistent copy of the database to a path. The update stops when it throws. Null means no database.</param>
/// <param name="AppPid">The process ID that the helper waits for.</param>
/// <param name="RestartArguments">The arguments for the new version at its first start.</param>
/// <param name="HealthTimeout">How long the helper waits for the new version to prove that it started.</param>
public sealed record FileUpdateInstallerOptions(
    string InstallDir,
    string UpdatesDir,
    string ExecutableName,
    string HelperSourceDir,
    string FromVersion,
    string? DatabasePath,
    Func<string, Task>? BackupDatabase,
    int AppPid,
    IReadOnlyList<string> RestartArguments,
    TimeSpan HealthTimeout);

/// <summary>
/// The Linux and Windows installer. It never changes the running installation. It unpacks the verified archive next to it, copies the database,
/// writes a plan and starts a helper from a copy outside the installation. The helper swaps the folders after the app
/// exits, starts the new version and puts the old one back if the new one does not prove itself. The installation has to be
/// in a folder that the user can change: a path that needs administrator rights is not supported.
/// </summary>
/// <param name="options">The facts about this installation.</param>
public sealed class FileUpdateInstaller(FileUpdateInstallerOptions options) : IUpdateInstaller
{
    /// <summary>The name of the file that the new version creates when it has started.</summary>
    public static readonly string HealthFileName = "health.ok";

    /// <summary>The name of the file that the helper writes at the end.</summary>
    public static readonly string ResultFileName = "result.json";

    private const string HELPER_NAME = "Codepad.Updater";

    /// <inheritdoc />
    public string? CheckSupported()
    {
        try
        {
            string parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(options.InstallDir)) ?? string.Empty;
            if (!Directory.Exists(options.InstallDir) || parent.Length == 0 || !File.Exists(Path.Combine(options.InstallDir, options.ExecutableName)))
            {
                return "CodePad cannot find its own files, so it cannot update itself.";
            }

            if (!CanWrite(options.InstallDir) || !CanWrite(parent))
            {
                return "CodePad is installed in a folder that you cannot change. Update it the same way you installed it.";
            }

            return HelperExists() ? null : "The update helper is missing from this installation.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "CodePad cannot check its installation folder, so it cannot update itself.";
        }
    }

    /// <inheritdoc />
    public async Task HandOffAsync(StagedUpdate staged, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(staged);
        if (CheckSupported() is not null)
        {
            throw new UpdateInstallException("not_writable");
        }

        string installDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.InstallDir));
        string newDir = installDir + ".new";
        string oldDir = installDir + ".old";
        string backupPath = Path.Combine(options.UpdatesDir, "pre-update-" + options.FromVersion + ".db");
        try
        {
            Directory.CreateDirectory(options.UpdatesDir);

            // The file sat in a folder that other programs can write. Check it again, right before use.
            await VerifyArchiveAsync(staged, cancellationToken).ConfigureAwait(false);

            DeleteDirectory(newDir);
            DeleteDirectory(oldDir);
            await ArchiveExtractor.ExtractAsync(staged.FilePath, newDir, cancellationToken).ConfigureAwait(false);
            if (!File.Exists(Path.Combine(newDir, options.ExecutableName)))
            {
                throw new UpdateInstallException("archive_incomplete");
            }

            if (options.BackupDatabase is not null && options.DatabasePath is not null)
            {
                await BackupAsync(backupPath).ConfigureAwait(false);
            }

            string healthFile = Path.Combine(options.UpdatesDir, HealthFileName);
            string resultFile = Path.Combine(options.UpdatesDir, ResultFileName);
            DeleteFile(healthFile);
            DeleteFile(resultFile);
            string planPath = Path.Combine(options.UpdatesDir, "plan.json");
            WritePlan(planPath, installDir, newDir, oldDir, healthFile, resultFile, backupPath, staged.Offer.Version.ToString());
            StartHelper(planPath);
        }
        catch (Exception exception) when (exception is not UpdateInstallException and not OperationCanceledException and not OutOfMemoryException)
        {
            DeleteDirectory(newDir);
            throw new UpdateInstallException(exception is IOException or UnauthorizedAccessException ? "io_error" : "helper_failed", exception);
        }
        catch
        {
            // A cancel or a refused archive. The installation is unchanged, and the half-written folder goes.
            DeleteDirectory(newDir);
            throw;
        }
    }

    // The helper is a framework-dependent program (a dll with an apphost) in a developer build, and one self-contained single file program in a package.
    private bool HelperExists() =>
        File.Exists(Path.Combine(options.HelperSourceDir, HELPER_NAME + ".dll")) || File.Exists(Path.Combine(options.HelperSourceDir, HELPER_NAME + (OperatingSystem.IsWindows() ? ".exe" : string.Empty)));

    private static bool CanWrite(string directory)
    {
        string probe = Path.Combine(directory, ".codepad-write-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task VerifyArchiveAsync(StagedUpdate staged, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(staged.FilePath);
        if (stream.Length != staged.Offer.Asset.Size)
        {
            throw new UpdateInstallException("archive_changed");
        }

        string actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(actual), System.Text.Encoding.ASCII.GetBytes(staged.Offer.Asset.Sha256)))
        {
            throw new UpdateInstallException("archive_changed");
        }
    }

    private async Task BackupAsync(string backupPath)
    {
        try
        {
            DeleteFile(backupPath);
            await options.BackupDatabase!(backupPath).ConfigureAwait(false);
            if (!File.Exists(backupPath))
            {
                throw new UpdateInstallException("backup_failed");
            }
        }
        catch (Exception exception) when (exception is not UpdateInstallException and not OutOfMemoryException)
        {
            // Without a copy of the data a rollback could lose work. The update does not start.
            throw new UpdateInstallException("backup_failed", exception);
        }
    }

    private void WritePlan(string planPath, string installDir, string newDir, string oldDir, string healthFile, string resultFile, string backupPath, string toVersion)
    {
        PlanFile plan = new PlanFile(
            1,
            options.AppPid,
            installDir,
            newDir,
            oldDir,
            Path.Combine(installDir, options.ExecutableName),
            options.RestartArguments,
            healthFile,
            resultFile,
            60,
            (int)options.HealthTimeout.TotalSeconds,
            options.DatabasePath,
            options.BackupDatabase is null ? null : backupPath,
            options.FromVersion,
            toVersion);
        File.WriteAllText(planPath, JsonSerializer.Serialize(plan, JsonSerializerOptions.Web));
    }

    // The helper runs from a copy outside the installation. The swap renames the installation, and a program cannot move the folder that it runs from.
    private void StartHelper(string planPath)
    {
        string copy = Path.Combine(options.UpdatesDir, "helper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(copy);
        foreach (string file in Directory.GetFiles(options.HelperSourceDir, HELPER_NAME + ".*").Where(path => !path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)))
        {
            File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
        }

        string apphost = Path.Combine(copy, HELPER_NAME + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        ProcessStartInfo info;
        if (File.Exists(apphost))
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(apphost, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            info = new ProcessStartInfo(apphost);
        }
        else
        {
            info = new ProcessStartInfo(WorkerLaunchOptions.ResolveDotnetHost());
            info.ArgumentList.Add(Path.Combine(copy, HELPER_NAME + ".dll"));
        }

        info.ArgumentList.Add(planPath);
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.WorkingDirectory = copy;
        using Process? helper = Process.Start(info);
        if (helper is null)
        {
            throw new UpdateInstallException("helper_failed");
        }

        // A helper that dies in the first moments, for example because of a missing runtime, is a failure that the user can still retry.
        if (helper.WaitForExit(500) && helper.ExitCode != 0)
        {
            throw new UpdateInstallException("helper_failed");
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The next update tries again.
        }
    }

    private static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The next update tries again.
        }
    }

    // The plan file. The helper reads the same names. A test checks that both sides agree.
    private sealed record PlanFile(
        int SchemaVersion,
        int AppPid,
        string InstallDir,
        string NewDir,
        string OldDir,
        string RestartPath,
        IReadOnlyList<string> RestartArguments,
        string HealthFile,
        string ResultFile,
        int WaitForExitSeconds,
        int HealthTimeoutSeconds,
        string? DatabasePath,
        string? DatabaseBackupPath,
        string FromVersion,
        string ToVersion);
}
