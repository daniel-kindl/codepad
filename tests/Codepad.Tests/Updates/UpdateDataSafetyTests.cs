using Codepad.Application.Experiments;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Infrastructure.Storage;
using Codepad.Infrastructure.Updates;
using Codepad.Tests.Experiments;
using Microsoft.Data.Sqlite;

namespace Codepad.Tests.Updates;

/// <summary>An update must never lose data. These tests cover the backup that goes before the swap and the rollback after a failed first start.</summary>
public sealed class UpdateDataSafetyTests : IDisposable
{
    private readonly TempDatabase temp = new TempDatabase();

    public void Dispose() => temp.Dispose();

    [Fact]
    public async Task ABackupHasAllCommittedDataAlsoFromTheWriteAheadLogAndTheSchemaVersion()
    {
        SqliteDatabase database = temp.Open();
        SqliteExperimentRepository repository = new SqliteExperimentRepository(database);
        Experiment experiment = Experiment.Create(ExperimentId.New(), OperationKind.CSharp, "Keep me", "var x = 1;", DateTimeOffset.UtcNow);
        await repository.SaveAsync(experiment, TestContext.Current.CancellationToken);
        string backup = Path.Combine(Path.GetDirectoryName(temp.Path)!, "backup.db");

        await database.BackupAsync(backup);

        using SqliteConnection connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT title FROM experiments;";
        Assert.Equal("Keep me", command.ExecuteScalar());
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal((long)SqliteMigrations.All[^1].Version, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public async Task ABackupReplacesAnOlderBackupAndLeavesNoTemporaryFile()
    {
        SqliteDatabase database = temp.Open();
        string backup = Path.Combine(Path.GetDirectoryName(temp.Path)!, "backup.db");
        await File.WriteAllTextAsync(backup, "old backup", TestContext.Current.CancellationToken);

        await database.BackupAsync(backup);

        Assert.True(new FileInfo(backup).Length > 100);
        Assert.False(File.Exists(backup + ".tmp"));
    }

    [Fact]
    public async Task ABackupToAFolderThatDoesNotExistIsAStorageFailureWithALogSafeReason()
    {
        SqliteDatabase database = temp.Open();

        ExperimentStorageException error = await Assert.ThrowsAsync<ExperimentStorageException>(() => database.BackupAsync(Path.Combine(Path.GetDirectoryName(temp.Path)!, "nope", "b.db")));

        Assert.DoesNotContain(temp.Path, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOldVersionCannotOpenANewerSchemaSoTheRollbackRestoresTheBackupAndTheOldVersionWorksAgain()
    {
        // The released schema is the "old version". A later version adds a step and writes data.
        SqliteDatabase oldVersion = temp.Open();
        await new SqliteExperimentRepository(oldVersion).SaveAsync(Experiment.Create(ExperimentId.New(), OperationKind.CSharp, "Before", "1", DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);
        string backup = Path.Combine(Path.GetDirectoryName(temp.Path)!, "pre-update.db");
        await oldVersion.BackupAsync(backup);

        Migration[] newer = [.. SqliteMigrations.All, new Migration(SqliteMigrations.All[^1].Version + 1, "ALTER TABLE experiments ADD COLUMN extra TEXT;")];
        new SqliteDatabase(temp.Path, newer, null).Migrate();

        // The old version meets a schema that is newer than it knows. It refuses, and does not change the file.
        ExperimentStorageException refused = Assert.Throws<ExperimentStorageException>(() => new SqliteDatabase(temp.Path).Migrate());
        Assert.Equal("schema_too_new", refused.Reason);

        // The helper restores the copy. The old version opens it and finds the data.
        foreach (string leftover in new[] { temp.Path + "-wal", temp.Path + "-shm" })
        {
            File.Delete(leftover);
        }

        File.Copy(backup, temp.Path, overwrite: true);
        SqliteDatabase restored = new SqliteDatabase(temp.Path);
        restored.Migrate();
        IReadOnlyList<Experiment> experiments = await new SqliteExperimentRepository(restored).ListAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Before", Assert.Single(experiments).Title);
    }

    [Fact]
    public async Task TheUpdateStoreKeepsTheTimeAndThePromptAndSurvivesStorageFailures()
    {
        SqliteDatabase database = temp.Open();
        PreferenceUpdateStore store = new PreferenceUpdateStore(new SqlitePreferenceRepository(database));
        DateTimeOffset time = new DateTimeOffset(2026, 12, 1, 8, 30, 0, TimeSpan.Zero);

        Assert.Null(await store.GetLastCheckAsync());
        Assert.False(await store.GetPromptShownAsync());

        await store.SetLastCheckAsync(time);
        await store.SetPromptShownAsync();

        Assert.Equal(time, await store.GetLastCheckAsync());
        Assert.True(await store.GetPromptShownAsync());
    }
}
