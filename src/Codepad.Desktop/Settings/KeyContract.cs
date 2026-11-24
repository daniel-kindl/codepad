namespace Codepad.Desktop.Settings;

/// <summary>One shortcut of the keyboard contract.</summary>
/// <param name="Keys">The keys, for example <c>Ctrl+K</c>.</param>
/// <param name="Description">What the keys do.</param>
public sealed record KeyBinding(string Keys, string Description);

/// <summary>
/// The shortcuts of CodePad v0.1. They are fixed and use Ctrl on Linux and Windows. The window handles them before the
/// editor sees the key, and none of them is an editing shortcut: copy, paste, undo, select all and the cursor keys stay
/// as the text control has them. One list feeds the settings window and this documentation.
/// </summary>
public static class KeyContract
{
    /// <summary>Gets the shortcuts in the order that the settings window shows them.</summary>
    public static IReadOnlyList<KeyBinding> All { get; } =
    [
        new KeyBinding("Ctrl+N", "New C# experiment"),
        new KeyBinding("Ctrl+Enter", "Run the active C# experiment"),
        new KeyBinding("Ctrl+K", "Open the command palette"),
        new KeyBinding("Ctrl+P", "Open the library search"),
        new KeyBinding("Ctrl+Tab", "Next tab"),
        new KeyBinding("Ctrl+Shift+Tab", "Previous tab"),
        new KeyBinding("Ctrl+W", "Close the active tab"),
        new KeyBinding("Ctrl+Shift+PageUp", "Move the active tab left"),
        new KeyBinding("Ctrl+Shift+PageDown", "Move the active tab right"),
        new KeyBinding("Ctrl+,", "Open the settings"),
        new KeyBinding("F8", "Next problem in the C# editor"),
        new KeyBinding("Escape", "Close the palette, a dialog or the completion list"),
    ];
}
