namespace Codepad.Domain.Operations;

/// <summary>
/// The rules for one operation kind: what starts it, how long it can run and how large its result can be.
/// An execution request holds a copy, so a later change to the settings does not change an earlier request.
/// </summary>
public sealed record OperationPolicy
{
    /// <summary>Makes a policy and checks it.</summary>
    /// <param name="kind">The operation kind.</param>
    /// <param name="version">The version of the operation semantics. A change to how the operation evaluates source adds 1.</param>
    /// <param name="trigger">What can start the operation.</param>
    /// <param name="timeLimit">The longest time that one execution can take. It must be more than zero.</param>
    /// <param name="results">The limits for the result.</param>
    /// <exception cref="ArgumentNullException"><paramref name="results"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An enum value is not defined, <paramref name="version"/> is less than 1, or <paramref name="timeLimit"/> is not more than zero.</exception>
    /// <exception cref="ArgumentException"><paramref name="kind"/> is C# and <paramref name="trigger"/> is live-eligible.</exception>
    public OperationPolicy(OperationKind kind, int version, OperationTrigger trigger, TimeSpan timeLimit, ResultPolicy results)
    {
        Guard.Defined(kind);
        Guard.Defined(trigger);
        Guard.AtLeastOne(version);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeLimit, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(results);

        // No path runs C# code automatically. See ARCHITECTURE.md, "Worker trust boundary".
        if (kind == OperationKind.CSharp && trigger != OperationTrigger.ExplicitOnly)
        {
            throw new ArgumentException("A C# operation must be explicit-only. CodePad never runs C# code automatically.", nameof(trigger));
        }

        Kind = kind;
        Version = version;
        Trigger = trigger;
        TimeLimit = timeLimit;
        Results = results;
    }

    /// <summary>Gets the default C# policy: explicit-only, version 1, a 10 second time limit and <see cref="ResultPolicy.Default"/>.</summary>
    public static OperationPolicy CSharpDefault { get; } = new OperationPolicy(OperationKind.CSharp, 1, OperationTrigger.ExplicitOnly, TimeSpan.FromSeconds(10), ResultPolicy.Default);

    /// <summary>Gets the operation kind.</summary>
    public OperationKind Kind { get; }

    /// <summary>Gets the version of the operation semantics.</summary>
    public int Version { get; }

    /// <summary>Gets what can start the operation.</summary>
    public OperationTrigger Trigger { get; }

    /// <summary>Gets the longest time that one execution can take.</summary>
    public TimeSpan TimeLimit { get; }

    /// <summary>Gets the limits for the result.</summary>
    public ResultPolicy Results { get; }
}
