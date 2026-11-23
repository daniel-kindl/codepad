using Codepad.Application.Experiments;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace Codepad.Tests.Experiments;

/// <summary>
/// A crash leaves the database file set as it is at that moment. These tests copy the files while a writer is in the
/// middle of a transaction, then open the copy as a new process would.
/// </summary>
public sealed class CrashRecoveryTests : IDisposable
{
    private static readonly DateTimeOffset _clock = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TempDatabase live = new TempDatabase();
    private readonly TempDatabase crashed = new TempDatabase();

    public void Dispose()
    {
        live.Dispose();
        crashed.Dispose();
    }

    // Copy with sharing, because the writer still holds the files open. The -shm file is not copied: SQLite rebuilds it.
    private void CopyLiveFilesToCrashedFolder()
    {
        foreach (string suffix in new[] { string.Empty, "-wal" })
        {
            string source = live.Path + suffix;
            if (!File.Exists(source))
            {
                continue;
            }

            using FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using FileStream output = File.Create(crashed.Path + suffix);
            input.CopyTo(output);
        }
    }

    [Fact]
    public async Task UncommittedTransactionIsGoneAndTheLastCommittedVersionIsRestored()
    {
        SqliteExperimentRepository repository = new SqliteExperimentRepository(live.Open());
        Experiment first = Experiment.Create(ExperimentId.New(), OperationKind.CSharp, "Scratch", "v1", _clock);
        Experiment second = first.WithSource("v2 committed", _clock.AddMinutes(1));
        await repository.SaveAsync(first, TestContext.Current.CancellationToken);
        await repository.SaveAsync(second, TestContext.Current.CancellationToken);

        using SqliteConnection writer = live.RawConnection();
        using SqliteCommand begin = writer.CreateCommand();
        begin.CommandText = "BEGIN; UPDATE experiments SET source = 'v3 NOT committed', revision = 3;";
        begin.ExecuteNonQuery();
        CopyLiveFilesToCrashedFolder();

        SqliteExperimentRepository restored = new SqliteExperimentRepository(crashed.Open());

        Experiment? read = await restored.GetAsync(first.Id, TestContext.Current.CancellationToken);
        Assert.Equal(second, read);
    }

    [Fact]
    public async Task CrashBetweenTransactionsRestoresTheLastCommittedVersion()
    {
        SqliteExperimentRepository repository = new SqliteExperimentRepository(live.Open());
        Experiment first = Experiment.Create(ExperimentId.New(), OperationKind.Json, "j", "{}", _clock);
        Experiment second = first.WithSource("{\"a\":1}", _clock.AddMinutes(1));
        await repository.SaveAsync(first, TestContext.Current.CancellationToken);
        await repository.SaveAsync(second, TestContext.Current.CancellationToken);

        CopyLiveFilesToCrashedFolder();

        Assert.Equal(second, await new SqliteExperimentRepository(crashed.Open()).GetAsync(first.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SessionRestartRestoresTheFlushedSourceAndNotTheUnflushedOne()
    {
        ManualTimeProvider time = new ManualTimeProvider();
        SqliteExperimentRepository repository = new SqliteExperimentRepository(live.Open());
        Experiment created = Experiment.Create(ExperimentId.New(), OperationKind.CSharp, "Scratch", "saved text", _clock);
        await repository.SaveAsync(created, TestContext.Current.CancellationToken);
        AutosaveSession session = AutosaveSession.Open(repository, created, time, TimeSpan.FromMilliseconds(500));

        session.Edit(created.WithSource("flushed text", _clock.AddSeconds(1)));
        await session.FlushAsync(TestContext.Current.CancellationToken);
        session.Edit(session.Current.WithSource("typed but never saved", _clock.AddSeconds(2)));
        CopyLiveFilesToCrashedFolder();

        Experiment? read = await new SqliteExperimentRepository(crashed.Open()).GetAsync(created.Id, TestContext.Current.CancellationToken);
        Assert.Equal("flushed text", read!.Source);
    }

    [Fact]
    public async Task ReadOnlyStorageReportsAnErrorAndTheSessionKeepsTheBuffer()
    {
        ManualTimeProvider time = new ManualTimeProvider();
        SqliteExperimentRepository repository = new SqliteExperimentRepository(live.Open(connection =>
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA query_only = ON;";
            command.ExecuteNonQuery();
        }));
        Experiment created = Experiment.Create(ExperimentId.New(), OperationKind.CSharp, "Scratch", "text", _clock);
        AutosaveSession session = AutosaveSession.StartNew(repository, created, time, TimeSpan.FromMilliseconds(500));
        Experiment typed = created.WithSource("typed while the disk is read-only", _clock.AddSeconds(1));

        session.Edit(typed);
        await session.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SaveState.Error, session.State);
        Assert.Equal("read_only", session.ErrorReason);
        Assert.Equal(typed, session.Current);
    }
}
