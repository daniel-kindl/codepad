using Codepad.Application.Json;
using Codepad.Desktop.Json;
using Codepad.Desktop.Results;
using Codepad.Tests.Experiments;

namespace Codepad.Desktop.Tests.Json;

public sealed class JsonToolViewModelTests
{
    private static readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ValidJsonFormatsAfterAPauseWithoutPressingRun()
    {
        Fixture fixture = new Fixture();

        fixture.Model.Load("{ \"a\": 1 }");
        Assert.Equal(JsonToolViewModel.CHECKING_TEXT, fixture.Model.SummaryText);
        fixture.Time.Advance(_debounce);
        await fixture.WaitForUiAsync(() => fixture.Model.FormattedText.Length > 0);

        Assert.Equal("{\n  \"a\": 1\n}", fixture.Model.FormattedText.ReplaceLineEndings("\n"));
        Assert.Equal(JsonToolViewModel.VALID_TEXT, fixture.Model.SummaryText);
        Assert.False(fixture.Model.HasProblem);
        Assert.True(fixture.Model.Results.IsTree);
        Assert.Single(fixture.Model.Results.Tree);
    }

    [Fact]
    public async Task InvalidJsonShowsTheLocationAndKeepsTheInputAndTheLastGoodPreview()
    {
        Fixture fixture = new Fixture();
        fixture.Model.Load("[1, 2]");
        fixture.Time.Advance(_debounce);
        await fixture.WaitForUiAsync(() => fixture.Model.FormattedText.Length > 0);

        fixture.Model.OnTextEdited("[1,\n 2,]");
        fixture.Time.Advance(_debounce);
        await fixture.WaitForUiAsync(() => fixture.Model.HasProblem);

        Assert.Equal("[1,\n 2,]", fixture.Model.Text);
        Assert.StartsWith("Invalid JSON at line 2, column 4", fixture.Model.SummaryText, StringComparison.Ordinal);
        Assert.True(fixture.Model.HasProblem);
        Assert.Contains("last valid", fixture.Model.FormattedNote, StringComparison.Ordinal);
        Assert.Contains("2", fixture.Model.FormattedText, StringComparison.Ordinal);
        Assert.True(fixture.Model.Results.IsShowingPrevious);
        RunDiagnosticRow row = Assert.Single(fixture.Model.Results.Diagnostics);
        Assert.Equal("Error", row.SeverityText);
        Assert.Equal("Line 2, column 4 · JSON_SYNTAX", row.PlaceText);
    }

    [Fact]
    public async Task ADelayedOlderEvaluationNeverReplacesTheNewestResult()
    {
        TaskCompletionSource gate = new TaskCompletionSource();
        Fixture fixture = new Fixture { Evaluator = { Gate = gate } };
        fixture.Model.Load("[1]");
        fixture.Time.Advance(_debounce);
        await fixture.Evaluator.Started.WaitAsync(_timeout, TestContext.Current.CancellationToken);

        // The first evaluation is slow. The user changes the text and the new text is bad JSON.
        fixture.Model.OnTextEdited("[1,");
        gate.SetResult();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        fixture.RunUi();

        // The late result of "[1]" is dropped, so nothing shows yet.
        Assert.Equal(string.Empty, fixture.Model.FormattedText);
        Assert.Equal(JsonToolViewModel.CHECKING_TEXT, fixture.Model.SummaryText);

        fixture.Time.Advance(_debounce);
        await fixture.WaitForUiAsync(() => fixture.Model.HasProblem);

        Assert.Equal(string.Empty, fixture.Model.FormattedText);
    }

