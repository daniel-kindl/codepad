using Codepad.Application.Experiments;
using Codepad.Application.Startup;
using Codepad.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace Codepad.Tests.Experiments;

public sealed class SqliteMigrationTests : IDisposable
{
    private readonly TempDatabase temp = new TempDatabase();

    public void Dispose() => temp.Dispose();

    private long Scalar(string sql)
    {
        using SqliteConnection connection = temp.RawConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private string Text(string sql)
    {
        using SqliteConnection connection = temp.RawConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    [Fact]
    public void NewDatabaseGetsTheLatestSchemaVersionAndAllTables()
    {
        temp.Open();

        Assert.Equal(SqliteMigrations.All[^1].Version, Scalar("PRAGMA user_version;"));
        foreach (string table in new[] { "experiments", "settings", "execution_history" })
        {
            Assert.Equal(1, Scalar($"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}';"));
        }

        // Step 3 removed the reserved table. The session is a setting.
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'tab_session';"));
    }

    [Fact]
    public void DatabaseUsesWriteAheadLoggingWithFullSynchronous()
    {
        temp.Open();

        Assert.Equal("wal", Text("PRAGMA journal_mode;"));
        SqliteDatabase database = new SqliteDatabase(temp.Path, SqliteMigrations.All, null);
        using SqliteConnection connection = database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA synchronous;";
        Assert.Equal(2L, Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void OpeningAgainKeepsTheDataAndTheVersion()
    {
        temp.Open();
        using (SqliteConnection connection = temp.RawConnection())
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO settings(key, value) VALUES ('k', 'v');";
            insert.ExecuteNonQuery();
        }

        temp.Open();

        Assert.Equal("v", Text("SELECT value FROM settings WHERE key = 'k';"));
    }

    [Fact]
    public void NewerSchemaIsRefusedAndLeftUntouched()
    {
        using (SqliteConnection connection = temp.RawConnection())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 999;";
            command.ExecuteNonQuery();
        }

        ExperimentStorageException error = Assert.Throws<ExperimentStorageException>(() => temp.Open());

        Assert.Equal("schema_too_new", error.Reason);
        Assert.Equal(999, Scalar("PRAGMA user_version;"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM sqlite_master;"));
        Assert.Equal("delete", Text("PRAGMA journal_mode;"));
    }

    [Fact]
    public async Task EightProcessesStartingTogetherOnANewFolderAllSucceed()
    {
        using Barrier start = new Barrier(8);
        Task[] starts = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            SqliteDatabase database = new SqliteDatabase(temp.Path, SqliteMigrations.All, null);
            start.SignalAndWait();
            database.Migrate();
        })).ToArray();

        await Task.WhenAll(starts);

        Assert.Equal(SqliteMigrations.All[^1].Version, Scalar("PRAGMA user_version;"));
    }

    [Fact]
    public void InitializeDoesNotThrowForAnExceptionThatIsNotAStorageFailure()
    {
        // A broken list stands for any failure that is not a storage failure, for example a missing native library.
        SqliteDatabase database = new SqliteDatabase(temp.Path, [new Migration(1, "SELECT 1;")], null);
        System.Reflection.FieldInfo field = typeof(SqliteDatabase).GetField("migrations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        field.SetValue(database, null);

        DataStoreStatus status = database.Initialize();

        Assert.False(status.IsReady);
        Assert.Equal(nameof(NullReferenceException), status.Reason);
    }

    [Fact]
    public void FailingFirstMigrationLeavesNoPartialSchema()
    {
        Migration[] migrations = [new Migration(1, "CREATE TABLE a(x INTEGER); CREATE TABLE a(x INTEGER);")];

        ExperimentStorageException error = Assert.Throws<ExperimentStorageException>(() => new SqliteDatabase(temp.Path, migrations, null).Migrate());

        Assert.Equal("migration_failed", error.Reason);
        Assert.Equal(0, Scalar("PRAGMA user_version;"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name = 'a';"));
    }

    [Fact]
    public void FailingLaterMigrationKeepsTheEarlierVersionAndItsData()
    {
        Migration first = new Migration(1, "CREATE TABLE a(x INTEGER); INSERT INTO a VALUES (7);");
        new SqliteDatabase(temp.Path, [first], null).Migrate();
        Migration[] migrations = [first, new Migration(2, "ALTER TABLE a ADD COLUMN y INTEGER; CREATE TABLE b(x); CREATE TABLE b(x);")];

        Assert.Throws<ExperimentStorageException>(() => new SqliteDatabase(temp.Path, migrations, null).Migrate());

        Assert.Equal(1, Scalar("PRAGMA user_version;"));
        Assert.Equal(7, Scalar("SELECT x FROM a;"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM pragma_table_info('a') WHERE name = 'y';"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name = 'b';"));
    }

    [Fact]
    public void LaterMigrationKeepsExistingData()
    {
        Migration first = new Migration(1, "CREATE TABLE a(x INTEGER); INSERT INTO a VALUES (7);");
        new SqliteDatabase(temp.Path, [first], null).Migrate();

        new SqliteDatabase(temp.Path, [first, new Migration(2, "ALTER TABLE a ADD COLUMN y INTEGER DEFAULT 3;")], null).Migrate();

        Assert.Equal(2, Scalar("PRAGMA user_version;"));
        Assert.Equal(10, Scalar("SELECT x + y FROM a;"));
    }

    [Fact]
    public void FileThatIsNotADatabaseIsReportedAsCorrupt()
    {
        File.WriteAllText(temp.Path, new string('z', 4096));

        ExperimentStorageException error = Assert.Throws<ExperimentStorageException>(() => temp.Open());

        Assert.Equal("corrupt", error.Reason);
    }

    [Fact]
    public void InitializeReturnsReadyForAGoodDatabase()
    {
        SqliteDatabase database = new SqliteDatabase(temp.Path, SqliteMigrations.All, null);

        Assert.Equal(DataStoreStatus.Ready, database.Initialize());
    }

    [Fact]
    public void InitializeReturnsAReasonWithoutThrowingForAnUnusableDatabase()
    {
        File.WriteAllText(temp.Path, new string('z', 4096));
        SqliteDatabase database = new SqliteDatabase(temp.Path, SqliteMigrations.All, null);

        DataStoreStatus status = database.Initialize();

        Assert.False(status.IsReady);
        Assert.Equal("corrupt", status.Reason);
    }
}
