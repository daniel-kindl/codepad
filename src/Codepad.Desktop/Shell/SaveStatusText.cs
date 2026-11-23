using Codepad.Application.Experiments;

namespace Codepad.Desktop.Shell;

/// <summary>The words that the UI shows for the save state of the open experiment.</summary>
public static class SaveStatusText
{
    private const string KEPT = "Your text is still in the editor.";
    private const string STALE_REASON = "stale_revision";
    private const string STALE_TEXT = "This experiment was changed somewhere else, so a retry cannot work.";

    /// <summary>Gets the text for a save state. It never shows the technical reason code.</summary>
    /// <param name="state">The save state.</param>
    /// <param name="errorReason">The reason code from <see cref="AutosaveSession.ErrorReason"/>. Used only for <see cref="SaveState.Error"/>.</param>
    /// <returns>The text. Empty for <see cref="SaveState.Clean"/>.</returns>
    public static string For(SaveState state, string? errorReason) => state switch
    {
        SaveState.Clean => string.Empty,
        SaveState.Dirty => "Unsaved changes",
        SaveState.Saving => "Saving…",
        SaveState.Saved => "Saved",
        SaveState.Error when errorReason == STALE_REASON => $"Couldn't save. {STALE_TEXT} {KEPT}",
        SaveState.Error => $"Couldn't save. {Cause(errorReason)} {KEPT} Retry to save it.",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "No status text for this save state."),
    };

    /// <summary>Tells whether the UI shows a Retry action for a save state.</summary>
    /// <param name="state">The save state.</param>
    /// <param name="errorReason">The reason code from <see cref="AutosaveSession.ErrorReason"/>.</param>
    /// <returns>True for <see cref="SaveState.Error"/>, except after a stale revision: the same write would fail again.</returns>
    public static bool OffersRetry(SaveState state, string? errorReason) => state == SaveState.Error && errorReason != STALE_REASON;

    private static string Cause(string? errorReason) => errorReason switch
    {
        "disk_full" => "The disk is full. Free some space.",
        "read_only" => "The data folder is read-only.",
        "busy" => "The database is in use by another program.",
        _ => "CodePad can't write to its database.",
    };
}
