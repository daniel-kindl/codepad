using Codepad.Application.Experiments;
using Microsoft.Data.Sqlite;

namespace Codepad.Infrastructure.Storage;

/// <summary>Turns a SQLite exception into a storage exception with a stable reason. The SQLite message can hold source or a path, so it is dropped.</summary>
internal static class SqliteFailure
{
    // SQLite primary result codes. See https://www.sqlite.org/rescode.html.
    private const int SQLITE_BUSY = 5;
    private const int SQLITE_LOCKED = 6;
    private const int SQLITE_READONLY = 8;
    private const int SQLITE_IOERR = 10;
    private const int SQLITE_CORRUPT = 11;
    private const int SQLITE_FULL = 13;
    private const int SQLITE_CANTOPEN = 14;
    private const int SQLITE_NOTADB = 26;

    /// <summary>Maps the exception to a storage exception.</summary>
    /// <param name="exception">The SQLite exception. The result keeps it as the inner exception.</param>
    /// <returns>A storage exception with a log-safe reason.</returns>
    public static ExperimentStorageException From(SqliteException exception)
    {
        string reason = exception.SqliteErrorCode switch
        {
            SQLITE_BUSY or SQLITE_LOCKED => "busy",
            SQLITE_READONLY => "read_only",
            SQLITE_IOERR => "io_error",
            SQLITE_CORRUPT or SQLITE_NOTADB => "corrupt",
            SQLITE_FULL => "disk_full",
            SQLITE_CANTOPEN => "cannot_open",
            _ => $"sqlite_error_{exception.SqliteErrorCode}",
        };
        return new ExperimentStorageException(reason, exception);
    }
}
