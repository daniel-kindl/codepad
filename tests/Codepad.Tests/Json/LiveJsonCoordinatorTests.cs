using Codepad.Application.Json;
using Codepad.Tests.Experiments;

namespace Codepad.Tests.Json;

public sealed class LiveJsonCoordinatorTests
{
    private static readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task EvaluatesTheNewestTextOnceAfterAPause()
    {
        ManualTimeProvider time = new ManualTimeProvider();
        FakeEvaluator evaluator = new FakeEvaluator();
        using LiveJsonCoordinator coordinator = new LiveJsonCoordinator(evaluator, time, _debounce);
        TaskCompletionSource<JsonUpdate> published = new TaskCompletionSource<JsonUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Published += update => published.TrySetResult(update);

        coordinator.Update("1");
        time.Advance(TimeSpan.FromMilliseconds(200));
        coordinator.Update("12");
        time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Empty(evaluator.Texts);

        time.Advance(TimeSpan.FromMilliseconds(100));
        JsonUpdate update = await published.Task.WaitAsync(_timeout, TestContext.Current.CancellationToken);

        Assert.Equal(["12"], evaluator.Texts);
        Assert.Equal(2, update.Version);
        Assert.True(update.Evaluation.IsValid);
    }

    [Fact]
    public async Task ADelayedOlderEvaluationNeverReplacesTheNewestResult()
    {
        ManualTimeProvider time = new ManualTimeProvider();
        FakeEvaluator evaluator = new FakeEvaluator { Gate = new TaskCompletionSource() };
        using LiveJsonCoordinator coordinator = new LiveJsonCoordinator(evaluator, time, _debounce);
        List<JsonUpdate> updates = [];
        coordinator.Published += update => { lock (updates) { updates.Add(update); } };

        coordinator.Update("[1]");
        time.Advance(_debounce);
        await evaluator.Started.WaitAsync(_timeout, TestContext.Current.CancellationToken);

        // The first evaluation is slow and ignores the cancellation. The user types again.
        coordinator.Update("[2]");
        evaluator.Gate.SetResult();
        time.Advance(_debounce);
        await WaitUntilAsync(() => { lock (updates) { return updates.Count == 1; } });
        await Task.Delay(100, TestContext.Current.CancellationToken);

        lock (updates)
        {
            JsonUpdate update = Assert.Single(updates);
            Assert.Equal(2, update.Version);
            Assert.Contains("2", update.Evaluation.Formatted, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RaisesNoEventAfterDispose()
    {
        ManualTimeProvider time = new ManualTimeProvider();
        FakeEvaluator evaluator = new FakeEvaluator { Gate = new TaskCompletionSource() };
        LiveJsonCoordinator coordinator = new LiveJsonCoordinator(evaluator, time, _debounce);
        int events = 0;
        coordinator.Published += _ => Interlocked.Increment(ref events);
        coordinator.Failed += _ => Interlocked.Increment(ref events);

        coordinator.Update("[1]");
        time.Advance(_debounce);
        await evaluator.Started.WaitAsync(_timeout, TestContext.Current.CancellationToken);
        coordinator.Dispose();
        evaluator.Gate.SetResult();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(0, Volatile.Read(ref events));
        Assert.False(coordinator.IsCurrent(1));
    }

    [Fact]
    public async Task ReportsAnEvaluatorFailureAsATypeNameOnly()
    {
        ManualTimeProvider time = new ManualTimeProvider();
        FakeEvaluator evaluator = new FakeEvaluator { Failure = new InvalidOperationException("secret [1]") };
        using LiveJsonCoordinator coordinator = new LiveJsonCoordinator(evaluator, time, _debounce);
        TaskCompletionSource<string> failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Failed += failed.SetResult;

        coordinator.Update("[1]");
        time.Advance(_debounce);

        Assert.Equal(nameof(InvalidOperationException), await failed.Task.WaitAsync(_timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void OnlyTheNewestVersionIsCurrent()
    {
        using LiveJsonCoordinator coordinator = new LiveJsonCoordinator(new FakeEvaluator(), new ManualTimeProvider(), _debounce);

        coordinator.Update("a");
        coordinator.Update("b");

        Assert.False(coordinator.IsCurrent(1));
        Assert.True(coordinator.IsCurrent(2));
        Assert.Equal(2, coordinator.Version);
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

    private sealed class FakeEvaluator : IJsonEvaluator
    {
        private readonly List<string> texts = [];
        private readonly TaskCompletionSource started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly JsonEvaluator real = new JsonEvaluator();

        public IReadOnlyList<string> Texts
        {
            get
            {
                lock (texts)
                {
                    return [.. texts];
                }
            }
        }

        public Task Started => started.Task;

        public TaskCompletionSource? Gate { get; init; }

        public Exception? Failure { get; init; }

        public async Task<JsonEvaluation> EvaluateAsync(string text, CancellationToken cancellationToken)
        {
            lock (texts)
            {
                texts.Add(text);
            }

            started.TrySetResult();
            if (Failure is not null)
            {
                throw Failure;
            }

            if (Gate is not null)
            {
                // Ignore the cancellation on purpose: a real provider cannot always stop.
                await Gate.Task.ConfigureAwait(false);
            }

            return real.Evaluate(text);
        }
    }
}
