using Codepad.Application.Experiments;
using Codepad.Application.History;
using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;
using Codepad.Domain.Results;
using Codepad.Infrastructure.Storage;
using Codepad.Tests.Experiments;

namespace Codepad.Tests.History;

public sealed class SqliteRunHistoryRepositoryTests : IDisposable
{
    private readonly TempDatabase temp = new TempDatabase();

    public void Dispose() => temp.Dispose();

    private async Task<(SqliteRunHistoryRepository History, Experiment Experiment)> NewAsync(int? retention = null)
    {
        SqliteDatabase database = temp.Open();
        Experiment experiment = RunSamples.Experiment();
        await new SqliteExperimentRepository(database).SaveAsync(experiment, TestContext.Current.CancellationToken);
        return (new SqliteRunHistoryRepository(database, retention), experiment);
    }

    [Fact]
    public async Task RecordReadsBackWithEveryField()
    {
        (SqliteRunHistoryRepository history, Experiment experiment) = await NewAsync();
        ResultNode value = new SequenceNode("System.Int32[]", [new ScalarNode("System.Int32", "1"), new TextNode("t", 5)], TruncationReason.ItemLimit);
        RunRecord record = RunSamples.Record(experiment, value: value);

        await history.AddAsync(record, TestContext.Current.CancellationToken);

        RunRecord? read = await history.GetAsync(record.Request.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(read);
        Assert.Equal(record.Request, read.Request);
        Assert.Equal(record.CompletedAt, read.CompletedAt);
        Assert.Equal(record.Report.SourceRevision, read.Report.SourceRevision);
        Assert.Equal(record.Report.Result.Outcome, read.Report.Result.Outcome);
        Assert.Equal(record.Report.Result.Diagnostics, read.Report.Result.Diagnostics);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(value), System.Text.Json.JsonSerializer.Serialize(read.Report.Result.Value));
        Assert.Equal(record.Report.Stdout, read.Report.Stdout);
        Assert.Equal(record.Report.Stderr, read.Report.Stderr);
        Assert.Equal(record.Report.Host, read.Report.Host);
    }

