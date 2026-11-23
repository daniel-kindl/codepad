using Codepad.Application.Execution;
using Codepad.Desktop.Results;
using Codepad.Domain.Executions;
using Codepad.Domain.Results;

namespace Codepad.Desktop.Tests.Results;

public sealed class ResultsViewModelTests
{
    private static readonly DateTimeOffset _now = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static ResultsViewModel Show(RunReport report)
    {
        ResultsViewModel model = new ResultsViewModel();
        model.Show(report, _now);
        return model;
    }

    [Fact]
    public void StartsEmptyWithAnExplanation()
    {
        ResultsViewModel model = new ResultsViewModel();

        Assert.Equal(ResultPresentation.None, model.Presentation);
        Assert.True(model.IsEmpty);
        Assert.Equal(ResultsViewModel.NO_RUN_TEXT, model.StatusText);
        Assert.Equal(ResultsViewModel.EMPTY_RESULT_TEXT, model.EmptyText);
        Assert.False(model.CanCopy);
        Assert.Null(model.GetCopyText());
    }

    [Theory]
    [InlineData(42, "42")]
    public void AScalarShowsAsText(int value, string expected)
    {
        ResultsViewModel model = Show(ResultSamples.Success(ResultSamples.Int(value)));

        Assert.Equal(ResultPresentation.Text, model.Presentation);
        Assert.Equal(expected, model.ValueText);
        Assert.Equal("Succeeded", model.StatusText);
        Assert.Equal("success", model.StatusKind);
        Assert.True(model.CanCopy);
    }

    [Fact]
    public void NullAndStringShowAsText()
    {
        Assert.Equal("null", Show(ResultSamples.Success(new NullNode())).ValueText);
        Assert.Equal("hello", Show(ResultSamples.Success(ResultSamples.Text("hello"))).ValueText);
    }

    [Fact]
    public void ACutStringTellsTheFullLength()
    {
        ResultsViewModel model = Show(ResultSamples.Success(new TextNode("abc", 12_345)));

        Assert.Equal("abc", model.ValueText);
        Assert.Contains("12,345", model.TruncationText, StringComparison.Ordinal);
    }

    [Fact]
    public void AnObjectShowsAsAnExpandableTreeWithLazyChildren()
    {
        ResultsViewModel model = Show(ResultSamples.Success(ResultSamples.Nested()));

        Assert.Equal(ResultPresentation.Tree, model.Presentation);
        ResultTreeItem root = Assert.Single(model.Tree);
        Assert.Equal("Order", root.TypeText);
        Assert.Equal(["Id", "Customer", "Lines", "Parent", "Notes"], root.Children.Select(child => child.Label));
        Assert.Same(root.Children, root.Children);
        ResultTreeItem customer = root.Children[1];
        Assert.Equal(["Name", "Age", "City"], customer.Children.Select(child => child.Label));
        Assert.Equal("[0]", root.Children[2].Children[0].Label);
    }

