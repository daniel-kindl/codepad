using Codepad.Application.Editing;
using Codepad.Domain.Results;
using Codepad.Tests.Experiments;

namespace Codepad.Tests.Editing;

public sealed class DiagnosticsCoordinatorTests
{
    private static readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task AnalyzesTheNewestTextAfterAPauseInTheEdits()
    {
        ManualTimeProvider time = new ManualTimeProvider();
        FakeAnalyzer analyzer = new FakeAnalyzer();
        using DiagnosticsCoordinator coordinator = new DiagnosticsCoordinator(analyzer, time, _debounce);
        TaskCompletionSource<AnalysisUpdate> published = Subscribe(coordinator);

        coordinator.Update("a");
        time.Advance(TimeSpan.FromMilliseconds(100));
        coordinator.Update("ab");
        time.Advance(TimeSpan.FromMilliseconds(200));

        // The second edit restarted the wait, so no analysis has started yet.
        Assert.Empty(analyzer.Sources);

        time.Advance(TimeSpan.FromMilliseconds(100));
        AnalysisUpdate update = await published.Task.WaitAsync(_timeout, TestContext.Current.CancellationToken);

        Assert.Equal(["ab"], analyzer.Sources);
        Assert.Equal(2, update.Version);
        Assert.Equal("ab", Assert.Single(update.Analysis.Diagnostics).Message);
    }

    [Fact]
    public async Task DropsTheResultOfAnOlderVersion()
    {
        ManualTimeProvider time = new ManualTimeProvider();
        FakeAnalyzer analyzer = new FakeAnalyzer { Gate = new TaskCompletionSource() };
        using DiagnosticsCoordinator coordinator = new DiagnosticsCoordinator(analyzer, time, _debounce);
        List<AnalysisUpdate> updates = [];
        coordinator.Published += update => { lock (updates) { updates.Add(update); } };

        coordinator.Update("old");
        time.Advance(_debounce);
        await analyzer.Started.WaitAsync(_timeout, TestContext.Current.CancellationToken);

        // The old analysis is still running. A new edit makes it stale, even if the analyzer ignores the cancellation.
        coordinator.Update("new");
        analyzer.Gate.SetResult();
        time.Advance(_debounce);
        await WaitUntilAsync(() => analyzer.Sources.Count == 2);
        await WaitUntilAsync(() => { lock (updates) { return updates.Count == 1; } });

        lock (updates)
        {
            AnalysisUpdate update = Assert.Single(updates);
            Assert.Equal(2, update.Version);
            Assert.Equal("new", Assert.Single(update.Analysis.Diagnostics).Message);
        }
    }

    [Fact]
    public async Task RaisesNoEventAfterDispose()
    {
        ManualTimeProvider time = new ManualTimeProvider();
        FakeAnalyzer analyzer = new FakeAnalyzer { Gate = new TaskCompletionSource() };
        DiagnosticsCoordinator coordinator = new DiagnosticsCoordinator(analyzer, time, _debounce);
        int events = 0;
        coordinator.Published += _ => Interlocked.Increment(ref events);
        coordinator.Failed += _ => Interlocked.Increment(ref events);

        coordinator.Update("tab one");
        time.Advance(_debounce);
        await analyzer.Started.WaitAsync(_timeout, TestContext.Current.CancellationToken);
        coordinator.Dispose();
        analyzer.Gate.SetResult();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(0, Volatile.Read(ref events));
        Assert.False(coordinator.IsCurrent(1));
    }

    [Fact]
    public async Task ReportsAnAnalyzerFailureAsATypeNameOnly()
    {
        ManualTimeProvider time = new ManualTimeProvider();
        FakeAnalyzer analyzer = new FakeAnalyzer { Failure = new InvalidOperationException("secret path /home/user") };
        using DiagnosticsCoordinator coordinator = new DiagnosticsCoordinator(analyzer, time, _debounce);
        TaskCompletionSource<string> failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Failed += failed.SetResult;

        coordinator.Update("x");
        time.Advance(_debounce);

        Assert.Equal(nameof(InvalidOperationException), await failed.Task.WaitAsync(_timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsCurrentOnlyForTheNewestVersion()
    {
        ManualTimeProvider time = new ManualTimeProvider();
        using DiagnosticsCoordinator coordinator = new DiagnosticsCoordinator(new FakeAnalyzer(), time, _debounce);

        coordinator.Update("a");
        coordinator.Update("b");

        Assert.False(coordinator.IsCurrent(1));
        Assert.True(coordinator.IsCurrent(2));
        Assert.Equal(2, coordinator.Version);
        await Task.CompletedTask;
    }

    private static TaskCompletionSource<AnalysisUpdate> Subscribe(DiagnosticsCoordinator coordinator)
    {
        TaskCompletionSource<AnalysisUpdate> published = new TaskCompletionSource<AnalysisUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Published += update => published.TrySetResult(update);
        return published;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + _timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not become true.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class FakeAnalyzer : ICSharpAnalyzer
    {
        private readonly List<string> sources = [];
        private readonly TaskCompletionSource started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> Sources
        {
            get
            {
                lock (sources)
                {
                    return [.. sources];
                }
            }
        }

        public Task Started => started.Task;

        public TaskCompletionSource? Gate { get; init; }

        public Exception? Failure { get; init; }

        public async Task<EditorAnalysis> AnalyzeAsync(string source, CancellationToken cancellationToken)
        {
            lock (sources)
            {
                sources.Add(source);
            }

            started.TrySetResult();
            if (Gate is not null)
            {
                await Gate.Task;
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            return new EditorAnalysis([new EditorDiagnostic(DiagnosticSeverity.Error, "T0001", source, 0, 1, 1, 1)]);
        }
    }
}
