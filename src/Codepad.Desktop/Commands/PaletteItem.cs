using Codepad.Application.Commands;

namespace Codepad.Desktop.Commands;

/// <summary>One row of the command palette.</summary>
/// <param name="Command">The command.</param>
/// <param name="IsEnabled">Whether the command can run.</param>
/// <param name="Reason">Why the command cannot run, or null.</param>
public sealed record PaletteItem(AppCommand Command, bool IsEnabled, string? Reason)
{
    /// <summary>Gets the label.</summary>
    public string Label => Command.Label;

    /// <summary>Gets the key binding, or an empty string.</summary>
    public string KeyHint => Command.KeyHint ?? string.Empty;

    /// <summary>Gets the reason for a disabled command, or an empty string. The row shows it as text.</summary>
    public string ReasonText => IsEnabled ? string.Empty : Reason ?? string.Empty;

    /// <summary>Gets the word that marks a disabled command. A disabled command is never shown by color alone.</summary>
    public string StateText => IsEnabled ? string.Empty : "Unavailable";

    /// <summary>Gets the text that a screen reader reads for the row.</summary>
    public string AccessibleName
    {
        get
        {
            string key = KeyHint.Length > 0 ? $", {KeyHint}" : string.Empty;
            return IsEnabled ? $"{Label}{key}" : $"{Label}{key}, unavailable: {Reason}";
        }
    }
}
