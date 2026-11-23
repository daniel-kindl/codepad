namespace Codepad.Application.Experiments;

/// <summary>The save state of one open experiment. The UI shows it.</summary>
public enum SaveState
{
    /// <summary>The experiment is as stored. Nothing is waiting to be saved.</summary>
    Clean,

    /// <summary>The editor holds changes that storage does not have yet.</summary>
    Dirty,

    /// <summary>A write is in progress. The changes are not durable yet.</summary>
    Saving,

    /// <summary>The last write committed, and no later change exists.</summary>
    Saved,

    /// <summary>The last write failed. The editor still holds the changes. A retry can save them.</summary>
    Error,
}
