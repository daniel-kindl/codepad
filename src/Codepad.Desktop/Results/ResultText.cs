using System.Globalization;
using Codepad.Domain.Results;

namespace Codepad.Desktop.Results;

/// <summary>Plain-language text for result nodes and truncation reasons. The UI shows these texts, never the enum names.</summary>
internal static class ResultText
{
    /// <summary>Gets a short sentence for a truncation reason.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>The sentence, without a final period.</returns>
    public static string Truncation(TruncationReason reason) => reason switch
    {
        TruncationReason.Cycle => "This value refers back to a value above it, so it is not shown again",
        TruncationReason.DepthLimit => "This value is nested too deeply to show",
        TruncationReason.ItemLimit => "Only the first items are shown",
        TruncationReason.TextLimit => "The text is cut",
        TruncationReason.NodeLimit => "The result is too large, so only the first part is shown",
        TruncationReason.TimeLimit => "Reading the value took too long, so only the first part is shown",
        TruncationReason.Error => "Reading the value failed, so only the first part is shown",
        _ => "Part of the value is not shown",
    };

    /// <summary>
    /// Gets the type name without its namespace. A generic type shows its arity, for example <c>List&lt;…&gt;</c>,
    /// because .NET type names hold the full assembly-qualified arguments. An array keeps its brackets.
    /// </summary>
    /// <param name="typeName">The full type name, or null.</param>
    /// <returns>The short name, or an empty string.</returns>
    public static string ShortType(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return string.Empty;
        }

        int tick = typeName.IndexOf('`', StringComparison.Ordinal);
        string head = tick < 0 ? typeName : typeName[..tick];
        string tail = string.Empty;
        if (tick >= 0)
        {
            tail = typeName.EndsWith("[]", StringComparison.Ordinal) ? "<…>[]" : "<…>";
        }

        int dot = head.LastIndexOf('.');
        string name = dot < 0 ? head : head[(dot + 1)..];
        int plus = name.LastIndexOf('+');
        return (plus < 0 ? name : name[(plus + 1)..]) + tail;
    }

    /// <summary>Gets the one-line summary of a node, as the tree and the table cells show it.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The summary.</returns>
    public static string Summary(ResultNode node) => node switch
    {
        NullNode => "null",
        ScalarNode scalar => scalar.Value,
        TextNode text => Quote(text),
        ObjectNode obj => Count(obj.Properties.Count, "property", "properties", obj.Truncation is not null),
        SequenceNode sequence => Count(sequence.Items.Count, "item", "items", sequence.Truncation is not null),
        TruncatedNode truncated => "Not expanded. " + Truncation(truncated.Reason),
        UnsupportedNode unsupported => string.IsNullOrEmpty(unsupported.Text) ? "Cannot show this value" : unsupported.Text,
        _ => string.Empty,
    };

    /// <summary>Gets the text that a table cell shows. It never has a line break.</summary>
    /// <param name="node">The cell value.</param>
    /// <returns>One line of text.</returns>
    public static string Cell(ResultNode node)
    {
        string text = node switch
        {
            TextNode value => value.Value + (value.IsTruncated ? "…" : string.Empty),
            ObjectNode or SequenceNode => ShortType(node is ObjectNode o ? o.TypeName : ((SequenceNode)node).TypeName) + " " + Summary(node),
            _ => Summary(node),
        };
        return text.ReplaceLineEndings(" ");
    }

    private static string Quote(TextNode text)
    {
        string value = "\"" + text.Value + "\"";
        return text.OriginalLength is int length
            ? value + string.Create(CultureInfo.InvariantCulture, $" (cut, {length:N0} characters in total)")
            : value;
    }

    private static string Count(int count, string one, string many, bool cut)
    {
        string text = string.Create(CultureInfo.InvariantCulture, $"{count:N0} {(count == 1 ? one : many)}");
        return cut ? text + " shown, more not shown" : text;
    }
}
