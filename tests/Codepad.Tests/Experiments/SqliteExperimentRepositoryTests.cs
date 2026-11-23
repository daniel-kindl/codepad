using Codepad.Application.Experiments;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace Codepad.Tests.Experiments;

public sealed class SqliteExperimentRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset _clock = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(2)).AddTicks(1234);

    private readonly TempDatabase temp = new TempDatabase();

    public void Dispose() => temp.Dispose();

    private SqliteExperimentRepository NewRepository() => new SqliteExperimentRepository(temp.Open());

    private static Experiment Sample(string source = "Console.WriteLine(1);") => Experiment.Create(ExperimentId.New(), OperationKind.CSharp, "Scratch", source, _clock);

    [Fact]
    public async Task SavedExperimentReadsBackWithEveryField()
    {
        SqliteExperimentRepository repository = NewRepository();
        Experiment experiment = Sample().WithInput("stdin text", _clock.AddMinutes(1));

        SaveResult result = await repository.SaveAsync(experiment, TestContext.Current.CancellationToken);

        Assert.Equal(SaveResult.Committed, result);
        Experiment? read = await repository.GetAsync(experiment.Id, TestContext.Current.CancellationToken);
        Assert.Equal(experiment, read);
        Assert.Equal(experiment.ModifiedAt, read!.ModifiedAt);
    }

    [Fact]
    public async Task JsonKindAndAwkwardTextSurviveTheRoundTrip()
    {
        SqliteExperimentRepository repository = NewRepository();
        string source = "{\"a\":\"\U0001F600 é\"}\r\n\tline2\0end";
        Experiment experiment = Experiment.Create(ExperimentId.New(), OperationKind.Json, "título", source, _clock);

        await repository.SaveAsync(experiment, TestContext.Current.CancellationToken);

        Assert.Equal(experiment, await repository.GetAsync(experiment.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetOfAnUnknownIdReturnsNull()
    {
        SqliteExperimentRepository repository = NewRepository();

        Assert.Null(await repository.GetAsync(ExperimentId.New(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NewerRevisionReplacesTheStoredOne()
    {
        SqliteExperimentRepository repository = NewRepository();
        Experiment first = Sample();
        Experiment renamed = first.WithTitle("Renamed", _clock.AddMinutes(1));
        await repository.SaveAsync(first, TestContext.Current.CancellationToken);

        SaveResult result = await repository.SaveAsync(renamed, TestContext.Current.CancellationToken);

        Assert.Equal(SaveResult.Committed, result);
        Assert.Equal(renamed, await repository.GetAsync(first.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OlderWriteArrivingAfterANewerOneDoesNotOverwriteIt()
    {
        SqliteExperimentRepository repository = NewRepository();
        Experiment old = Sample("old");
        Experiment current = old.WithSource("new", _clock.AddMinutes(1));

        SaveResult newFirst = await repository.SaveAsync(current, TestContext.Current.CancellationToken);
        SaveResult oldSecond = await repository.SaveAsync(old, TestContext.Current.CancellationToken);

        Assert.Equal(SaveResult.Committed, newFirst);
        Assert.Equal(SaveResult.Stale, oldSecond);
        Assert.Equal("new", (await repository.GetAsync(old.Id, TestContext.Current.CancellationToken))!.Source);
    }

    [Fact]
    public async Task SameRevisionIsStaleAndChangesNothing()
    {
        SqliteExperimentRepository repository = NewRepository();
        Experiment experiment = Sample();
        await repository.SaveAsync(experiment, TestContext.Current.CancellationToken);

        SaveResult result = await repository.SaveAsync(experiment, TestContext.Current.CancellationToken);

        Assert.Equal(SaveResult.Stale, result);
    }

    [Fact]
    public async Task TwoQueuedWritesWithOldAndNewRevisionsKeepTheNewSource()
    {
        SqliteExperimentRepository repository = NewRepository();
        Experiment old = Sample("old");
        Experiment current = old.WithSource("new", _clock.AddMinutes(1));
        await repository.SaveAsync(old, TestContext.Current.CancellationToken);

        await Task.WhenAll(repository.SaveAsync(current, TestContext.Current.CancellationToken), repository.SaveAsync(old, TestContext.Current.CancellationToken));

        Assert.Equal("new", (await repository.GetAsync(old.Id, TestContext.Current.CancellationToken))!.Source);
    }

    [Fact]
    public async Task ListReturnsTheMostRecentlyModifiedFirst()
    {
        SqliteExperimentRepository repository = NewRepository();
        Experiment oldest = Experiment.Create(ExperimentId.New(), OperationKind.Json, "oldest", "{}", _clock);
        Experiment newest = Experiment.Create(ExperimentId.New(), OperationKind.Json, "newest", "{}", _clock.AddHours(1));
        Experiment middle = Experiment.Create(ExperimentId.New(), OperationKind.Json, "middle", "{}", _clock.AddMinutes(30));
        foreach (Experiment experiment in new[] { oldest, newest, middle })
        {
            await repository.SaveAsync(experiment, TestContext.Current.CancellationToken);
        }

        IReadOnlyList<Experiment> list = await repository.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["newest", "middle", "oldest"], list.Select(experiment => experiment.Title));
    }

    [Fact]
    public async Task CreateEditRenameAndReopenAfterRestart()
    {
        Experiment created = Sample();
        Experiment edited = created.WithSource("edited", _clock.AddMinutes(1));
        Experiment renamed = edited.WithTitle("Renamed", _clock.AddMinutes(2));
        SqliteExperimentRepository firstRun = NewRepository();
        await firstRun.SaveAsync(created, TestContext.Current.CancellationToken);
        await firstRun.SaveAsync(edited, TestContext.Current.CancellationToken);
        await firstRun.SaveAsync(renamed, TestContext.Current.CancellationToken);

        // A new database object on the same file stands for a new process.
        SqliteExperimentRepository secondRun = NewRepository();

        Assert.Equal(renamed, await secondRun.GetAsync(created.Id, TestContext.Current.CancellationToken));
        Assert.Equal([renamed], await secondRun.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FailedWriteLeavesTheStoredRowUnchangedAndReportsALogSafeReason()
    {
        SqliteDatabase database = temp.Open();
        SqliteExperimentRepository repository = new SqliteExperimentRepository(database);
        Experiment first = Sample("first");
        await repository.SaveAsync(first, TestContext.Current.CancellationToken);
        using (SqliteConnection raw = temp.RawConnection())
        {
            using SqliteCommand trigger = raw.CreateCommand();
            trigger.CommandText = "CREATE TRIGGER fail_update BEFORE UPDATE ON experiments BEGIN SELECT RAISE(ABORT, 'secret source text'); END;";
            trigger.ExecuteNonQuery();
        }

        ExperimentStorageException error = await Assert.ThrowsAsync<ExperimentStorageException>(() => repository.SaveAsync(first.WithSource("second", _clock.AddMinutes(1)), TestContext.Current.CancellationToken));

        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(temp.Path, error.Message, StringComparison.Ordinal);
        Assert.Equal(first, await repository.GetAsync(first.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadOnlyStorageReportsReadOnly()
    {
        SqliteExperimentRepository repository = new SqliteExperimentRepository(temp.Open(connection => Execute(connection, "PRAGMA query_only = ON;")));

        ExperimentStorageException error = await Assert.ThrowsAsync<ExperimentStorageException>(() => repository.SaveAsync(Sample(), TestContext.Current.CancellationToken));

        Assert.Equal("read_only", error.Reason);
    }

    [Fact]
    public async Task FullStorageReportsDiskFullAndKeepsEarlierData()
    {
        SqliteDatabase small = temp.Open();
        Experiment kept = Sample("kept");
        await new SqliteExperimentRepository(small).SaveAsync(kept, TestContext.Current.CancellationToken);
        int pages;
        using (SqliteConnection raw = temp.RawConnection())
        {
            using SqliteCommand count = raw.CreateCommand();
            count.CommandText = "PRAGMA page_count;";
            pages = Convert.ToInt32(count.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }

        SqliteExperimentRepository limited = new SqliteExperimentRepository(temp.Open(connection => Execute(connection, $"PRAGMA max_page_count = {pages};")));
        ExperimentStorageException error = await Assert.ThrowsAsync<ExperimentStorageException>(() => limited.SaveAsync(Sample(new string('x', 200_000)), TestContext.Current.CancellationToken));

        Assert.Equal("disk_full", error.Reason);
        Assert.Equal(kept, await limited.GetAsync(kept.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LockedDatabaseReportsBusyAfterTheWaitAndKeepsEarlierData()
    {
        SqliteExperimentRepository repository = new SqliteExperimentRepository(temp.Open(connection => connection.DefaultTimeout = 1));
        Experiment kept = Sample("kept");
        await repository.SaveAsync(kept, TestContext.Current.CancellationToken);
        using SqliteConnection locker = temp.RawConnection();
        Execute(locker, "BEGIN IMMEDIATE;");

        ExperimentStorageException error = await Assert.ThrowsAsync<ExperimentStorageException>(() => repository.SaveAsync(Sample("other"), TestContext.Current.CancellationToken));

        Assert.Equal("busy", error.Reason);
        Execute(locker, "ROLLBACK;");
        Assert.Equal(kept, await repository.GetAsync(kept.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OneDamagedRowDoesNotHideTheOtherExperimentsFromTheList()
    {
        SqliteExperimentRepository repository = NewRepository();
        Experiment good = Sample("good");
        await repository.SaveAsync(good, TestContext.Current.CancellationToken);
        Guid damaged = Guid.NewGuid();
        using (SqliteConnection raw = temp.RawConnection())
        {
            Execute(raw, $"INSERT INTO experiments VALUES ('{damaged}', 'Json', 't', '{{}}', '', '2026-01-02T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00', 1);");
        }

        IReadOnlyList<Experiment> list = await repository.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([good], list);
        using (SqliteConnection raw = temp.RawConnection())
        {
            Execute(raw, $"INSERT INTO experiments VALUES ('{Guid.NewGuid()}', '99', 't', '{{}}', '', '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00', 1);");
        }

        Assert.Equal([good], await repository.ListAsync(TestContext.Current.CancellationToken));
        ExperimentStorageException error = await Assert.ThrowsAsync<ExperimentStorageException>(() => repository.GetAsync(new ExperimentId(damaged), TestContext.Current.CancellationToken));
        Assert.Equal("corrupt", error.Reason);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
