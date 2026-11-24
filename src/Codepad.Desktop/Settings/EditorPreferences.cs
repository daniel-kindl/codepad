namespace Codepad.Desktop.Settings;

/// <summary>
/// The editor settings that the views apply at once: the font size and the indentation width. The views read the values
/// when they open and again after <see cref="Changed"/>. The object is shared, so a change reaches every open editor.
/// </summary>
public sealed class EditorPreferences
{
    /// <summary>Raised on the UI thread after a value changed.</summary>
    public event Action? Changed;

    /// <summary>Gets the font size in points.</summary>
    public int FontSize { get; private set; } = 14;

    /// <summary>Gets the number of spaces for one indentation step.</summary>
    public int IndentationWidth { get; private set; } = 4;

    /// <summary>Sets the values and raises <see cref="Changed"/> when one of them is new.</summary>
    /// <param name="fontSize">The font size.</param>
    /// <param name="indentationWidth">The indentation width.</param>
    public void Update(int fontSize, int indentationWidth)
    {
        if (fontSize == FontSize && indentationWidth == IndentationWidth)
        {
            return;
        }

        FontSize = fontSize;
        IndentationWidth = indentationWidth;
        Changed?.Invoke();
    }
}