    [Fact]
    public void ACycleAndANullShowTheirMeaning()
    {
        ResultTreeItem root = Show(ResultSamples.Success(ResultSamples.Nested())).Tree.Single();

        ResultTreeItem parent = root.Children.Single(child => child.Label == "Parent");
        Assert.True(parent.IsTruncated);
        Assert.Contains("refers back", parent.TruncationText, StringComparison.Ordinal);
        Assert.Equal("null", root.Children.Single(child => child.Label == "Notes").Summary);
        Assert.Contains("Parent, Order: Not expanded", parent.AccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public void AHomogeneousListOfObjectsShowsAsATable()
    {
        ResultsViewModel model = Show(ResultSamples.Success(ResultSamples.People(5)));

        Assert.Equal(ResultPresentation.Table, model.Presentation);
        Assert.Equal(["Name", "Age", "City"], model.Table!.Columns);
        Assert.Equal(5, model.Table.Rows.Count);
        Assert.Equal("Person 1", model.Table.Rows[0][0]);
        Assert.Equal("21", model.Table.Rows[0][1]);
        Assert.Equal(string.Empty, model.Table.Rows[0][9]);
    }

    [Fact]
    public void ATruncatedListShowsAClearNoticeAndStaysATable()
    {
        ResultsViewModel model = Show(ResultSamples.Success(ResultSamples.People(1_000, TruncationReason.ItemLimit)));

        Assert.Equal(ResultPresentation.Table, model.Presentation);
        Assert.Equal(1_000, model.Table!.Rows.Count);
        Assert.Contains("first items", model.TruncationText, StringComparison.Ordinal);
    }

    [Fact]
    public void AListOfMixedValuesOrDifferentPropertiesShowsAsATree()
    {
        SequenceNode mixed = new SequenceNode("object[]", [ResultSamples.Int(1), ResultSamples.Text("two")], null);
        SequenceNode different = new SequenceNode("object[]", [ResultSamples.Person("A", 1, "B"), new ObjectNode("X", [new ResultProperty("Other", new NullNode())], null)], null);
        SequenceNode scalars = new SequenceNode("int[]", [ResultSamples.Int(1), ResultSamples.Int(2)], null);
        SequenceNode empty = new SequenceNode("int[]", [], null);

        Assert.Equal(ResultPresentation.Tree, Show(ResultSamples.Success(mixed)).Presentation);
        Assert.Equal(ResultPresentation.Tree, Show(ResultSamples.Success(different)).Presentation);
        Assert.Equal(ResultPresentation.Tree, Show(ResultSamples.Success(scalars)).Presentation);
        Assert.Equal(ResultPresentation.Tree, Show(ResultSamples.Success(empty)).Presentation);
    }

    [Fact]
    public void ADictionaryShowsAsAKeyValueTable()
    {
        SequenceNode dictionary = new SequenceNode(
            "Dictionary",
            [
                new ObjectNode("KeyValuePair", [new ResultProperty("Key", ResultSamples.Text("a")), new ResultProperty("Value", ResultSamples.Int(1))], null),
                new ObjectNode("KeyValuePair", [new ResultProperty("Key", ResultSamples.Text("b")), new ResultProperty("Value", ResultSamples.Int(2))], null),
            ],
            null);

        ResultsViewModel model = Show(ResultSamples.Success(dictionary));

        Assert.Equal(["Key", "Value"], model.Table!.Columns);
    }

    [Fact]
    public void ANestedValueInACellShowsAsOneLineSummary()
    {
        ObjectNode row = new ObjectNode("Row", [new ResultProperty("Inner", ResultSamples.Person("A", 1, "B")), new ResultProperty("Items", new SequenceNode("System.Int32[]", [ResultSamples.Int(1)], null))], null);

        ResultsViewModel model = Show(ResultSamples.Success(new SequenceNode("Row[]", [row], null)));

        Assert.Equal("Person 3 properties", model.Table!.Rows[0][0]);
        Assert.Equal("Int32[] 1 item", model.Table.Rows[0][1]);
    }

    [Fact]
    public void ACompilationFailureShowsTheProblemsWithPlaceAndCode()
    {
        RunReport report = ResultSamples.Report(ExecutionOutcome.CompilationFailed, diagnostics: [new Diagnostic(DiagnosticSeverity.Error, "CS1525", "Invalid expression term ';'", new SourceLocation(2, 9)), new Diagnostic(DiagnosticSeverity.Warning, null, "A warning", null)]);

        ResultsViewModel model = Show(report);

        Assert.Equal("Compilation failed", model.StatusText);
        Assert.Equal("error", model.StatusKind);
        Assert.Equal("Problems (2)", model.DiagnosticsTitle);
        Assert.Equal("Line 2, column 9 · CS1525", model.Diagnostics[0].PlaceText);
        Assert.Equal("Error, Line 2, column 9 · CS1525: Invalid expression term ';'", model.Diagnostics[0].AccessibleName);
        Assert.Equal("Warning: A warning", model.Diagnostics[1].AccessibleName);
        Assert.Contains("did not compile", model.EmptyText, StringComparison.Ordinal);
        Assert.True(model.IsEmpty);
    }

    [Theory]
    [InlineData(ExecutionOutcome.RuntimeFailed, "Failed with an exception", "error")]
    [InlineData(ExecutionOutcome.Canceled, "Canceled", "warning")]
    [InlineData(ExecutionOutcome.TimedOut, "Stopped: time limit reached", "warning")]
    [InlineData(ExecutionOutcome.WorkerFailed, "The worker stopped unexpectedly", "error")]
    public void EachFailureHasItsOwnStatusPhraseAndKind(ExecutionOutcome outcome, string text, string kind)
    {
        ResultsViewModel model = Show(ResultSamples.Report(outcome));

        Assert.Equal(text, model.StatusText);
        Assert.Equal(kind, model.StatusKind);
    }

    [Fact]
    public void AFailedRunKeepsTheValueOfTheLastSuccessfulRun()
    {
        ResultsViewModel model = new ResultsViewModel();
        model.Show(ResultSamples.Success(ResultSamples.Int(7)), _now);

        model.Show(ResultSamples.Report(ExecutionOutcome.TimedOut), _now.AddSeconds(5));

        Assert.Equal("Stopped: time limit reached", model.StatusText);
        Assert.Equal("7", model.ValueText);
        Assert.True(model.IsShowingPrevious);
        Assert.True(model.CanCopy);

        model.Show(ResultSamples.Success(ResultSamples.Int(8)), _now.AddSeconds(10));

        Assert.Equal("8", model.ValueText);
        Assert.False(model.IsShowingPrevious);
    }

    [Fact]
    public void ASuccessWithNoValueClearsTheOldValue()
    {
        ResultsViewModel model = new ResultsViewModel();
        model.Show(ResultSamples.Success(ResultSamples.Int(7)), _now);

        model.Show(ResultSamples.Success(null), _now);

        Assert.True(model.IsEmpty);
        Assert.False(model.IsShowingPrevious);
    }

    [Fact]
    public void ASuccessWithNoValueSaysSoAndForgetsAnEarlierFailureText()
    {
        ResultsViewModel model = Show(ResultSamples.Report(ExecutionOutcome.CompilationFailed, diagnostics: [new Diagnostic(DiagnosticSeverity.Error, "CS1", "x", null)]));
        Assert.Contains("did not compile", model.EmptyText, StringComparison.Ordinal);

        model.Show(ResultSamples.Success(null), _now);

        Assert.Equal(ResultsViewModel.NO_VALUE_TEXT, model.EmptyText);
    }

    [Fact]
    public void ACopyFailureShowsANoticeThatTheNextRunClears()
    {
        ResultsViewModel model = Show(ResultSamples.Success(ResultSamples.Int(1)));

        model.ReportCopyFailed();
        Assert.Equal(ResultsViewModel.COPY_FAILED_TEXT, model.CopyNotice);

        model.Show(ResultSamples.Success(ResultSamples.Int(2)), _now);
        Assert.Equal(string.Empty, model.CopyNotice);
    }

    [Fact]
    public void OutputIsShownAndACutOutputSaysSo()
    {
        RunReport report = ResultSamples.Report(ExecutionOutcome.Success, stdout: "hello\n", stderr: "warn\n", stdoutOriginal: 5_000_000);

        ResultsViewModel model = Show(report);

        Assert.Equal("hello\n", model.Stdout);
        Assert.Equal("warn\n", model.Stderr);
        Assert.Contains("5,000,000", model.StdoutNote, StringComparison.Ordinal);
        Assert.Equal(string.Empty, model.StderrNote);
    }

    [Fact]
    public void ExecutionTextHasTheIdAndTheTime()
    {
        RunReport report = ResultSamples.Success(ResultSamples.Int(1));

        ResultsViewModel model = Show(report);

        Assert.Contains(report.ExecutionId.ToString(), model.ExecutionText, StringComparison.Ordinal);
        Assert.Contains("2026-03-01", model.ExecutionText, StringComparison.Ordinal);
    }

    [Fact]
    public void CopyGivesTheBoundedValueAsJson()
    {
        ResultsViewModel model = Show(ResultSamples.Success(ResultSamples.Nested()));

        string json = model.GetCopyText()!;

        Assert.Contains("\"kind\": \"object\"", json, StringComparison.Ordinal);
        Assert.Contains("Ada Lovelace", json, StringComparison.Ordinal);
        Assert.Contains("\"Reason\": \"Cycle\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearReturnsToTheStartState()
    {
        ResultsViewModel model = Show(ResultSamples.Success(ResultSamples.Nested(), "out"));

        model.Clear();

        Assert.True(model.IsEmpty);
        Assert.Equal(ResultsViewModel.NO_RUN_TEXT, model.StatusText);
        Assert.Equal(string.Empty, model.Stdout);
        Assert.False(model.CanCopy);
    }

    [Fact]
    public void ALargeBoundedResultBuildsQuicklyAndOnlyCreatesTheRowsThatAreAsked()
    {
        // 1,000 rows of 3 properties each: 4,000 nodes, well inside the default limit of 50,000.
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

        ResultsViewModel model = Show(ResultSamples.Success(ResultSamples.People(1_000)));
        SequenceNode wide = new SequenceNode("object[]", [.. Enumerable.Range(0, 1_000).Select(i => i % 2 == 0 ? (ResultNode)ResultSamples.Nested() : ResultSamples.Int(i))], TruncationReason.ItemLimit);
        ResultsViewModel tree = Show(ResultSamples.Success(wide));
        ResultTreeItem root = tree.Tree.Single();

        Assert.Equal(1_000, model.Table!.Rows.Count);
        Assert.Equal(1_000, root.Children.Count);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Building took {watch.Elapsed}.");
    }
}
