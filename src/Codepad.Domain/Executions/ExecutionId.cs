namespace Codepad.Domain.Executions;

/// <summary>
/// The identity of one execution and of its request. Each run, rerun included, gets a new ID.
/// Two IDs with the same value are equal.
/// </summary>
public sealed record ExecutionId
{
    /// <summary>Makes an ID from a stored value.</summary>
    /// <param name="value">The value. It cannot be <see cref="Guid.Empty"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <see cref="Guid.Empty"/>.</exception>
    public ExecutionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An execution ID cannot be the empty GUID.", nameof(value));
        }

        Value = value;
    }

    /// <summary>Gets the value of the ID.</summary>
    public Guid Value { get; }

    /// <summary>Makes a new, unique ID. The value is a version 7 GUID, so new IDs sort by creation time.</summary>
    /// <returns>A new ID.</returns>
    public static ExecutionId New() => new ExecutionId(Guid.CreateVersion7());

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
