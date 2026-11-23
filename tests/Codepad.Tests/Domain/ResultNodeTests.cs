using Codepad.Domain.Operations;
using Codepad.Domain.Results;

namespace Codepad.Tests.Domain;

public sealed class ResultNodeTests
{
    private static readonly ResultPolicy s_small = new ResultPolicy(MaxDepth: 2, MaxItems: 3, MaxTextLength: 5, MaxNodes: 6, InvokePropertyGetters: false);

    [Fact]
    public void ObjectRejectsDuplicatePropertyNames()
    {
        ResultProperty[] properties = [new ResultProperty("A", new NullNode()), new ResultProperty("A", new NullNode())];

        ArgumentException error = Assert.Throws<ArgumentException>(() => new ObjectNode("Point", properties, null));
        Assert.Contains("A", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ObjectAndSequenceKeepTheirOwnCopyOfTheChildren()
    {
        List<ResultProperty> properties = [new ResultProperty("X", new ScalarNode("System.Int32", "1"))];
        List<ResultNode> items = [new NullNode()];
        ObjectNode obj = new ObjectNode("Point", properties, null);
        SequenceNode sequence = new SequenceNode("List", items, null);

        properties.Clear();
        items.Clear();

        Assert.Single(obj.Properties);
        Assert.Single(sequence.Items);
    }

    [Fact]
    public void NodesRejectNullChildren()
    {
        Assert.Throws<ArgumentNullException>(() => new ObjectNode("T", null!, null));
        Assert.Throws<ArgumentException>(() => new ObjectNode("T", [null!], null));
        Assert.Throws<ArgumentNullException>(() => new SequenceNode("T", null!, null));
        Assert.Throws<ArgumentException>(() => new SequenceNode("T", [null!], null));
        Assert.Throws<ArgumentNullException>(() => new ResultProperty(null!, new NullNode()));
        Assert.Throws<ArgumentNullException>(() => new ResultProperty("A", null!));
        Assert.Throws<ArgumentNullException>(() => new TextNode(null!, null));
        Assert.Throws<ArgumentNullException>(() => new ScalarNode("T", null!));
    }

    [Fact]
    public void TruncatedTextNeedsAnOriginalLengthLongerThanItsValue()
    {
        TextNode text = new TextNode("abc", 10);

        Assert.True(text.IsTruncated);
        Assert.False(new TextNode("abc", null).IsTruncated);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextNode("abc", 3));
    }

    [Fact]
    public void NodesRejectAnUndefinedTruncationReason()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TruncatedNode("T", (TruncationReason)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SequenceNode("T", [], (TruncationReason)99));
    }

    [Fact]
    public void FindExceededLimitReturnsNullForATreeInsideTheLimits()
    {
        ResultNode root = new SequenceNode("List", [new ObjectNode("P", [new ResultProperty("X", new TextNode("hello", null))], null)], null);

        Assert.Null(s_small.FindExceededLimit(root));
    }

    [Fact]
    public void FindExceededLimitAcceptsATreeExactlyAtEachLimit()
    {
        // 6 nodes (MaxNodes), 3 items in the root (MaxItems), depth 2 (MaxDepth), text of 5 characters (MaxTextLength).
        SequenceNode inner = new SequenceNode("L", [new TextNode("12345", null), new NullNode()], null);
        ResultNode root = new SequenceNode("L", [inner, new NullNode(), new NullNode()], null);

        Assert.Null(s_small.FindExceededLimit(root));
    }

    [Fact]
    public void FindExceededLimitAcceptsAnObjectTreeExactlyAtTheNodeLimit()
    {
        // 6 nodes (MaxNodes), built from objects only.
        ObjectNode inner = new ObjectNode("I", [new ResultProperty("a", new NullNode()), new ResultProperty("b", new NullNode())], null);
        ResultNode root = new ObjectNode("O", [new ResultProperty("x", inner), new ResultProperty("y", new NullNode()), new ResultProperty("z", new NullNode())], null);

        Assert.Null(s_small.FindExceededLimit(root));
    }

    [Fact]
    public void FindExceededLimitReportsTheNodeCountOfAnObjectTree()
    {
        // 7 nodes, one over MaxNodes, built from objects only.
        ObjectNode inner = new ObjectNode("I", [new ResultProperty("a", new NullNode()), new ResultProperty("b", new NullNode()), new ResultProperty("c", new NullNode())], null);
        ResultNode root = new ObjectNode("O", [new ResultProperty("x", inner), new ResultProperty("y", new NullNode()), new ResultProperty("z", new NullNode())], null);

        Assert.Equal(TruncationReason.NodeLimit, s_small.FindExceededLimit(root));
    }

    [Fact]
    public void FindExceededLimitReportsDepth()
    {
        ResultNode root = new SequenceNode("L", [new SequenceNode("L", [new SequenceNode("L", [new NullNode()], null)], null)], null);

        Assert.Equal(TruncationReason.DepthLimit, s_small.FindExceededLimit(root));
    }

    [Fact]
    public void FindExceededLimitReportsItemsInASequenceAndAnObject()
    {
        ResultNode sequence = new SequenceNode("L", [new NullNode(), new NullNode(), new NullNode(), new NullNode()], null);
        ResultNode obj = new ObjectNode("O", [new ResultProperty("a", new NullNode()), new ResultProperty("b", new NullNode()), new ResultProperty("c", new NullNode()), new ResultProperty("d", new NullNode())], null);

        Assert.Equal(TruncationReason.ItemLimit, s_small.FindExceededLimit(sequence));
        Assert.Equal(TruncationReason.ItemLimit, s_small.FindExceededLimit(obj));
    }

    [Fact]
    public void FindExceededLimitReportsText()
    {
        Assert.Equal(TruncationReason.TextLimit, s_small.FindExceededLimit(new TextNode("123456", null)));
        Assert.Equal(TruncationReason.TextLimit, s_small.FindExceededLimit(new ScalarNode("System.Int64", "123456")));
        Assert.Equal(TruncationReason.TextLimit, s_small.FindExceededLimit(new UnsupportedNode("System.IntPtr", "123456")));
    }

    [Fact]
    public void FindExceededLimitReportsTheTotalNodeCount()
    {
        SequenceNode inner = new SequenceNode("L", [new NullNode(), new NullNode(), new NullNode()], null);
        ResultNode root = new SequenceNode("L", [inner, new NullNode(), new NullNode()], null);

        Assert.Equal(TruncationReason.NodeLimit, s_small.FindExceededLimit(root));
    }

    [Fact]
    public void FindExceededLimitCountsASharedChildAtEachPlace()
    {
        // A tree cannot hold a cycle, because a child must exist before its parent. A shared child
        // is serialized at each place, so the check counts it at each place.
        SequenceNode shared = new SequenceNode("L", [new NullNode(), new NullNode()], null);
        ResultNode root = new SequenceNode("L", [shared, shared], null);

        Assert.Equal(TruncationReason.NodeLimit, s_small.FindExceededLimit(root));
    }

    [Fact]
    public void FindExceededLimitHandlesADeepTreeWithNoStackOverflow()
    {
        ResultNode node = new NullNode();
        for (int depth = 0; depth < 100_000; depth++)
        {
            node = new SequenceNode("L", [node], null);
        }

        Assert.Equal(TruncationReason.DepthLimit, ResultPolicy.Default.FindExceededLimit(node));
    }
}
