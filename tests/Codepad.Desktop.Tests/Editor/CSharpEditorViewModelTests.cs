using Codepad.Application.Editing;
using Codepad.Desktop.Editor;
using Codepad.Domain.Results;
using Codepad.Tests.Experiments;

namespace Codepad.Desktop.Tests.Editor;

public sealed class CSharpEditorViewModelTests
{
    private static readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ShowsTheDiagnosticsOfTheCurrentTextAfterAPause()
    {
        Fixture fixture = new Fixture();
        fixture.Model.Load("int x = ;");
        Assert.Equal(CSharpEditorViewModel.CHECKING_TEXT, fixture.Model.SummaryText);

        fixture.Time.Advance(_debounce);
        await fixture.Analyzer.Started.WaitAsync(_timeout, TestContext.Current.CancellationToken);
        await fixture.WaitForUiAsync(() => fixture.Model.Diagnostics.Count == 1);

        DiagnosticItem item = Assert.Single(fixture.Model.Diagnostics);
        Assert.Equal("Error", item.SeverityText);
        Assert.Equal("1 error", fixture.Model.SummaryText);
        Assert.Equal("Error, Line 1, column 9 · T0001: int x = ;", item.AccessibleName);
    }

    [Fact]
    public async Task AnEditEmptiesTheListAndAStaleResultNeverReturns()
    {
        Fixture fixture = new Fixture { Analyzer = { Gate = new TaskCompletionSource() } };
        fixture.Model.Load("old");
        fixture.Time.Advance(_debounce);
        await fixture.Analyzer.Started.WaitAsync(_timeout, TestContext.Current.CancellationToken);

        // The user types while the first analysis still runs. The result of "old" must not show.
        fixture.Model.OnTextEdited("new");
        fixture.Analyzer.Gate.SetResult();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        fixture.RunUi();

        Assert.Empty(fixture.Model.Diagnostics);
        Assert.Equal(CSharpEditorViewModel.CHECKING_TEXT, fixture.Model.SummaryText);

        fixture.Time.Advance(_debounce);
        await fixture.WaitForUiAsync(() => fixture.Model.Diagnostics.Count == 1);

        Assert.Equal("new", fixture.Model.Diagnostics[0].Source.Message);
    }

    [Fact]
    public async Task LoadingAnotherDocumentNeverShowsTheDiagnosticsOfTheOldOne()
    {
        Fixture fixture = new Fixture { Analyzer = { Gate = new TaskCompletionSource() } };
        fixture.Model.Load("tab one");
        fixture.Time.Advance(_debounce);
        await fixture.Analyzer.Started.WaitAsync(_timeout, TestContext.Current.CancellationToken);

        fixture.Model.Load("tab two");
        fixture.Analyzer.Gate.SetResult();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        fixture.RunUi();

        Assert.Empty(fixture.Model.Diagnostics);
        Assert.Equal("tab two", fixture.Model.Text);
    }

    [Fact]
    public void ReportsEditsToTheContentListenerButNotLoads()
    {
        Fixture fixture = new Fixture();
        List<string> changes = [];
        fixture.Model.ContentChanged += changes.Add;

        fixture.Model.Load("loaded");
        fixture.Model.OnTextEdited("edited");

        Assert.Equal(["edited"], changes);
    }

    [Fact]
    public void LoadRaisesDocumentLoadedWithoutRaisingContentChanged()
    {
        Fixture fixture = new Fixture();
        List<string> loaded = [];
        List<string> edits = [];
        fixture.Model.DocumentLoaded += loaded.Add;
        fixture.Model.ContentChanged += edits.Add;

        fixture.Model.Load("a");
        // The view sets the editor text from DocumentLoaded. That raises an edit callback, which the model must ignore.
        fixture.Model.DocumentLoaded += text => fixture.Model.OnTextEdited(text);
        fixture.Model.Load("b");

        Assert.Equal(["a", "b"], loaded);
        Assert.Empty(edits);
    }

    [Fact]
    public async Task AnAnalyzerFailureShowsAMessageAndIsReported()
    {
        Fixture fixture = new Fixture { Analyzer = { Failure = new InvalidOperationException("private detail") } };
        fixture.Model.Load("x");
        fixture.Time.Advance(_debounce);

        await fixture.WaitForUiAsync(() => fixture.Model.SummaryText == CSharpEditorViewModel.ANALYSIS_UNAVAILABLE_TEXT);

        Assert.Equal([nameof(InvalidOperationException)], fixture.Failures);
    }

    [Fact]
    public async Task SummaryCountsErrorsAndWarnings()
    {
        Fixture fixture = new Fixture
        {
            Analyzer =
            {
                Result =
                [
                    new EditorDiagnostic(DiagnosticSeverity.Error, "E1", "a", 0, 1, 1, 1),
                    new EditorDiagnostic(DiagnosticSeverity.Error, "E2", "b", 1, 1, 1, 2),
                    new EditorDiagnostic(DiagnosticSeverity.Warning, "W1", "c", 2, 1, 1, 3),
                ],
            },
        };
        fixture.Model.Load("abc");
        fixture.Time.Advance(_debounce);

        await fixture.WaitForUiAsync(() => fixture.Model.Diagnostics.Count == 3);

        Assert.Equal("2 errors, 1 warning", fixture.Model.SummaryText);
    }

    private sealed class Fixture
    {
        private readonly Queue<Action> ui = new Queue<Action>();

        public Fixture()
        {
            Model = new CSharpEditorViewModel(new NoHighlighter(), Analyzer, Time, _debounce, action => { lock (ui) { ui.Enqueue(action); } }, Failures.Add);
        }

        public ManualTimeProvider Time { get; } = new ManualTimeProvider();

        public FakeAnalyzer Analyzer { get; } = new FakeAnalyzer();

        public List<string> Failures { get; } = [];

        public CSharpEditorViewModel Model { get; }

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

    private sealed class NoHighlighter : ICSharpHighlighter
    {
        public IReadOnlyList<HighlightSpan> Highlight(string source) => [];
    }

    private sealed class FakeAnalyzer : ICSharpAnalyzer
    {
        private readonly TaskCompletionSource started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;

        public TaskCompletionSource? Gate { get; set; }

        public Exception? Failure { get; set; }

        public IReadOnlyList<EditorDiagnostic>? Result { get; set; }

        public async Task<EditorAnalysis> AnalyzeAsync(string source, CancellationToken cancellationToken)
        {
            started.TrySetResult();
            if (Gate is not null)
            {
                await Gate.Task;
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            return new EditorAnalysis(Result ?? [new EditorDiagnostic(DiagnosticSeverity.Error, "T0001", source, 0, 1, 1, 9)]);
        }
    }
}
