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
    public void ObjectWithGettersOffIsShownWithoutCallingAGetter()
    {
        Probe probe = new Probe();

        ResultNode node = Convert(probe);

        // Name has a computed getter, so it is not shown. GetterCalled is an auto-property, so its stored value is.
        ObjectNode shown = Assert.IsType<ObjectNode>(node);
        Assert.Equal(typeof(Probe).FullName, shown.TypeName);
        Assert.DoesNotContain(shown.Properties, property => property.Name == "Name");
        Assert.Equal("False", Assert.IsType<ScalarNode>(shown.Properties.Single(property => property.Name == "GetterCalled").Value).Value);
        Assert.False(probe.GetterCalled);
    }

    private sealed record Person(string Name, int Age, Address Home);

    private sealed record Address(string City);

    [Fact]
    public void RecordWithGettersOffShowsItsDeclaredDataAsNestedObjects()
    {
        ObjectNode node = Assert.IsType<ObjectNode>(Convert(new Person("Ada", 36, new Address("London"))));

        Assert.Equal(["Name", "Age", "Home"], node.Properties.Select(property => property.Name));
        Assert.Equal("Ada", Assert.IsType<TextNode>(node.Properties[0].Value).Value);
        Assert.Equal("36", Assert.IsType<ScalarNode>(node.Properties[1].Value).Value);
        ObjectNode home = Assert.IsType<ObjectNode>(node.Properties[2].Value);
        Assert.Equal("London", Assert.IsType<TextNode>(home.Properties.Single().Value).Value);
    }

    [Fact]
    public void AnonymousObjectAndTupleShowTheirMembers()
    {
        ObjectNode anonymous = Assert.IsType<ObjectNode>(Convert(new { Id = 7, Label = "x" }));
        ObjectNode tuple = Assert.IsType<ObjectNode>(Convert((1, "two")));

        Assert.Equal(["Id", "Label"], anonymous.Properties.Select(property => property.Name));
        Assert.Equal(["Item1", "Item2"], tuple.Properties.Select(property => property.Name));
    }

    private sealed class Throwing
    {
        public int Safe { get; } = 1;

        public int Computed => Safe == 1 ? throw new InvalidOperationException("never call me") : 0;
    }

    [Fact]
    public void ComputedPropertyThatThrowsIsNeverCalledWithGettersOff()
    {
        ObjectNode node = Assert.IsType<ObjectNode>(Convert(new Throwing()));

        Assert.Equal(["Safe"], node.Properties.Select(property => property.Name));
    }

    [Fact]
    public void GetterThatThrowsWithGettersOnBecomesUnsupportedNode()
    {
        ResultPolicy policy = new ResultPolicy(16, 1_000, 10_000, 50_000, InvokePropertyGetters: true);

        ObjectNode node = Assert.IsType<ObjectNode>(Convert(new Throwing(), policy));

        UnsupportedNode failed = Assert.IsType<UnsupportedNode>(node.Properties.Single(property => property.Name == "Computed").Value);
        Assert.Contains("InvalidOperationException", failed.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void DictionaryBecomesASequenceOfKeyValueObjects()
    {
        SequenceNode node = Assert.IsType<SequenceNode>(Convert(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }));

        Assert.Equal(2, node.Items.Count);
        ObjectNode first = Assert.IsType<ObjectNode>(node.Items[0]);
        Assert.Equal(["Key", "Value"], first.Properties.Select(property => property.Name));
        Assert.Equal("a", Assert.IsType<TextNode>(first.Properties[0].Value).Value);
        Assert.Equal("1", Assert.IsType<ScalarNode>(first.Properties[1].Value).Value);
    }

    [Fact]
    public void ManyLargeDictionariesStayInsideTheNodeLimit()
    {
        List<Dictionary<int, int>> dictionaries = [.. Enumerable.Range(0, 100).Select(_ => Enumerable.Range(0, 1_000).ToDictionary(key => key, key => key))];

        ResultNode node = Convert(dictionaries);

        Assert.Null(ResultPolicy.Default.FindExceededLimit(node));
    }

    [Fact]
    public void DictionaryWithAnObjectValueStaysInsideASmallNodeLimit()
    {
        ResultPolicy policy = new ResultPolicy(8, 50, 100, 20, InvokePropertyGetters: false);
        Dictionary<string, object> dictionary = Enumerable.Range(0, 30).ToDictionary(i => "k" + i, i => (object)new { Id = i, Name = "n" + i });

        ResultNode node = Convert(dictionary, policy);

        Assert.Null(policy.FindExceededLimit(node));
    }

    private static IEnumerable<int> FailsAfterTwo()
    {
        yield return 1;
        yield return 2;
        throw new InvalidOperationException("boom");
    }

    [Fact]
    public void ExceptionWhileEnumeratingKeepsTheItemsReadSoFar()
    {
        SequenceNode node = Assert.IsType<SequenceNode>(Convert(FailsAfterTwo()));

        Assert.Equal(2, node.Items.Count);
        Assert.Equal(TruncationReason.Error, node.Truncation);
    }

    private static IEnumerable<int> SlowEndless()
    {
        int next = 0;
        while (true)
        {
            Thread.Sleep(5);
            yield return next++;
        }
    }

    [Fact]
    public void SlowEnumerationStopsAtTheTimeBudget()
    {
        ResultPolicy policy = new ResultPolicy(16, 1_000_000, 10_000, 1_000_000, InvokePropertyGetters: false);

        ResultNode node = new ResultNodeConverter(policy, TimeSpan.FromMilliseconds(100)).Convert(SlowEndless());

        SequenceNode sequence = Assert.IsType<SequenceNode>(node);
        Assert.Equal(TruncationReason.TimeLimit, sequence.Truncation);
        Assert.NotEmpty(sequence.Items);
    }

    [Fact]
    public void DeepNestingBecomesADepthTruncationNotARecursionFailure()
    {
        object?[] root = new object?[1];
        object?[] current = root;
        for (int i = 0; i < 5_000; i++)
        {
            object?[] next = new object?[1];
            current[0] = next;
            current = next;
        }

        ResultNode node = Convert(root);

        Assert.Null(ResultPolicy.Default.FindExceededLimit(node));
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
