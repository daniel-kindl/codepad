using Avalonia.Controls;

namespace Codepad.Desktop.Workspace;

/// <summary>The content of a C# tab: the editor above the result panel.</summary>
public sealed partial class CSharpTabView : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="CSharpTabView"/> class.</summary>
    public CSharpTabView()
    {
        InitializeComponent();
    }

    /// <summary>Gives the focus to the editor.</summary>
    internal void FocusEditor() => EditorView.TextEditor.TextArea.Focus();
}
