using System.Globalization;
using Codepad.Application.Experiments;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Microsoft.Data.Sqlite;

namespace Codepad.Infrastructure.Storage;

/// <summary>
/// Stores experiments in SQLite. Each write is one statement in one transaction, and a lower or equal revision never replaces a higher one.
/// Reading does not run the source.
/// </summary>
/// <param name="database">The prepared database.</param>
public sealed class SqliteExperimentRepository(SqliteDatabase database) : IExperimentRepository
{
    private const string TIME_FORMAT = "O";

    // One statement decides the revision check and the write together, so two writers cannot interleave between a check and a write.
    private const string UPSERT_SQL = """
        INSERT INTO experiments (id, kind, title, source, input, created_at, modified_at, revision)
        VALUES ($id, $kind, $title, $source, $input, $created_at, $modified_at, $revision)
        ON CONFLICT (id) DO UPDATE SET
            title = excluded.title,
            source = excluded.source,
            input = excluded.input,
            modified_at = excluded.modified_at,
            revision = excluded.revision
        WHERE excluded.revision > experiments.revision;
        """;

    private const string SELECT_SQL = "SELECT id, kind, title, source, input, created_at, modified_at, revision FROM experiments";

    /// <inheritdoc />
    public Task<SaveResult> SaveAsync(Experiment experiment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        return RunAsync(
            () =>
            {
                using SqliteConnection connection = database.OpenConnection();
                using SqliteTransaction transaction = connection.BeginTransaction();
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = UPSERT_SQL;
                command.Parameters.AddWithValue("$id", experiment.Id.Value.ToString());
                command.Parameters.AddWithValue("$kind", experiment.Kind.ToString());
                command.Parameters.AddWithValue("$title", experiment.Title);
                command.Parameters.AddWithValue("$source", experiment.Source);
                command.Parameters.AddWithValue("$input", experiment.Input);
                command.Parameters.AddWithValue("$created_at", FormatTime(experiment.CreatedAt));
                command.Parameters.AddWithValue("$modified_at", FormatTime(experiment.ModifiedAt));
                command.Parameters.AddWithValue("$revision", experiment.Revision);
                int changed = command.ExecuteNonQuery();
                transaction.Commit();
                return changed == 1 ? SaveResult.Committed : SaveResult.Stale;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Experiment?> GetAsync(ExperimentId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        return RunAsync(
            () =>
            {
                using SqliteConnection connection = database.OpenConnection();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"{SELECT_SQL} WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id.Value.ToString());
                using SqliteDataReader reader = command.ExecuteReader();
                return reader.Read() ? Read(reader) : null;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Experiment>> ListAsync(CancellationToken cancellationToken = default) => RunAsync<IReadOnlyList<Experiment>>(
        () =>
        {
            using SqliteConnection connection = database.OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"{SELECT_SQL} ORDER BY modified_at DESC, id DESC;";
            using SqliteDataReader reader = command.ExecuteReader();
            List<Experiment> list = [];
            while (reader.Read())
            {
                list.Add(Read(reader));
            }

            return list;
        },
        cancellationToken);

    private static string FormatTime(DateTimeOffset time) => time.ToUniversalTime().ToString(TIME_FORMAT, CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string text) => DateTimeOffset.ParseExact(text, TIME_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static Experiment Read(SqliteDataReader reader)
    {
        try
        {
            return Experiment.Restore(
                new ExperimentId(Guid.Parse(reader.GetString(0))),
                Enum.Parse<OperationKind>(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                ParseTime(reader.GetString(5)),
                ParseTime(reader.GetString(6)),
                reader.GetInt64(7));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
        {
            // A row that the domain rejects is damaged data. The message can hold the stored text, so it stays out of the reason.
            throw new ExperimentStorageException("corrupt", exception);
        }
    }

    // The work is short and local. Running it on the thread pool keeps the caller (the UI or a timer) free during a slow fsync.
    private static Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            try
            {
                return work();
            }
            catch (SqliteException exception)
            {
                throw SqliteFailure.From(exception);
            }
        },
        cancellationToken);
}