    [Fact]
    public async Task LoadingAnotherDocumentNeverShowsTheResultOfTheOldOne()
    {
        TaskCompletionSource gate = new TaskCompletionSource();
        Fixture fixture = new Fixture { Evaluator = { Gate = gate } };
        fixture.Model.Load("[\"tab one\"]");
        fixture.Time.Advance(_debounce);
        await fixture.Evaluator.Started.WaitAsync(_timeout, TestContext.Current.CancellationToken);

        fixture.Model.Load("[\"tab two\"]");
        gate.SetResult();
        fixture.Time.Advance(_debounce);
        await fixture.WaitForUiAsync(() => fixture.Model.FormattedText.Length > 0);

        Assert.Contains("tab two", fixture.Model.FormattedText, StringComparison.Ordinal);
        Assert.DoesNotContain("tab one", fixture.Model.FormattedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyInputClearsThePreviewAndIsNotAnError()
    {
        Fixture fixture = new Fixture();
        fixture.Model.Load("[1]");
        fixture.Time.Advance(_debounce);
        await fixture.WaitForUiAsync(() => fixture.Model.FormattedText.Length > 0);

        fixture.Model.OnTextEdited(string.Empty);
        fixture.Time.Advance(_debounce);
        await fixture.WaitForUiAsync(() => fixture.Model.FormattedText.Length == 0);

        Assert.Equal(JsonToolViewModel.EMPTY_TEXT, fixture.Model.SummaryText);
        Assert.False(fixture.Model.HasProblem);
        Assert.True(fixture.Model.Results.IsEmpty);
    }

    [Fact]
    public async Task ALimitFailureIsExplainedAndTheInputStays()
    {
        Fixture fixture = new Fixture(new JsonEvaluator(new JsonLimits(MaxBytes: 10, MaxDepth: 64)));
        fixture.Model.Load("[1, 2, 3, 4, 5, 6, 7]");
        fixture.Time.Advance(_debounce);
        await fixture.WaitForUiAsync(() => fixture.Model.HasProblem);

        Assert.Contains("larger than the limit", fixture.Model.SummaryText, StringComparison.Ordinal);
        Assert.Equal("[1, 2, 3, 4, 5, 6, 7]", fixture.Model.Text);
    }

    [Fact]
    public void OnlyAnEditRaisesContentChangedAndLoadDoesNot()
    {
        Fixture fixture = new Fixture();
        List<string> changes = [];
        fixture.Model.ContentChanged += changes.Add;

        fixture.Model.Load("[1]");
        fixture.Model.OnTextEdited("[1, 2]");

        Assert.Equal(["[1, 2]"], changes);
    }

    [Fact]
    public void ReportingTheLoadedTextAgainIsNotAnEdit()
    {
        Fixture fixture = new Fixture();
        List<string> changes = [];
        fixture.Model.ContentChanged += changes.Add;
        fixture.Model.Load("[1]");

        fixture.Model.OnTextEdited("[1]");

        Assert.Empty(changes);
    }

    [Fact]
    public async Task AnEvaluatorFailureShowsAFriendlyLineAndReportsOnlyTheTypeName()
    {
        Fixture fixture = new Fixture { Evaluator = { Failure = new InvalidOperationException("secret [1]") } };
        fixture.Model.Load("[1]");
        fixture.Time.Advance(_debounce);
        await fixture.WaitForUiAsync(() => fixture.Failures.Count == 1);

        Assert.True(fixture.Model.HasProblem);
        Assert.Equal(string.Empty, fixture.Model.FormattedText);
        Assert.Equal([nameof(InvalidOperationException)], fixture.Failures);
        Assert.Equal(JsonToolViewModel.UNAVAILABLE_TEXT, fixture.Model.SummaryText);
    }

    private sealed class Fixture
    {
        private readonly Queue<Action> ui = new Queue<Action>();

        public Fixture(JsonEvaluator? real = null)
        {
            Evaluator = new GateEvaluator(real ?? new JsonEvaluator());
            Model = new JsonToolViewModel(Evaluator, Time, _debounce, action => { lock (ui) { ui.Enqueue(action); } }, Failures.Add);
        }

        public ManualTimeProvider Time { get; } = new ManualTimeProvider();

        public GateEvaluator Evaluator { get; }

        public List<string> Failures { get; } = [];

        public JsonToolViewModel Model { get; }

        public void RunUi()
        {
            while (true)
            {
                Action? next;
                lock (ui)
                {
                    next = ui.Count > 0 ? ui.Dequeue() : null;
                }

                if (next is null)
                {
                    return;
                }

                next();
            }
        }

        public async Task WaitForUiAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow + _timeout;
            while (true)
            {
                RunUi();
                if (condition())
                {
                    return;
                }

                Assert.True(DateTime.UtcNow < deadline, "The condition did not become true.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }
    }

    private sealed class GateEvaluator(JsonEvaluator real) : IJsonEvaluator
    {
        private readonly TaskCompletionSource started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;

        public TaskCompletionSource? Gate { get; set; }

        public Exception? Failure { get; set; }

        public async Task<JsonEvaluation> EvaluateAsync(string text, CancellationToken cancellationToken)
        {
            started.TrySetResult();
            if (Failure is not null)
            {
                throw Failure;
            }

            if (Gate is not null && text != "[1,")
            {
                // The gate holds back the first text only. A real provider cannot always stop, so the gate ignores the token.
                await Gate.Task.ConfigureAwait(false);
            }

            return real.Evaluate(text);
        }
    }
}
