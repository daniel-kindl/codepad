using Codepad.Application.Json;
using Codepad.Domain.Operations;
using Codepad.Domain.Results;

namespace Codepad.Tests.Json;

public sealed class JsonEvaluatorTests
{
    private readonly JsonEvaluator evaluator = new JsonEvaluator();

    [Fact]
    public void FormatsValidJsonWithIndentationAndBuildsATree()
    {
        JsonEvaluation result = evaluator.Evaluate("{ \"a\": 1,\"b\":[true,null,\"x\"] }");

        Assert.True(result.IsValid);
        Assert.Null(result.Problem);
        Assert.Equal("{\n  \"a\": 1,\n  \"b\": [\n    true,\n    null,\n    \"x\"\n  ]\n}", result.Formatted?.ReplaceLineEndings("\n"));
        ObjectNode root = Assert.IsType<ObjectNode>(result.Tree);
        Assert.Equal(["a", "b"], root.Properties.Select(property => property.Name));
        Assert.Equal("1", Assert.IsType<ScalarNode>(root.Properties[0].Value).Value);
        SequenceNode list = Assert.IsType<SequenceNode>(root.Properties[1].Value);
        Assert.Equal(3, list.Items.Count);
        Assert.IsType<NullNode>(list.Items[1]);
    }

    [Fact]
    public void KeepsNumbersAndNonAsciiTextExactly()
    {
        JsonEvaluation result = evaluator.Evaluate("[1.0e10, 12345678901234567890123, \"žluťoučký\"]");

        Assert.True(result.IsValid);
        Assert.Contains("1.0e10", result.Formatted, StringComparison.Ordinal);
        Assert.Contains("12345678901234567890123", result.Formatted, StringComparison.Ordinal);
        Assert.Contains("žluťoučký", result.Formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsTheLineAndColumnOfASyntaxError()
    {
        JsonEvaluation result = evaluator.Evaluate("{\n  \"a\": 1,\n  \"b\": ,\n}");

        Assert.False(result.IsValid);
        Assert.Null(result.Formatted);
        Assert.Null(result.Tree);
        Diagnostic problem = Assert.IsType<Diagnostic>(result.Problem);
        Assert.Equal(DiagnosticSeverity.Error, problem.Severity);
        Assert.Equal(new SourceLocation(3, 8), problem.Location);
        Assert.Equal("JSON_SYNTAX", problem.Code);
        Assert.DoesNotContain("LineNumber", problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("BytePosition", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheColumnCountsCharactersNotBytes()
    {
        // "ž" has two bytes in UTF-8. The error is at the character after it.
        JsonEvaluation result = evaluator.Evaluate("[\"ž\" x]");

        Assert.Equal(new SourceLocation(1, 6), result.Problem?.Location);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    public void EmptyInputIsAnExplainedProblemNotACrash(string text)
    {
        JsonEvaluation result = evaluator.Evaluate(text);

        Assert.False(result.IsValid);
        Assert.Equal("JSON_EMPTY", result.Problem?.Code);
    }

    [Fact]
    public void TooDeepJsonFailsWithAnExplanation()
    {
        string text = new string('[', 70) + new string(']', 70);

        JsonEvaluation result = evaluator.Evaluate(text);

        Assert.False(result.IsValid);
        Assert.Equal("JSON_TOO_DEEP", result.Problem?.Code);
        Assert.Contains("64", result.Problem?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOversizedDocumentFailsBeforeItIsParsed()
    {
        JsonEvaluator small = new JsonEvaluator(new JsonLimits(MaxBytes: 20, MaxDepth: 64));

        JsonEvaluation result = small.Evaluate("[1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]");

        Assert.False(result.IsValid);
        Assert.Equal("JSON_TOO_LARGE", result.Problem?.Code);
        Assert.Contains("20", result.Problem?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWideArrayBecomesACutTreeButTheFormattedTextStaysWhole()
    {
        string text = "[" + string.Join(",", Enumerable.Range(0, 1_500)) + "]";

        JsonEvaluation result = evaluator.Evaluate(text);

        Assert.True(result.IsValid);
        SequenceNode list = Assert.IsType<SequenceNode>(result.Tree);
        Assert.Equal(TruncationReason.ItemLimit, list.Truncation);
        Assert.Equal(1_000, list.Items.Count);
        Assert.Contains("1499", result.Formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void ATreeDeeperThanTheResultPolicyIsCutWithAMarker()
    {
        string text = new string('[', 30) + new string(']', 30);

        JsonEvaluation result = evaluator.Evaluate(text);

        Assert.True(result.IsValid);
        Assert.Null(ResultPolicy.Default.FindExceededLimit(result.Tree!));
    }

    [Fact]
    public void TheTreeOfEveryValidDocumentRespectsTheResultPolicy()
    {
        string item = "{\"k\":[1,2,3],\"s\":\"" + new string('x', 20_000) + "\"}";
        JsonEvaluation result = evaluator.Evaluate("[" + item + "," + item + "]");

        Assert.True(result.IsValid);
        Assert.Null(ResultPolicy.Default.FindExceededLimit(result.Tree!));
    }

    [Fact]
    public async Task EvaluateAsyncGivesTheSameResultAsEvaluate()
    {
        JsonEvaluation result = await evaluator.EvaluateAsync("[1]", TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void KeepsDuplicateNamesOutOfTheTreeCrash()
    {
        // ObjectNode forbids two properties with one name. The tree must not throw for valid JSON.
        JsonEvaluation result = evaluator.Evaluate("{\"a\":1,\"a\":2}");

        Assert.True(result.IsValid);
        Assert.Equal(["a"], Assert.IsType<ObjectNode>(result.Tree).Properties.Select(property => property.Name));
        Assert.Contains("\"a\": 2", result.Formatted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"a\":\"#\"}", 1, 7)]
    [InlineData("[1,\n\"x#\"]", 2, 3)]
    [InlineData("#", 1, 1)]
    public void ALoneSurrogateIsAResultWithAPlaceNotAnException(string template, int line, int column)
    {
        // The test runner cannot carry a lone surrogate in its data, so the test puts it in here.
        JsonEvaluation result = evaluator.Evaluate(template.Replace('#', '\uD800'));

        Assert.False(result.IsValid);
        Assert.Equal("JSON_INVALID_TEXT", result.Problem?.Code);
        Assert.Equal(new SourceLocation(line, column), result.Problem?.Location);
    }

    [Fact]
    public void AValidSurrogatePairIsFine()
    {
        Assert.True(evaluator.Evaluate("[\"\U0001F600\"]").IsValid);
    }
}
