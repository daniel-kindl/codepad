using Codepad.Application.Execution;
using Codepad.Application.Experiments;
using Codepad.Application.History;
using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Tests.Experiments;

namespace Codepad.Tests.History;

public sealed class RunHistoryServiceTests
{
    private readonly FakeRunner runner = new FakeRunner();
    private readonly FakeHistory history = new FakeHistory();
    private readonly ManualTimeProvider clock = new ManualTimeProvider();

    private RunHistoryService NewService() => new RunHistoryService(runner, history, clock);

    [Fact]
    public async Task RunSnapshotsTheExperimentRunsItAndStoresTheRecord()
    {
        Experiment experiment = RunSamples.Experiment("1 + 1");

        CompletedRun run = await NewService().RunAsync(experiment, TestContext.Current.CancellationToken);

        Assert.Null(run.StorageFailure);
        Assert.Equal("1 + 1", run.Record.Request.Source);
        Assert.Equal(experiment.Id, run.Record.Request.ExperimentId);
        Assert.Same(run.Record.Request, runner.Requests.Single());
        Assert.Same(run.Record, history.Added.Single());
        Assert.Equal(clock.GetUtcNow(), run.Record.CompletedAt);
    }

    [Fact]
    public async Task EditBetweenTwoRunsGivesTwoDifferentSnapshots()
    {
        Experiment first = RunSamples.Experiment("1 + 1");
        Experiment second = first.WithSource("2 + 2", clock.GetUtcNow());
        RunHistoryService service = NewService();

        CompletedRun a = await service.RunAsync(first, TestContext.Current.CancellationToken);
        CompletedRun b = await service.RunAsync(second, TestContext.Current.CancellationToken);

        Assert.NotEqual(a.Record.Request.Id, b.Record.Request.Id);
        Assert.Equal(["1 + 1", "2 + 2"], history.Added.Select(record => record.Request.Source));
    }

    [Theory]
    [InlineData(ExecutionOutcome.CompilationFailed)]
    [InlineData(ExecutionOutcome.RuntimeFailed)]
    [InlineData(ExecutionOutcome.Canceled)]
    [InlineData(ExecutionOutcome.TimedOut)]
    [InlineData(ExecutionOutcome.WorkerFailed)]
    public async Task EveryOutcomeIsStoredAsTheRunnerReportedIt(ExecutionOutcome outcome)
    {
        runner.Outcome = outcome;

        CompletedRun run = await NewService().RunAsync(RunSamples.Experiment(), TestContext.Current.CancellationToken);

        Assert.Equal(outcome, run.Record.Report.Result.Outcome);
        Assert.Equal(outcome, history.Added.Single().Report.Result.Outcome);
    }

    [Fact]
    public async Task RerunUsesTheSavedSnapshotWithANewIdAndKeepsTheOldRecord()
    {
        RunHistoryService service = NewService();
        CompletedRun original = await service.RunAsync(RunSamples.Experiment("old source"), TestContext.Current.CancellationToken);

        CompletedRun again = await service.RerunAsync(original.Record, TestContext.Current.CancellationToken);

        Assert.NotEqual(original.Record.Request.Id, again.Record.Request.Id);
        Assert.Equal("old source", again.Record.Request.Source);
        Assert.Equal(original.Record.Request.ExperimentRevision, again.Record.Request.ExperimentRevision);
        Assert.Equal(2, history.Added.Count);
        Assert.Equal(original.Record.Request.Id, history.Added[0].Request.Id);
        Assert.Equal("old source", history.Added[0].Request.Source);
    }

    [Fact]
    public async Task StorageFailureKeepsTheRunResult()
    {
        history.FailWith = "disk_full";

        CompletedRun run = await NewService().RunAsync(RunSamples.Experiment(), TestContext.Current.CancellationToken);

        Assert.Equal("disk_full", run.StorageFailure);
        Assert.Equal(ExecutionOutcome.Success, run.Record.Report.Result.Outcome);
    }

    [Fact]
    public async Task JsonExperimentIsRefused()
    {
        Experiment json = Experiment.Create(ExperimentId.New(), OperationKind.Json, "j", "{}", RunSamples.Start);

        await Assert.ThrowsAsync<ArgumentException>(() => NewService().RunAsync(json, TestContext.Current.CancellationToken));

        Assert.Empty(runner.Requests);
        Assert.Empty(history.Added);
    }

    private sealed class FakeRunner : ICSharpRunner
    {
        public List<ExecutionRequest> Requests { get; } = [];

        public ExecutionOutcome Outcome { get; set; } = ExecutionOutcome.Success;

        public Task<RunReport> RunAsync(ExecutionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(RunSamples.Report(request, Outcome));
        }
    }

    private sealed class FakeHistory : IRunHistoryRepository
    {
        public List<RunRecord> Added { get; } = [];

        public string? FailWith { get; set; }

        public Task AddAsync(RunRecord record, CancellationToken cancellationToken = default)
        {
            if (FailWith is not null)
            {
                throw new ExperimentStorageException(FailWith);
            }

            Added.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RunSummary>> ListAsync(ExperimentId experimentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RunRecord?> GetAsync(ExecutionId id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
