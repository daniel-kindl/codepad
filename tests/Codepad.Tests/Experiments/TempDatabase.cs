using Codepad.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace Codepad.Tests.Experiments;

/// <summary>A database file in a new temporary folder. Dispose deletes the folder. Pooling is off, so Windows can delete the file.</summary>
internal sealed class TempDatabase : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("codepad-db-tests-").FullName;

    public string Path => System.IO.Path.Combine(root, "codepad.db");

    public SqliteDatabase Open(Action<SqliteConnection>? configure = null)
    {
        SqliteDatabase database = new SqliteDatabase(Path, SqliteMigrations.All, configure);
        database.Migrate();
        return database;
    }

    public SqliteConnection RawConnection()
    {
        SqliteConnection connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
