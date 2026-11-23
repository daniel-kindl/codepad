using Codepad.Domain.Results;

namespace Codepad.Desktop.Results;

/// <summary>
/// One row of the result tree. The row creates its children only when the tree first asks for them, so a large
/// result never allocates rows that the user does not open.
/// </summary>
public sealed class ResultTreeItem
{
    private readonly ResultNode node;
    private IReadOnlyList<ResultTreeItem>? children;

    /// <summary>Initializes a new instance of the <see cref="ResultTreeItem"/> class.</summary>
    /// <param name="label">The property name or the index, for example <c>Name</c> or <c>[3]</c>.</param>
    /// <param name="node">The node that the row shows.</param>
    public ResultTreeItem(string label, ResultNode node)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(node);
        Label = label;
        this.node = node;
    }

    /// <summary>Gets the property name or the index.</summary>
    public string Label { get; }

    /// <summary>Gets the one-line value or summary.</summary>
    public string Summary => ResultText.Summary(node);

    /// <summary>Gets the short type name, or an empty string.</summary>
    public string TypeText => ResultText.ShortType(node switch
    {
        ScalarNode scalar => scalar.TypeName,
        ObjectNode obj => obj.TypeName,
        SequenceNode sequence => sequence.TypeName,
        TruncatedNode truncated => truncated.TypeName,
        UnsupportedNode unsupported => unsupported.TypeName,
        _ => null,
    });

    /// <summary>Gets a value indicating whether the node holds less than the full value.</summary>
    public bool IsTruncated => node switch
    {
        ObjectNode obj => obj.Truncation is not null,
        SequenceNode sequence => sequence.Truncation is not null,
        TextNode text => text.IsTruncated,
        TruncatedNode => true,
        _ => false,
    };

    /// <summary>Gets the sentence that tells why the node holds less than the full value, or an empty string.</summary>
    public string TruncationText => node switch
    {
        ObjectNode { Truncation: TruncationReason reason } => ResultText.Truncation(reason),
        SequenceNode { Truncation: TruncationReason reason } => ResultText.Truncation(reason),
        TextNode { IsTruncated: true } => ResultText.Truncation(TruncationReason.TextLimit),
        TruncatedNode truncated => ResultText.Truncation(truncated.Reason),
        _ => string.Empty,
    };

    /// <summary>Gets the text that a screen reader reads for the row.</summary>
    public string AccessibleName
    {
        get
        {
            string type = TypeText.Length > 0 ? $", {TypeText}" : string.Empty;
            string cut = IsTruncated ? $". {TruncationText}" : string.Empty;
            return $"{Label}{type}: {Summary}{cut}";
        }
    }

    /// <summary>Gets the child rows. They are created on the first read.</summary>
    public IReadOnlyList<ResultTreeItem> Children => children ??= CreateChildren();

    private List<ResultTreeItem> CreateChildren() => node switch
    {
        ObjectNode obj => [.. obj.Properties.Select(property => new ResultTreeItem(property.Name, property.Value))],
        SequenceNode sequence => [.. sequence.Items.Select((item, index) => new ResultTreeItem($"[{index}]", item))],
        _ => [],
    };
}
