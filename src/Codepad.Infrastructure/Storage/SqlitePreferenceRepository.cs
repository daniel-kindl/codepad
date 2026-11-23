using Codepad.Application.Experiments;
using Microsoft.Data.Sqlite;

namespace Codepad.Infrastructure.Storage;

/// <summary>Stores the user preferences in the <c>settings</c> table of the CodePad database.</summary>
/// <param name="database">The prepared database.</param>
public sealed class SqlitePreferenceRepository(SqliteDatabase database) : IPreferenceRepository
{
    /// <inheritdoc />
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Task.Run(
            () =>
            {
                try
                {
                    using SqliteConnection connection = database.OpenConnection();
                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText = "SELECT value FROM settings WHERE key = $key;";
                    command.Parameters.AddWithValue("$key", key);
                    return command.ExecuteScalar() as string;
                }
                catch (SqliteException exception)
                {
                    throw SqliteFailure.From(exception);
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        return Task.Run(
            () =>
            {
                try
                {
                    using SqliteConnection connection = database.OpenConnection();
                    using SqliteTransaction transaction = connection.BeginTransaction();
                    using SqliteCommand command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = "INSERT INTO settings (key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value;";
                    command.Parameters.AddWithValue("$key", key);
                    command.Parameters.AddWithValue("$value", value);
                    command.ExecuteNonQuery();
                    transaction.Commit();
                }
                catch (SqliteException exception)
                {
                    throw SqliteFailure.From(exception);
                }
            },
            cancellationToken);
    }
}
