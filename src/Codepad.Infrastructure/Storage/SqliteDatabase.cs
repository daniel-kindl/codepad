using Codepad.Application.Experiments;
using Codepad.Application.Startup;
using Microsoft.Data.Sqlite;

namespace Codepad.Infrastructure.Storage;

/// <summary>
/// The CodePad SQLite database file. It creates the file, applies the migrations and refuses a schema that is newer than the app.
/// The database uses write-ahead logging and full synchronization: a commit is durable, and a crash restores the last commit.
/// </summary>
public sealed class SqliteDatabase : IDataStoreInitializer
{
    // How long a write waits for another writer before it reports "busy". The provider turns this into the SQLite busy timeout.
    private const int BUSY_WAIT_SECONDS = 5;

    private readonly string path;
    private readonly IReadOnlyList<Migration> migrations;
    private readonly Action<SqliteConnection>? configure;

    /// <summary>Initializes a new instance of the <see cref="SqliteDatabase"/> class for the current schema.</summary>
    /// <param name="path">The full path of the database file. The folder must exist.</param>
    public SqliteDatabase(string path)
        : this(path, SqliteMigrations.All, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SqliteDatabase"/> class. Tests use it to supply their own steps and connection setup.</summary>
    /// <param name="path">The full path of the database file.</param>
    /// <param name="migrations">The schema steps, in order.</param>
    /// <param name="configure">Runs on each connection that a repository opens. A test uses it to simulate a read-only or a full disk.</param>
    internal SqliteDatabase(string path, IReadOnlyList<Migration> migrations, Action<SqliteConnection>? configure)
    {
        this.path = path;
        this.migrations = migrations;
        this.configure = configure;
    }

    /// <inheritdoc />
    public DataStoreStatus Initialize()
    {
        try
        {
            Migrate();
            return DataStoreStatus.Ready;
        }
        catch (ExperimentStorageException exception)
        {
            return DataStoreStatus.Unavailable(exception.Reason);
        }
    }

    /// <summary>Creates the file if needed and applies each migration that the file has not seen. Each step is one transaction.</summary>
    /// <exception cref="ExperimentStorageException">
    /// The file is not a database (<c>corrupt</c>), its schema is newer than this app (<c>schema_too_new</c>),
    /// a step failed (<c>migration_failed</c>), or storage failed.
    /// </exception>
    public void Migrate()
    {
        try
        {
            using SqliteConnection connection = Open(applyConfigure: false);
            Execute(connection, "PRAGMA journal_mode = WAL;");

            long version = Scalar(connection, "PRAGMA user_version;");
            int latest = migrations.Count == 0 ? 0 : migrations[^1].Version;
            if (version > latest)
            {
                throw new ExperimentStorageException("schema_too_new");
            }

            foreach (Migration step in migrations.Where(step => step.Version > version))
            {
                Apply(connection, step);
            }
        }
        catch (SqliteException exception)
        {
            throw SqliteFailure.From(exception);
        }
    }

    /// <summary>Opens a connection for a repository. The caller disposes it. Pooling is off, so a disposed connection releases the file.</summary>
    /// <returns>An open connection.</returns>
    internal SqliteConnection OpenConnection() => Open(applyConfigure: true);

    private static void Apply(SqliteConnection connection, Migration step)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        try
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = step.Sql;
            command.ExecuteNonQuery();
            command.CommandText = $"PRAGMA user_version = {step.Version};";
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        catch (SqliteException exception)
        {
            transaction.Rollback();
            ExperimentStorageException failure = SqliteFailure.From(exception);
            throw failure.Reason.StartsWith("sqlite_error_", StringComparison.Ordinal)
                ? new ExperimentStorageException("migration_failed", exception)
                : failure;
        }
    }

    private SqliteConnection Open(bool applyConfigure)
    {
        SqliteConnection connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = BUSY_WAIT_SECONDS,
        }.ToString());
        try
        {
            connection.Open();
            Execute(connection, "PRAGMA synchronous = FULL; PRAGMA foreign_keys = ON;");
            if (applyConfigure)
            {
                configure?.Invoke(connection);
            }

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
