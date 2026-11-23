using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Codepad.Application.Editing;

namespace Codepad.Desktop.Editor;

/// <summary>One row of the completion list. The kind shows as a word, so the row never relies on color.</summary>
internal sealed class CompletionData : ICompletionData
{
    private readonly CompletionItem item;

    /// <summary>Initializes a new instance of the <see cref="CompletionData"/> class.</summary>
    /// <param name="item">The suggestion.</param>
    public CompletionData(CompletionItem item)
    {
        this.item = item;
        StackPanel row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        TextBlock kind = new TextBlock { Text = KindWord(item.Kind), MinWidth = 64 };
        kind.Classes.Add("completion-kind");
        TextBlock name = new TextBlock { Text = item.Text, FontWeight = FontWeight.SemiBold };
        name.Classes.Add("completion-name");
        row.Children.Add(kind);
        row.Children.Add(name);
        if (item.Detail.Length > 0)
        {
            TextBlock detail = new TextBlock { Text = item.Detail, TextTrimming = TextTrimming.CharacterEllipsis };
            detail.Classes.Add("completion-detail");
            row.Children.Add(detail);
        }

        AutomationProperties.SetName(row, item.Detail.Length > 0 ? $"{item.Text}, {KindWord(item.Kind)}, {item.Detail}" : $"{item.Text}, {KindWord(item.Kind)}");
        Content = row;
    }

    /// <inheritdoc />
    public Avalonia.Media.IImage? Image => null;

    /// <inheritdoc />
    public string Text => item.Text;

    /// <inheritdoc />
    public object Content { get; }

    /// <inheritdoc />
    public object? Description => null;

    /// <inheritdoc />
    public double Priority => 0;

    /// <summary>Gets the word that names a kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>A lower-case word.</returns>
    internal static string KindWord(CompletionKind kind) => kind switch
    {
        CompletionKind.Keyword => "keyword",
        CompletionKind.Variable => "variable",
        CompletionKind.Member => "member",
        CompletionKind.Method => "method",
        CompletionKind.Namespace => "namespace",
        _ => "type",
    };

    /// <inheritdoc />
    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        // The segment is the typed word. Replacing it is one document change, so one undo restores the typed word.
        textArea.Document.Replace(completionSegment, item.Text);
    }
}
