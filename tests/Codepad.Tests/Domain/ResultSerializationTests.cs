using System.Text.Json;
using Codepad.Domain.Executions;
using Codepad.Domain.Results;

namespace Codepad.Tests.Domain;

public sealed class ResultSerializationTests
{
    private static readonly JsonSerializerOptions s_options = JsonSerializerOptions.Web;

    public static TheoryData<string, ResultNode> Variants => new()
    {
        { "null", new NullNode() },
        { "scalar", new ScalarNode("System.Int32", "42") },
        { "text", new TextNode("hello", null) },
        { "text", new TextNode("hel", 5) },
        { "object", new ObjectNode("Point", [new ResultProperty("X", new ScalarNode("System.Int32", "1")), new ResultProperty("Name", new NullNode())], null) },
        { "object", new ObjectNode("Big", [new ResultProperty("A", new NullNode())], TruncationReason.ItemLimit) },
        { "sequence", new SequenceNode("System.Int32[]", [new ScalarNode("System.Int32", "1"), new ScalarNode("System.Int32", "2")], null) },
        { "sequence", new SequenceNode("IEnumerable<int>", [new ScalarNode("System.Int32", "1")], TruncationReason.TimeLimit) },
        { "sequence", new SequenceNode(null, [], null) },
        { "truncated", new TruncatedNode("Node", TruncationReason.Cycle) },
        { "unsupported", new UnsupportedNode("System.IntPtr", "0x1") },
        { "unsupported", new UnsupportedNode("Thrower", null) },
    };

    [Theory]
    [MemberData(nameof(Variants))]
    public void EachVariantRoundTripsWithItsKind(string kind, ResultNode node)
    {
        string json = JsonSerializer.Serialize(node, s_options);

        ResultNode? back = JsonSerializer.Deserialize<ResultNode>(json, s_options);

        Assert.NotNull(back);
        Assert.Equal(node.GetType(), back.GetType());
        Assert.Equal(json, JsonSerializer.Serialize(back, s_options));
        Assert.Equal(kind, JsonElement.Parse(json).GetProperty("kind").GetString());
    }

    [Fact]
    public void WireFormatOfANestedTreeIsStable()
    {
        ResultNode node = new ObjectNode("Node", [
            new ResultProperty("Name", new TextNode("ab", 4)),
            new ResultProperty("Next", new TruncatedNode("Node", TruncationReason.Cycle)),
            new ResultProperty("Tags", new SequenceNode("List", [new NullNode()], TruncationReason.ItemLimit)),
        ], null);

        string json = JsonSerializer.Serialize(node, s_options);

        Assert.Equal(
            """{"kind":"object","typeName":"Node","properties":[{"name":"Name","value":{"kind":"text","value":"ab","originalLength":4}},{"name":"Next","value":{"kind":"truncated","typeName":"Node","reason":"Cycle"}},{"name":"Tags","value":{"kind":"sequence","typeName":"List","items":[{"kind":"null"}],"truncation":"ItemLimit"}}],"truncation":null}""",
            json);
    }

    [Fact]
    public void DiagnosticsRoundTripWithAndWithoutALocation()
    {
        Diagnostic[] diagnostics =
        [
            new Diagnostic(DiagnosticSeverity.Error, "CS0103", "The name 'x' does not exist.", new SourceLocation(3, 9)),
            new Diagnostic(DiagnosticSeverity.Warning, null, "No location.", null),
            new Diagnostic(DiagnosticSeverity.Info, "I1", "Note.", new SourceLocation(1, 1)),
        ];

        string json = JsonSerializer.Serialize(diagnostics, s_options);
        Diagnostic[]? back = JsonSerializer.Deserialize<Diagnostic[]>(json, s_options);

        Assert.Equal(diagnostics, back);
        Assert.Contains("\"severity\":\"Error\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutionResultRoundTripsWithItsOutcomeAsText()
    {
        ExecutionResult result = new ExecutionResult(
            ExecutionOutcome.CompilationFailed,
            [new Diagnostic(DiagnosticSeverity.Error, "CS1002", "; expected.", new SourceLocation(1, 6))],
            null);

        string json = JsonSerializer.Serialize(result, s_options);
        ExecutionResult? back = JsonSerializer.Deserialize<ExecutionResult>(json, s_options);

        Assert.NotNull(back);
        Assert.Equal(ExecutionOutcome.CompilationFailed, back.Outcome);
        Assert.Equal(result.Diagnostics, back.Diagnostics);
        Assert.Null(back.Value);
        Assert.Contains("\"outcome\":\"CompilationFailed\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializeRejectsAnObjectWithDuplicatePropertyNames()
    {
        string json = """{"kind":"object","typeName":"P","properties":[{"name":"A","value":{"kind":"null"}},{"name":"A","value":{"kind":"null"}}],"truncation":null}""";

        Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize<ResultNode>(json, s_options));
    }

    [Fact]
    public void DeserializeRejectsAnObjectWithNoPropertiesAsAnArgumentException()
    {
        // A receiver must treat JsonException and ArgumentException alike as bad data. See ResultNode.
        Assert.ThrowsAny<ArgumentException>(() => JsonSerializer.Deserialize<ResultNode>("""{"kind":"object","typeName":"P"}""", s_options));
    }

    [Fact]
    public void DeserializeRejectsAnUndefinedReasonNumberAsAnArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => JsonSerializer.Deserialize<ResultNode>("""{"kind":"truncated","typeName":null,"reason":99}""", s_options));
    }

    [Theory]
    [InlineData("""{"typeName":"T","reason":"Cycle"}""")]
    [InlineData("""{"typeName":"T","kind":"truncated","reason":"Cycle"}""")]
    [InlineData("""{"kind":"sequence","typeName":"L","items":[{"typeName":"T","kind":"null"}],"truncation":null}""")]
    public void DeserializeRejectsAMissingOrLateKindAsNotSupported(string json)
    {
        // System.Text.Json needs "kind" first in each node. A receiver must treat NotSupportedException as bad data too.
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<ResultNode>(json, s_options));
    }

    [Fact]
    public void DeserializeRejectsAnUnknownKind()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ResultNode>("""{"kind":"image"}""", s_options));
    }
}
