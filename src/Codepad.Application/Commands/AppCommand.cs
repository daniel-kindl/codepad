namespace Codepad.Application.Commands;

/// <summary>
/// One built-in action of the app. The registry is in the process: a command has no path, no shell text and no plugin.
/// The handler is code of the app.
/// </summary>
public sealed class AppCommand
{
    /// <summary>Initializes a new instance of the <see cref="AppCommand"/> class.</summary>
    /// <param name="id">The stable ID, for example <c>run.csharp</c>.</param>
    /// <param name="label">The name that the user sees and searches.</param>
    /// <param name="availability">Tells whether the command can run now. The palette calls it each time it lists the commands.</param>
    /// <param name="execute">The handler. The palette calls it only when the command is available.</param>
    /// <param name="keyHint">The key binding to show, or null.</param>
    /// <param name="keywords">Other words that find the command, or null.</param>
    public AppCommand(string id, string label, Func<CommandAvailability> availability, Func<Task> execute, string? keyHint = null, string? keywords = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(execute);
        Id = id;
        Label = label;
        Availability = availability;
        Execute = execute;
        KeyHint = keyHint;
        Keywords = keywords;
    }

    /// <summary>Gets the stable ID.</summary>
    public string Id { get; }

    /// <summary>Gets the name that the user sees and searches.</summary>
    public string Label { get; }

    /// <summary>Gets the function that tells whether the command can run now.</summary>
    public Func<CommandAvailability> Availability { get; }

    /// <summary>Gets the handler.</summary>
    public Func<Task> Execute { get; }

    /// <summary>Gets the key binding to show, or null.</summary>
    public string? KeyHint { get; }

    /// <summary>Gets other words that find the command, or null.</summary>
    public string? Keywords { get; }
}
