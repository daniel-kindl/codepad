using Codepad.Application.Execution;
using Codepad.Contracts;
using Codepad.Domain.Executions;
using Codepad.Domain.Results;

namespace Codepad.Desktop.Tests.Results;

/// <summary>Builds run reports for the result panel tests.</summary>
internal static class ResultSamples
{
    public static RunReport Report(ExecutionOutcome outcome, ResultNode? value = null, IReadOnlyList<Diagnostic>? diagnostics = null, string stdout = "", string stderr = "", int? stdoutOriginal = null)
    {
        return new RunReport(
            ExecutionId.New(),
            1,
            new ExecutionResult(outcome, diagnostics ?? [], value),
            new CapturedText(stdout, stdoutOriginal),
            new CapturedText(stderr, null),
            null);
    }

    public static RunReport Success(ResultNode? value, string stdout = "") => Report(ExecutionOutcome.Success, value, stdout: stdout);

    public static ScalarNode Int(int value) => new ScalarNode("System.Int32", value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static TextNode Text(string value) => new TextNode(value, null);

    public static ObjectNode Person(string name, int age, string city) => new ObjectNode(
        "Demo.Person",
        [new ResultProperty("Name", Text(name)), new ResultProperty("Age", Int(age)), new ResultProperty("City", Text(city))],
        null);

    public static SequenceNode People(int count, TruncationReason? truncation = null) => new SequenceNode(
        "System.Collections.Generic.List`1[Demo.Person]",
        [.. Enumerable.Range(1, count).Select(i => Person("Person " + i, 20 + (i % 50), "City " + (i % 7)))],
        truncation);

    public static ObjectNode Nested() => new ObjectNode(
        "Demo.Order",
        [
            new ResultProperty("Id", Int(1001)),
            new ResultProperty("Customer", Person("Ada Lovelace", 36, "London")),
            new ResultProperty("Lines", new SequenceNode("System.Int32[]", [Int(1), Int(2), Int(3)], null)),
            new ResultProperty("Parent", new TruncatedNode("Demo.Order", TruncationReason.Cycle)),
            new ResultProperty("Notes", new NullNode()),
        ],
        null);
}
