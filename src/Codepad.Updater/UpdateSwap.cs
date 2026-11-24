using System.Text.Json;

namespace Codepad.Updater;

/// <summary>A process that the helper started.</summary>
internal interface IStartedProcess
{
    bool HasExited { get; }

    void KillTree();
}

/// <summary>What the swap needs from the machine. The tests give a fake.</summary>
internal interface IUpdateHost
{
    bool IsRunning(int pid);

    IStartedProcess Start(string path, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment);

    Task DelayAsync(TimeSpan time, CancellationToken cancellationToken);

    DateTimeOffset Now { get; }
}

/// <summary>
/// Replaces the installation after CodePad exited, and puts the old one back when the new one does not start. The old
/// installation stays until the new version has created its health file. The steps are renames of whole folders, so a step
/// either happened or did not. A crash between two renames is repaired when the helper runs again.
/// </summary>
internal sealed class UpdateSwap(IUpdateHost host, Action<string> log)
{
    public const int OK = 0;
    public const int ROLLED_BACK = 10;
    public const int NOT_APPLIED = 11;
    public const int APP_STILL_RUNNING = 12;
    public const int INVALID_PLAN = 13;
    public const int ROLLBACK_FAILED = 20;

    private static readonly TimeSpan s_poll = TimeSpan.FromMilliseconds(200);

    public async Task<int> RunAsync(UpdatePlan plan, CancellationToken cancellationToken)
    {
        string? problem = Validate(plan);
        if (problem is not null)
        {
            log("invalid plan: " + problem);
            return INVALID_PLAN;
        }

        // A crash between the two renames leaves no installation. The old one is still there. Put it back first.
        if (!Directory.Exists(plan.InstallDir) && Directory.Exists(plan.OldDir))
        {
            Directory.Move(plan.OldDir, plan.InstallDir);
            Write(plan, "rolled_back", "recovered_after_interruption");
            log("recovered an interrupted swap");
            return ROLLED_BACK;
        }

        if (!await WaitForExitAsync(plan, cancellationToken).ConfigureAwait(false))
        {
            Write(plan, "not_applied", "app_still_running");
            return APP_STILL_RUNNING;
        }

        if (!File.Exists(Path.Combine(plan.NewDir, Path.GetRelativePath(plan.InstallDir, plan.RestartPath))))
        {
            Write(plan, "not_applied", "new_version_incomplete");
            return NOT_APPLIED;
        }

        TryDelete(plan.OldDir);
        if (!await SwapAsync(plan, cancellationToken).ConfigureAwait(false))
        {
            Write(plan, "not_applied", "swap_failed");
            return NOT_APPLIED;
        }

        TryDeleteFile(plan.HealthFile);
        IStartedProcess started;
        try
        {
            started = host.Start(plan.RestartPath, plan.RestartArguments, new Dictionary<string, string> { ["CODEPAD_UPDATE_HEALTH_FILE"] = plan.HealthFile });
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            return await RollBackAsync(plan, null, "start_failed", cancellationToken).ConfigureAwait(false);
        }

        string? unhealthy = await WaitForHealthAsync(plan, started, cancellationToken).ConfigureAwait(false);
        if (unhealthy is null)
        {
            // Only now the old installation goes. The user data was never inside it.
            TryDelete(plan.OldDir);
            Write(plan, "updated", "ok");
            log("update applied");
            return OK;
        }

        return await RollBackAsync(plan, started, unhealthy, cancellationToken).ConfigureAwait(false);
    }

