namespace Codepad.Application.Commands;

/// <summary>Tells whether a command can run now. A command that cannot run has a reason that the user can read.</summary>
public sealed record CommandAvailability
{
    /// <summary>Initializes a new instance of the <see cref="CommandAvailability"/> class.</summary>
    /// <param name="enabled">True when the command can run.</param>
    /// <param name="reason">Why the command cannot run. It is required when <paramref name="enabled"/> is false.</param>
    public CommandAvailability(bool enabled, string? reason = null)
    {
        if (!enabled && string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A command that cannot run needs a reason.", nameof(reason));
        }

        Enabled = enabled;
        Reason = enabled ? null : reason;
    }

    /// <summary>Gets the availability of a command that can always run.</summary>
    public static CommandAvailability Available { get; } = new CommandAvailability(true);

    /// <summary>Gets a value indicating whether the command can run.</summary>
    public bool Enabled { get; }

    /// <summary>Gets the reason why the command cannot run, or null.</summary>
    public string? Reason { get; }
}
