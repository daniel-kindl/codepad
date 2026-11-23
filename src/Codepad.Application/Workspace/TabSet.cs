using Codepad.Domain.Experiments;

namespace Codepad.Application.Workspace;

/// <summary>
/// The open tabs: the experiments in tab order and the active one. The set is immutable. Each change returns a new set.
/// A tab is an <see cref="ExperimentId"/>, so one experiment never has two tabs. Closing a tab only removes the
/// ID from this set. It never deletes the experiment.
/// </summary>
public sealed record TabSet
{
    private TabSet(IReadOnlyList<ExperimentId> open, ExperimentId? active)
    {
        Open = open;
        Active = active;
    }

    /// <summary>Gets the set with no tab.</summary>
    public static TabSet Empty { get; } = new TabSet([], null);

    /// <summary>Gets the open experiments in tab order.</summary>
    public IReadOnlyList<ExperimentId> Open { get; }

    /// <summary>Gets the active experiment, or null when no tab is open.</summary>
    public ExperimentId? Active { get; }

    /// <summary>Rebuilds a set from stored IDs. It drops an ID that is not valid or that repeats, and it repairs the active tab.</summary>
    /// <param name="ids">The stored IDs in tab order.</param>
    /// <param name="active">The stored active ID, or null.</param>
    /// <param name="valid">Tells whether an experiment still exists.</param>
    /// <returns>The set. When the active ID is not open, the last tab is active.</returns>
    public static TabSet Restore(IEnumerable<ExperimentId> ids, ExperimentId? active, Func<ExperimentId, bool> valid)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(valid);
        List<ExperimentId> kept = [];
        foreach (ExperimentId id in ids)
        {
            if (!kept.Contains(id) && valid(id))
            {
                kept.Add(id);
            }
        }

        ExperimentId? repaired = active is not null && kept.Contains(active) ? active : kept.Count > 0 ? kept[^1] : null;
        return new TabSet(kept, repaired);
    }

    /// <summary>Opens a tab for an experiment and activates it. An open experiment keeps its tab and only becomes active.</summary>
    /// <param name="id">The experiment.</param>
    /// <returns>The new set.</returns>
    public TabSet OpenTab(ExperimentId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Open.Contains(id) ? new TabSet(Open, id) : new TabSet([.. Open, id], id);
    }

    /// <summary>Activates an open tab. An unknown ID changes nothing.</summary>
    /// <param name="id">The experiment.</param>
    /// <returns>The new set.</returns>
    public TabSet Activate(ExperimentId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Open.Contains(id) ? new TabSet(Open, id) : this;
    }

    /// <summary>Closes a tab. When it was active, the tab on its right becomes active, or the tab on its left when it was the last.</summary>
    /// <param name="id">The experiment.</param>
    /// <returns>The new set.</returns>
    public TabSet Close(ExperimentId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        int index = IndexOf(id);
        if (index < 0)
        {
            return this;
        }

        List<ExperimentId> remaining = [.. Open];
        remaining.RemoveAt(index);
        ExperimentId? active = Active;
        if (id == Active)
        {
            active = remaining.Count == 0 ? null : remaining[Math.Min(index, remaining.Count - 1)];
        }

        return new TabSet(remaining, active);
    }

    /// <summary>Moves a tab to a new position. A position outside the list stops at the nearest end. The active tab stays active.</summary>
    /// <param name="id">The experiment.</param>
    /// <param name="index">The new position, from 0.</param>
    /// <returns>The new set.</returns>
    public TabSet Move(ExperimentId id, int index)
    {
        ArgumentNullException.ThrowIfNull(id);
        int from = IndexOf(id);
        if (from < 0)
        {
            return this;
        }

        List<ExperimentId> order = [.. Open];
        order.RemoveAt(from);
        order.Insert(Math.Clamp(index, 0, order.Count), id);
        return new TabSet(order, Active);
    }

    /// <summary>Activates the next tab. After the last tab it goes to the first.</summary>
    /// <returns>The new set.</returns>
    public TabSet Next() => Step(1);

    /// <summary>Activates the previous tab. Before the first tab it goes to the last.</summary>
    /// <returns>The new set.</returns>
    public TabSet Previous() => Step(-1);

    private TabSet Step(int direction)
    {
        if (Open.Count == 0)
        {
            return this;
        }

        int current = Active is null ? -1 : IndexOf(Active);
        int target = current < 0 ? 0 : (current + direction + Open.Count) % Open.Count;
        return new TabSet(Open, Open[target]);
    }

    private int IndexOf(ExperimentId id)
    {
        for (int index = 0; index < Open.Count; index++)
        {
            if (Open[index] == id)
            {
                return index;
            }
        }

        return -1;
    }
}
