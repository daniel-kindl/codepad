using Codepad.Domain.Results;

namespace Codepad.Domain.Operations;

/// <summary>
/// The size limits for a <see cref="ResultNode"/> tree, and whether the converter can call property getters.
/// </summary>
/// <remarks>
/// <para>
/// The converter (#7) stops at a limit and puts truncation information in the tree. It does not fail.
/// A receiver uses <see cref="FindExceededLimit"/> to refuse a tree that ignores the limits.
/// </para>
/// <para>
/// Depth: the root has depth 0. Each object property value and each sequence item has the depth of its parent plus 1.
/// Items: the number of properties of one object, or of items in one sequence.
/// Text length: the number of UTF-16 characters in one text, scalar or unsupported value. Names and type names do not count.
/// Nodes: the number of nodes in the tree. A node that is in two places counts two times, because it is serialized two times.
/// </para>
/// </remarks>
/// <param name="MaxDepth">The largest depth that a node can have. At least 1.</param>
/// <param name="MaxItems">The largest number of properties or items in one node. At least 1.</param>
/// <param name="MaxTextLength">The largest length of one value text. At least 1.</param>
/// <param name="MaxNodes">The largest number of nodes in the tree. At least 1.</param>
/// <param name="InvokePropertyGetters">
/// If false, the converter reads only fields and auto-property values. It does not call a property getter,
/// because a getter can run any code, throw or never return.
/// </param>
public sealed record ResultPolicy(int MaxDepth, int MaxItems, int MaxTextLength, int MaxNodes, bool InvokePropertyGetters)
{
    /// <summary>Gets the default limits: depth 16, 1,000 items, 10,000 characters, 50,000 nodes and no property getters.</summary>
    public static ResultPolicy Default { get; } = new ResultPolicy(16, 1_000, 10_000, 50_000, InvokePropertyGetters: false);

    /// <summary>Gets the largest depth that a node can have.</summary>
    public int MaxDepth { get; } = Guard.AtLeastOne(MaxDepth);

    /// <summary>Gets the largest number of properties or items in one node.</summary>
    public int MaxItems { get; } = Guard.AtLeastOne(MaxItems);

    /// <summary>Gets the largest length of one value text.</summary>
    public int MaxTextLength { get; } = Guard.AtLeastOne(MaxTextLength);

    /// <summary>Gets the largest number of nodes in the tree.</summary>
    public int MaxNodes { get; } = Guard.AtLeastOne(MaxNodes);

    /// <summary>Gets a value indicating whether the converter can call property getters.</summary>
    public bool InvokePropertyGetters { get; } = InvokePropertyGetters;

    /// <summary>
    /// Finds the first limit that a tree goes over. The check stops at that limit, so its work is bounded by
    /// <see cref="MaxNodes"/> and it uses no recursion.
    /// </summary>
    /// <param name="root">The root of the tree.</param>
    /// <returns>The limit that the tree goes over, or null if the tree is inside all limits.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is null.</exception>
    public TruncationReason? FindExceededLimit(ResultNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        Stack<(ResultNode Node, int Depth)> pending = new Stack<(ResultNode Node, int Depth)>();
        pending.Push((root, 0));
        int nodes = 0;

        while (pending.TryPop(out (ResultNode Node, int Depth) entry))
        {
            if (entry.Depth > MaxDepth)
            {
                return TruncationReason.DepthLimit;
            }

            nodes++;
            if (nodes > MaxNodes)
            {
                return TruncationReason.NodeLimit;
            }

            switch (entry.Node)
            {
                case TextNode text when text.Value.Length > MaxTextLength:
                case ScalarNode scalar when scalar.Value.Length > MaxTextLength:
                case UnsupportedNode unsupported when unsupported.Text?.Length > MaxTextLength:
                    return TruncationReason.TextLimit;

                case ObjectNode obj:
                    if (obj.Properties.Count > MaxItems)
                    {
                        return TruncationReason.ItemLimit;
                    }

                    foreach (ResultProperty property in obj.Properties)
                    {
                        pending.Push((property.Value, entry.Depth + 1));
                    }

                    break;

                case SequenceNode sequence:
                    if (sequence.Items.Count > MaxItems)
                    {
                        return TruncationReason.ItemLimit;
                    }

                    foreach (ResultNode item in sequence.Items)
                    {
                        pending.Push((item, entry.Depth + 1));
                    }

                    break;
            }
        }

        return null;
    }
}
