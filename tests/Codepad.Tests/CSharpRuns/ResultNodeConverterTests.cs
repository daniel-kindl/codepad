using Codepad.Domain.Operations;
using Codepad.Domain.Results;
using Codepad.Worker;

namespace Codepad.Tests.CSharpRuns;

public sealed class ResultNodeConverterTests
{
    private static ResultNode Convert(object? value, ResultPolicy? policy = null)
    {
        return new ResultNodeConverter(policy ?? ResultPolicy.Default).Convert(value);
    }

    [Fact]
    public void NullBecomesNullNode()
    {
        Assert.IsType<NullNode>(Convert(null));
    }

    [Fact]
    public void IntegerBecomesScalarWithInvariantText()
    {
        ScalarNode node = Assert.IsType<ScalarNode>(Convert(1234));

        Assert.Equal("System.Int32", node.TypeName);
        Assert.Equal("1234", node.Value);
    }

    [Fact]
    public void DoubleUsesTheInvariantCulture()
    {
        System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            ScalarNode node = Assert.IsType<ScalarNode>(Convert(1.5));

            Assert.Equal("1.5", node.Value);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ShortStringBecomesTextNodeWithoutTruncation()
    {
        TextNode node = Assert.IsType<TextNode>(Convert("abc"));

        Assert.Equal("abc", node.Value);
        Assert.False(node.IsTruncated);
    }

    [Fact]
    public void LongStringIsCutAndKeepsTheOriginalLength()
    {
        ResultPolicy policy = new ResultPolicy(4, 10, 5, 100, InvokePropertyGetters: false);

        TextNode node = Assert.IsType<TextNode>(Convert("abcdefghij", policy));

        Assert.Equal("abcde", node.Value);
        Assert.Equal(10, node.OriginalLength);
    }

    [Fact]
    public void SequenceKeepsItsItemsInOrder()
    {
        SequenceNode node = Assert.IsType<SequenceNode>(Convert(new List<int> { 1, 2, 3 }));

        Assert.Equal("1,2,3", string.Join(",", node.Items.Cast<ScalarNode>().Select(item => item.Value)));
        Assert.Null(node.Truncation);
    }

    [Fact]
    public void SequenceAboveTheItemLimitIsCutWithItemLimit()
    {
        ResultPolicy policy = new ResultPolicy(4, 2, 100, 100, InvokePropertyGetters: false);

        SequenceNode node = Assert.IsType<SequenceNode>(Convert(new List<int> { 1, 2, 3, 4 }, policy));

        Assert.Equal(2, node.Items.Count);
        Assert.Equal(TruncationReason.ItemLimit, node.Truncation);
    }

    [Fact]
    public void InfiniteSequenceStopsAtTheItemLimit()
    {
        ResultPolicy policy = new ResultPolicy(4, 3, 100, 100, InvokePropertyGetters: false);

        SequenceNode node = Assert.IsType<SequenceNode>(Convert(Endless(), policy));

        Assert.Equal(3, node.Items.Count);
        Assert.Equal(TruncationReason.ItemLimit, node.Truncation);
    }

    [Fact]
    public void ContainerAtTheDepthLimitBecomesTruncatedNode()
    {
        ResultPolicy policy = new ResultPolicy(1, 10, 100, 100, InvokePropertyGetters: false);
        object[] nested = [new object[] { new object[] { 1 } }];

        SequenceNode root = Assert.IsType<SequenceNode>(Convert(nested, policy));

        TruncatedNode cut = Assert.IsType<TruncatedNode>(root.Items[0]);
        Assert.Equal(TruncationReason.DepthLimit, cut.Reason);
        Assert.Null(policy.FindExceededLimit(root));
    }

    [Fact]
    public void ObjectWithGettersOffBecomesUnsupportedWithoutCallingAGetter()
    {
        Probe probe = new Probe();

        UnsupportedNode node = Assert.IsType<UnsupportedNode>(Convert(probe));

        Assert.Equal(typeof(Probe).FullName, node.TypeName);
        Assert.False(probe.GetterCalled);
    }

    [Fact]
    public void ObjectWithGettersOnBecomesObjectNodeWithItsProperties()
    {
        ResultPolicy policy = new ResultPolicy(16, 1_000, 10_000, 50_000, InvokePropertyGetters: true);

        ObjectNode node = Assert.IsType<ObjectNode>(Convert(new Probe(), policy));

        ResultProperty name = Assert.Single(node.Properties, property => property.Name == "Name");
        Assert.Equal("probe", Assert.IsType<TextNode>(name.Value).Value);
    }

    [Fact]
    public void CycleBecomesTruncatedNodeWithCycleReason()
    {
        ResultPolicy policy = new ResultPolicy(16, 1_000, 10_000, 50_000, InvokePropertyGetters: true);
        Loop loop = new Loop();
        loop.Self = loop;

        ObjectNode node = Assert.IsType<ObjectNode>(Convert(loop, policy));

        TruncatedNode cut = Assert.IsType<TruncatedNode>(node.Properties.Single().Value);
        Assert.Equal(TruncationReason.Cycle, cut.Reason);
    }

    [Fact]
    public void WideTreeStopsAtTheNodeLimitAndStaysWithinPolicy()
    {
        ResultPolicy policy = new ResultPolicy(8, 10, 100, 12, InvokePropertyGetters: false);
        object[][] grid = Enumerable.Range(0, 10).Select(_ => Enumerable.Range(0, 10).Cast<object>().ToArray()).ToArray();

        ResultNode node = Convert(grid, policy);

        Assert.Null(policy.FindExceededLimit(node));
    }

    private static IEnumerable<int> Endless()
    {
        int next = 0;
        while (true)
        {
            yield return next++;
        }
    }

    private sealed class Probe
    {
        public bool GetterCalled { get; private set; }

        public string Name
        {
            get
            {
                GetterCalled = true;
                return "probe";
            }
        }
    }

    private sealed class Loop
    {
        public Loop? Self { get; set; }
    }
}