    private static string? Validate(UpdatePlan plan)
    {
        if (plan.SchemaVersion != 1)
        {
            return "schema";
        }

        string[] paths = [plan.InstallDir, plan.NewDir, plan.OldDir, plan.RestartPath, plan.HealthFile, plan.ResultFile];
        if (paths.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)))
        {
            return "relative_path";
        }

        string parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(plan.InstallDir)) ?? string.Empty;
        if (parent.Length == 0
            || Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(plan.NewDir)) != parent
            || Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(plan.OldDir)) != parent
            || new[] { plan.InstallDir, plan.NewDir, plan.OldDir }.Distinct().Count() != 3)
        {
            return "folders_not_neighbors";
        }

        string relative = Path.GetRelativePath(plan.InstallDir, plan.RestartPath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            return "restart_outside_installation";
        }

        return plan.WaitForExitSeconds is < 0 or > 600 || plan.HealthTimeoutSeconds is < 1 or > 600 ? "timeout" : null;
    }

    private async Task<bool> WaitForExitAsync(UpdatePlan plan, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = host.Now.AddSeconds(plan.WaitForExitSeconds);
        while (host.IsRunning(plan.AppPid))
        {
            if (host.Now >= deadline)
            {
                return false;
            }

            await host.DelayAsync(s_poll, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    // On Windows a folder can stay locked for a moment after the process exits. A few tries cover it.
    private async Task<bool> SwapAsync(UpdatePlan plan, CancellationToken cancellationToken)
    {
        bool movedOld = false;
        for (int attempt = 0; attempt < 20 && !movedOld; attempt++)
        {
            try
            {
                Directory.Move(plan.InstallDir, plan.OldDir);
                movedOld = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await host.DelayAsync(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            }
        }

        if (!movedOld)
        {
            return false;
        }

        try
        {
            Directory.Move(plan.NewDir, plan.InstallDir);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The new folder cannot take the place. Put the old one back at once.
            Directory.Move(plan.OldDir, plan.InstallDir);
            return false;
        }
    }

    private async Task<string?> WaitForHealthAsync(UpdatePlan plan, IStartedProcess started, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = host.Now.AddSeconds(plan.HealthTimeoutSeconds);
        while (true)
        {
            if (File.Exists(plan.HealthFile))
            {
                return null;
            }

            if (started.HasExited)
            {
                // The health file may come just before the exit. Look once more.
                return File.Exists(plan.HealthFile) ? null : "exited_before_ready";
            }

            if (host.Now >= deadline)
            {
                return "health_timeout";
            }

            await host.DelayAsync(s_poll, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<int> RollBackAsync(UpdatePlan plan, IStartedProcess? failed, string reason, CancellationToken cancellationToken)
    {
        log("rolling back: " + reason);
        try
        {
            failed?.KillTree();
            await host.DelayAsync(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            string failedDir = plan.InstallDir + ".failed";
            TryDelete(failedDir);
            Directory.Move(plan.InstallDir, failedDir);
            Directory.Move(plan.OldDir, plan.InstallDir);
            RestoreDatabase(plan);
            TryDelete(failedDir);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Write(plan, "rollback_failed", reason);
            return ROLLBACK_FAILED;
        }

        Write(plan, "rolled_back", reason);
        try
        {
            host.Start(plan.RestartPath, plan.RestartArguments, new Dictionary<string, string>());
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            log("the old version is back but did not start");
        }

        return ROLLED_BACK;
    }

    // The new version can have changed the database schema. The old version cannot read a newer schema. The copy from before the update goes back.
    private static void RestoreDatabase(UpdatePlan plan)
    {
        if (plan.DatabasePath is null || plan.DatabaseBackupPath is null || !File.Exists(plan.DatabaseBackupPath))
        {
            return;
        }

        File.Copy(plan.DatabaseBackupPath, plan.DatabasePath, overwrite: true);
        TryDeleteFile(plan.DatabasePath + "-wal");
        TryDeleteFile(plan.DatabasePath + "-shm");
    }

    private static void Write(UpdatePlan plan, string status, string reason)
    {
        try
        {
            string? directory = Path.GetDirectoryName(plan.ResultFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(plan.ResultFile, JsonSerializer.Serialize(new UpdateResult(status, reason, plan.FromVersion, plan.ToVersion), JsonSerializerOptions.Web));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The result is a courtesy for the next start. The update does not depend on it.
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A folder that stays is removed at the next update.
        }
    }

    private static void TryDeleteFile(string path)
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
            // See above.
        }
    }
}
