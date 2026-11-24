using System.Diagnostics;
using Codepad.Application.History;
using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Domain.Results;
using Codepad.Infrastructure.Storage;
using Codepad.Tests.CSharpRuns;
using Codepad.Tests.Experiments;

namespace Codepad.Tests.Hardening;

/// <summary>
/// Runs real code that makes huge, cyclic or slow output in the real worker. The result and the output stay inside the
/// limits, the run ends in time, and the saved run reads back from SQLite.
/// </summary>
public sealed class BoundedOutputIntegrationTests : IDisposable
{
    private readonly WorkerHarness harness = new WorkerHarness();
    private readonly TempDatabase temp = new TempDatabase();

    public void Dispose()
    {
        harness.Dispose();
        temp.Dispose();
    }

    private async Task<(CompletedRun Run, SqliteRunHistoryRepository History, Experiment Experiment)> RunAndStoreAsync(string source)
    {
        SqliteDatabase database = temp.Open();
        Experiment experiment = Experiment.Create(ExperimentId.New(), OperationKind.CSharp, "bounded", source, DateTimeOffset.UtcNow);
        await new SqliteExperimentRepository(database).SaveAsync(experiment, TestContext.Current.CancellationToken);
        SqliteRunHistoryRepository history = new SqliteRunHistoryRepository(database);
        RunHistoryService service = new RunHistoryService(harness.Runner, history, TimeProvider.System, () => new OperationPolicy(OperationKind.CSharp, 1, OperationTrigger.ExplicitOnly, TimeSpan.FromSeconds(60), ResultPolicy.Default));
        CompletedRun run = await service.RunAsync(experiment, TestContext.Current.CancellationToken);
        return (run, history, experiment);
    }

    private static async Task AssertReadableAsync(SqliteRunHistoryRepository history, Experiment experiment, CompletedRun run)
    {
        Assert.Null(run.StorageFailure);
        IReadOnlyList<RunSummary> list = await history.ListAsync(experiment.Id, TestContext.Current.CancellationToken);
        RunSummary summary = Assert.Single(list);
        RunRecord? read = await history.GetAsync(summary.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(read);
        Assert.Equal(run.Record.Report.Result.Outcome, read.Report.Result.Outcome);
        if (read.Report.Result.Value is ResultNode value)
        {
            Assert.Null(ResultPolicy.Default.FindExceededLimit(value));
        }

        Assert.True(read.Report.Stdout.Text.Length <= Codepad.Contracts.WorkerProtocol.DefaultMaxOutputChars);
    }

    [Fact]
    public async Task ACyclicValueIsCutAndTheSavedRunReadsBack()
    {
        (CompletedRun run, SqliteRunHistoryRepository history, Experiment experiment) = await RunAndStoreAsync("var list = new List<object>(); list.Add(list); list.Add(new List<object> { list }); list");

        Assert.Equal(ExecutionOutcome.Success, run.Record.Report.Result.Outcome);
        SequenceNode root = Assert.IsType<SequenceNode>(run.Record.Report.Result.Value);
        Assert.Contains(root.Items, node => node is TruncatedNode { Reason: TruncationReason.Cycle });
        await AssertReadableAsync(history, experiment, run);
    }

    [Fact]
    public async Task AHugeSequenceOfBigObjectsStaysInsideTheResultPolicy()
    {
        (CompletedRun run, SqliteRunHistoryRepository history, Experiment experiment) = await RunAndStoreAsync("Enumerable.Range(0, 5_000_000).Select(i => new { Index = i, Text = new string('x', 5000) })");

        Assert.Equal(ExecutionOutcome.Success, run.Record.Report.Result.Outcome);
        Assert.NotNull(run.Record.Report.Result.Value);
        await AssertReadableAsync(history, experiment, run);
    }

    [Fact]
    public async Task AFiftyMillionCharacterStringIsCutAndKeepsItsLength()
    {
        (CompletedRun run, SqliteRunHistoryRepository history, Experiment experiment) = await RunAndStoreAsync("new string('y', 50_000_000)");

        TextNode text = Assert.IsType<TextNode>(run.Record.Report.Result.Value);
        Assert.True(text.IsTruncated);
        Assert.Equal(50_000_000, text.OriginalLength);
        Assert.Equal(ResultPolicy.Default.MaxTextLength, text.Value.Length);
        await AssertReadableAsync(history, experiment, run);
    }

    [Fact]
    public async Task AnOutputFloodIsCutAndTheRunStaysFast()
    {
        Stopwatch clock = Stopwatch.StartNew();

        (CompletedRun run, SqliteRunHistoryRepository history, Experiment experiment) = await RunAndStoreAsync("for (int i = 0; i < 20_000; i++) { Console.WriteLine(new string('z', 1000)); Console.Error.WriteLine(new string('e', 1000)); } 1");

        clock.Stop();
        Assert.Equal(ExecutionOutcome.Success, run.Record.Report.Result.Outcome);
        Assert.True(run.Record.Report.Stdout.IsTruncated);
        Assert.True(run.Record.Report.Stderr.IsTruncated);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"The run took {clock.Elapsed}.");
        await AssertReadableAsync(history, experiment, run);
    }

    [Fact]
    public async Task ASlowEndlessSequenceIsCutByTheTimeBudgetAndTheRunEnds()
    {
        Stopwatch clock = Stopwatch.StartNew();

        (CompletedRun run, SqliteRunHistoryRepository history, Experiment experiment) = await RunAndStoreAsync("IEnumerable<int> Slow() { while (true) { Thread.Sleep(200); yield return 1; } } Slow()");

        clock.Stop();
        Assert.Equal(ExecutionOutcome.Success, run.Record.Report.Result.Outcome);
        SequenceNode root = Assert.IsType<SequenceNode>(run.Record.Report.Result.Value);
        Assert.Equal(TruncationReason.TimeLimit, root.Truncation);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"The run took {clock.Elapsed}.");
        await AssertReadableAsync(history, experiment, run);
    }

    [Fact]
    public async Task ADeepNestedValueBecomesADepthCutNotAFailure()
    {
        (CompletedRun run, SqliteRunHistoryRepository history, Experiment experiment) = await RunAndStoreAsync("object o = 1; for (int i = 0; i < 5000; i++) { o = new object[] { o }; } o");

        Assert.Equal(ExecutionOutcome.Success, run.Record.Report.Result.Outcome);
        await AssertReadableAsync(history, experiment, run);
    }
}
