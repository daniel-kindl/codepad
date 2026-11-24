namespace Codepad.Updater;

/// <summary>
/// What the helper must do. CodePad writes this file before it exits. The helper trusts the folder of the user that holds
/// it, but it still checks that the three folders are neighbors and that the program to restart is inside the installation.
/// </summary>
/// <param name="SchemaVersion">The version of this file. The helper reads version 1.</param>
/// <param name="AppPid">The process ID of CodePad. The helper waits until it ends.</param>
/// <param name="InstallDir">The folder of the installation.</param>
/// <param name="NewDir">The folder with the new version, next to the installation.</param>
/// <param name="OldDir">The folder where the old installation goes, next to the installation.</param>
/// <param name="RestartPath">The program to start after the swap. It is inside the installation.</param>
/// <param name="RestartArguments">The arguments for the program.</param>
/// <param name="HealthFile">The file that the new version creates after it started. Without it the helper restores the old version.</param>
/// <param name="ResultFile">The file where the helper writes the result. The next start of CodePad reads it.</param>
/// <param name="WaitForExitSeconds">How long the helper waits for CodePad to exit.</param>
/// <param name="HealthTimeoutSeconds">How long the helper waits for the health file.</param>
/// <param name="DatabasePath">The database of CodePad, or null.</param>
/// <param name="DatabaseBackupPath">The copy of the database from before the update, or null. A rollback puts it back.</param>
/// <param name="FromVersion">The old version.</param>
/// <param name="ToVersion">The new version.</param>
internal sealed record UpdatePlan(
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

/// <summary>The result that the helper writes for the next start of CodePad.</summary>
/// <param name="Status">One of <c>updated</c>, <c>rolled_back</c>, <c>not_applied</c> or <c>rollback_failed</c>.</param>
/// <param name="Reason">A short code, for example <c>health_timeout</c>.</param>
/// <param name="FromVersion">The old version.</param>
/// <param name="ToVersion">The new version.</param>
internal sealed record UpdateResult(string Status, string Reason, string FromVersion, string ToVersion);
