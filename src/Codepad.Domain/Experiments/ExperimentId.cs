namespace Codepad.Domain.Experiments;

/// <summary>
/// The stable identity of an experiment. An edit to the experiment does not change it.
/// Two IDs with the same value are equal.
/// </summary>
public sealed record ExperimentId
{
    /// <summary>Makes an ID from a stored value.</summary>
    /// <param name="value">The value. It cannot be <see cref="Guid.Empty"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <see cref="Guid.Empty"/>.</exception>
    public ExperimentId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An experiment ID cannot be the empty GUID.", nameof(value));
        }

        Value = value;
    }

    /// <summary>Gets the value of the ID.</summary>
    public Guid Value { get; }

    /// <summary>Makes a new, unique ID. The value is a version 7 GUID, so new IDs sort by creation time.</summary>
    /// <returns>A new ID.</returns>
    public static ExperimentId New() => new ExperimentId(Guid.CreateVersion7());

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