    [Fact]
    public async Task UnknownIdReturnsNull()
    {
        (SqliteRunHistoryRepository history, _) = await NewAsync();

        Assert.Null(await history.GetAsync(ExecutionId.New(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TwoRunsWithAnEditBetweenThemKeepTwoDistinctSnapshots()
    {
        (SqliteRunHistoryRepository history, Experiment first) = await NewAsync();
        Experiment second = first.WithSource("2 + 2", RunSamples.Start.AddMinutes(1)).WithInput("in", RunSamples.Start.AddMinutes(1));
        RunRecord a = RunSamples.Record(first, minutes: 1, value: new ScalarNode("System.Int32", "2"));
        RunRecord b = RunSamples.Record(second, minutes: 2, value: new ScalarNode("System.Int32", "4"));

        await history.AddAsync(a, TestContext.Current.CancellationToken);
        await history.AddAsync(b, TestContext.Current.CancellationToken);

        RunRecord readA = (await history.GetAsync(a.Request.Id, TestContext.Current.CancellationToken))!;
        RunRecord readB = (await history.GetAsync(b.Request.Id, TestContext.Current.CancellationToken))!;
        Assert.Equal("1 + 1", readA.Request.Source);
        Assert.Equal(string.Empty, readA.Request.Input);
        Assert.Equal("2 + 2", readB.Request.Source);
        Assert.Equal("in", readB.Request.Input);
        Assert.Equal("2", ((ScalarNode)readA.Report.Result.Value!).Value);
        Assert.Equal("4", ((ScalarNode)readB.Report.Result.Value!).Value);
    }

    [Theory]
    [InlineData(ExecutionOutcome.Success)]
    [InlineData(ExecutionOutcome.CompilationFailed)]
    [InlineData(ExecutionOutcome.RuntimeFailed)]
    [InlineData(ExecutionOutcome.Canceled)]
    [InlineData(ExecutionOutcome.TimedOut)]
    [InlineData(ExecutionOutcome.WorkerFailed)]
    public async Task EachOutcomeKeepsItsCauseAndItsHostFacts(ExecutionOutcome outcome)
    {
        (SqliteRunHistoryRepository history, Experiment experiment) = await NewAsync();
        RunRecord record = RunSamples.Record(experiment, outcome);

        await history.AddAsync(record, TestContext.Current.CancellationToken);

        RunRecord read = (await history.GetAsync(record.Request.Id, TestContext.Current.CancellationToken))!;
        Assert.Equal(outcome, read.Report.Result.Outcome);
        Assert.Equal(record.Report.Result.Diagnostics, read.Report.Result.Diagnostics);
        Assert.Equal(record.Report.Host, read.Report.Host);
        Assert.Equal(outcome == ExecutionOutcome.Success, read.Report.Result.Value is not null);
        RunSummary summary = Assert.Single(await history.ListAsync(experiment.Id, TestContext.Current.CancellationToken));
        Assert.Equal(outcome, summary.Outcome);
    }

    [Fact]
    public async Task ListIsNewestFirstAndOnlyForTheExperiment()
    {
        (SqliteRunHistoryRepository history, Experiment experiment) = await NewAsync();
        Experiment other = RunSamples.Experiment("other");
        await new SqliteExperimentRepository(temp.Open()).SaveAsync(other, TestContext.Current.CancellationToken);
        RunRecord old = RunSamples.Record(experiment, minutes: 1);
        RunRecord recent = RunSamples.Record(experiment, ExecutionOutcome.RuntimeFailed, minutes: 5);
        await history.AddAsync(recent, TestContext.Current.CancellationToken);
        await history.AddAsync(RunSamples.Record(other, minutes: 9), TestContext.Current.CancellationToken);
        await history.AddAsync(old, TestContext.Current.CancellationToken);

        IReadOnlyList<RunSummary> list = await history.ListAsync(experiment.Id, TestContext.Current.CancellationToken);

        Assert.Equal([recent.Request.Id, old.Request.Id], list.Select(item => item.Id));
        Assert.Equal(recent.CompletedAt, list[0].CompletedAt);
        Assert.Equal(TimeSpan.FromMilliseconds(250), list[0].Duration);
    }

    [Fact]
    public async Task HistorySurvivesARestart()
    {
        (SqliteRunHistoryRepository history, Experiment experiment) = await NewAsync();
        RunRecord record = RunSamples.Record(experiment, ExecutionOutcome.TimedOut);
        await history.AddAsync(record, TestContext.Current.CancellationToken);

        SqliteRunHistoryRepository restarted = new SqliteRunHistoryRepository(temp.Open());

        RunSummary summary = Assert.Single(await restarted.ListAsync(experiment.Id, TestContext.Current.CancellationToken));
        Assert.Equal(record.Request.Id, summary.Id);
        Assert.Equal(ExecutionOutcome.TimedOut, summary.Outcome);
    }

    [Fact]
    public async Task RetentionRemovesOnlyTheOldestRunsOfThatExperiment()
    {
        (SqliteRunHistoryRepository history, Experiment experiment) = await NewAsync(retention: 3);
        Experiment other = RunSamples.Experiment("other");
        await new SqliteExperimentRepository(temp.Open()).SaveAsync(other, TestContext.Current.CancellationToken);
        RunRecord otherRun = RunSamples.Record(other, minutes: 0);
        await history.AddAsync(otherRun, TestContext.Current.CancellationToken);
        List<RunRecord> runs = [];
        for (int minute = 1; minute <= 5; minute++)
        {
            RunRecord run = RunSamples.Record(experiment, minutes: minute);
            runs.Add(run);
            await history.AddAsync(run, TestContext.Current.CancellationToken);
        }

        IReadOnlyList<RunSummary> list = await history.ListAsync(experiment.Id, TestContext.Current.CancellationToken);

        Assert.Equal(runs.Skip(2).Reverse().Select(run => run.Request.Id), list.Select(item => item.Id));
        Assert.Null(await history.GetAsync(runs[0].Request.Id, TestContext.Current.CancellationToken));
        Assert.NotNull(await history.GetAsync(otherRun.Request.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RetentionKeepsTheExperimentAndItsCurrentSource()
    {
        SqliteDatabase database = temp.Open();
        SqliteExperimentRepository experiments = new SqliteExperimentRepository(database);
        SqliteRunHistoryRepository history = new SqliteRunHistoryRepository(database, 1);
        Experiment experiment = RunSamples.Experiment("first");
        await experiments.SaveAsync(experiment, TestContext.Current.CancellationToken);
        await history.AddAsync(RunSamples.Record(experiment, minutes: 1), TestContext.Current.CancellationToken);
        Experiment edited = experiment.WithSource("edited and never run", RunSamples.Start.AddMinutes(2));
        await experiments.SaveAsync(edited, TestContext.Current.CancellationToken);

        await history.AddAsync(RunSamples.Record(edited, minutes: 3), TestContext.Current.CancellationToken);
        await history.AddAsync(RunSamples.Record(edited, minutes: 4), TestContext.Current.CancellationToken);

        Assert.Single(await history.ListAsync(experiment.Id, TestContext.Current.CancellationToken));
        Experiment? stored = await experiments.GetAsync(experiment.Id, TestContext.Current.CancellationToken);
        Assert.Equal("edited and never run", stored!.Source);
    }

    [Fact]
    public void RetentionBelowOneIsRefused()
    {
        SqliteDatabase database = temp.Open();

        Assert.Throws<ArgumentOutOfRangeException>(() => new SqliteRunHistoryRepository(database, 0));
    }

    [Fact]
    public async Task AddingTheSameExecutionAgainDoesNotChangeTheStoredRecord()
    {
        (SqliteRunHistoryRepository history, Experiment experiment) = await NewAsync();
        RunRecord record = RunSamples.Record(experiment);
        await history.AddAsync(record, TestContext.Current.CancellationToken);
        Experiment edited = experiment.WithSource("changed", RunSamples.Start.AddMinutes(1));
        ExecutionRequest changed = ExecutionRequest.Snapshot(record.Request.Id, edited, record.Request.Policy, record.Request.RequestedAt);

        ExperimentStorageException failure = await Assert.ThrowsAsync<ExperimentStorageException>(
            () => history.AddAsync(new RunRecord(changed, record.CompletedAt, RunSamples.Report(changed)), TestContext.Current.CancellationToken));

        Assert.Equal("duplicate", failure.Reason);
        Assert.Equal("1 + 1", (await history.GetAsync(record.Request.Id, TestContext.Current.CancellationToken))!.Request.Source);
    }

    [Fact]
    public async Task RunOfAnUnknownExperimentIsRefusedAndLeavesNothing()
    {
        SqliteRunHistoryRepository history = new SqliteRunHistoryRepository(temp.Open());
        Experiment unsaved = RunSamples.Experiment();

        await Assert.ThrowsAsync<ExperimentStorageException>(() => history.AddAsync(RunSamples.Record(unsaved), TestContext.Current.CancellationToken));

        Assert.Empty(await history.ListAsync(unsaved.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DamagedRowDoesNotHideTheOthers()
    {
        (SqliteRunHistoryRepository history, Experiment experiment) = await NewAsync();
        RunRecord good = RunSamples.Record(experiment, minutes: 1);
        RunRecord bad = RunSamples.Record(experiment, minutes: 2);
        await history.AddAsync(good, TestContext.Current.CancellationToken);
        await history.AddAsync(bad, TestContext.Current.CancellationToken);
        using (Microsoft.Data.Sqlite.SqliteConnection connection = temp.RawConnection())
        {
            using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE execution_history SET diagnostics_json = 'not json' WHERE id = $id;";
            command.Parameters.AddWithValue("$id", bad.Request.Id.ToString());
            command.ExecuteNonQuery();
        }

        ExperimentStorageException failure = await Assert.ThrowsAsync<ExperimentStorageException>(() => history.GetAsync(bad.Request.Id, TestContext.Current.CancellationToken));

        Assert.Equal("corrupt", failure.Reason);
        Assert.NotNull(await history.GetAsync(good.Request.Id, TestContext.Current.CancellationToken));
        Assert.Equal(2, (await history.ListAsync(experiment.Id, TestContext.Current.CancellationToken)).Count);
    }
}
