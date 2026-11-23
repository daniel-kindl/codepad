using System.Globalization;
using System.Text.Json;
using Codepad.Application.Execution;
using Codepad.Application.Experiments;
using Codepad.Application.History;
using Codepad.Contracts;
using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Domain.Results;
using Microsoft.Data.Sqlite;

namespace Codepad.Infrastructure.Storage;

/// <summary>
/// Stores run history in SQLite. A record is one insert. The insert and the removal of the oldest runs are one
/// transaction, so a crash leaves either both or neither. Nothing here changes a stored record or an experiment.
/// </summary>
public sealed class SqliteRunHistoryRepository : IRunHistoryRepository
{
    /// <summary>The number of runs that history keeps for each experiment unless the caller chooses another number.</summary>
    public static readonly int DefaultRetention = 100;

    private const string TIME_FORMAT = "O";

    private const string INSERT_SQL = """
        INSERT INTO execution_history (
            id, experiment_id, experiment_revision, requested_at, completed_at, outcome, policy_json, source, input,
            source_revision, diagnostics_json, value_json, stdout, stdout_original_len, stderr, stderr_original_len, host_json)
        VALUES (
            $id, $experiment_id, $experiment_revision, $requested_at, $completed_at, $outcome, $policy_json, $source, $input,
            $source_revision, $diagnostics_json, $value_json, $stdout, $stdout_original_len, $stderr, $stderr_original_len, $host_json);
        """;

    // Keeps the newest runs of one experiment and removes the rest. The ID breaks a tie in the time (IDs sort by creation time).
    private const string TRIM_SQL = """
        DELETE FROM execution_history
        WHERE experiment_id = $experiment_id
          AND id NOT IN (
              SELECT id FROM execution_history
              WHERE experiment_id = $experiment_id
              ORDER BY completed_at DESC, id DESC
              LIMIT $retention);
        """;

    private static readonly JsonSerializerOptions s_json = JsonSerializerOptions.Web;

    private readonly SqliteDatabase database;
    private readonly int retention;

    /// <summary>Initializes a new instance of the <see cref="SqliteRunHistoryRepository"/> class.</summary>
    /// <param name="database">The prepared database.</param>
    /// <param name="retention">The number of runs to keep for each experiment. It must be at least 1. Null keeps <see cref="DefaultRetention"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retention"/> is less than 1.</exception>
    public SqliteRunHistoryRepository(SqliteDatabase database, int? retention = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        int keep = retention ?? DefaultRetention;
        ArgumentOutOfRangeException.ThrowIfLessThan(keep, 1);
        this.database = database;
        this.retention = keep;
    }

    /// <inheritdoc />
    public Task AddAsync(RunRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return RunAsync(
            () =>
            {
                ExecutionRequest request = record.Request;
                ExecutionResult result = record.Report.Result;
                using SqliteConnection connection = database.OpenConnection();
                using SqliteTransaction transaction = connection.BeginTransaction();
                using (SqliteCommand insert = connection.CreateCommand())
                {
                    insert.Transaction = transaction;
                    insert.CommandText = INSERT_SQL;
                    insert.Parameters.AddWithValue("$id", request.Id.Value.ToString());
                    insert.Parameters.AddWithValue("$experiment_id", request.ExperimentId.Value.ToString());
                    insert.Parameters.AddWithValue("$experiment_revision", request.ExperimentRevision);
                    insert.Parameters.AddWithValue("$requested_at", FormatTime(request.RequestedAt));
                    insert.Parameters.AddWithValue("$completed_at", FormatTime(record.CompletedAt));
                    insert.Parameters.AddWithValue("$outcome", result.Outcome.ToString());
                    insert.Parameters.AddWithValue("$policy_json", JsonSerializer.Serialize(PolicyData.From(request.Policy), s_json));
                    insert.Parameters.AddWithValue("$source", request.Source);
                    insert.Parameters.AddWithValue("$input", request.Input);
                    insert.Parameters.AddWithValue("$source_revision", record.Report.SourceRevision);
                    insert.Parameters.AddWithValue("$diagnostics_json", JsonSerializer.Serialize(result.Diagnostics, s_json));
                    insert.Parameters.AddWithValue("$value_json", result.Value is null ? DBNull.Value : JsonSerializer.Serialize(result.Value, s_json));
                    insert.Parameters.AddWithValue("$stdout", record.Report.Stdout.Text);
                    insert.Parameters.AddWithValue("$stdout_original_len", (object?)record.Report.Stdout.OriginalLength ?? DBNull.Value);
                    insert.Parameters.AddWithValue("$stderr", record.Report.Stderr.Text);
                    insert.Parameters.AddWithValue("$stderr_original_len", (object?)record.Report.Stderr.OriginalLength ?? DBNull.Value);
                    insert.Parameters.AddWithValue("$host_json", record.Report.Host is null ? DBNull.Value : JsonSerializer.Serialize(record.Report.Host, s_json));
                    insert.ExecuteNonQuery();
                }

                using (SqliteCommand trim = connection.CreateCommand())
                {
                    trim.Transaction = transaction;
                    trim.CommandText = TRIM_SQL;
                    trim.Parameters.AddWithValue("$experiment_id", request.ExperimentId.Value.ToString());
                    trim.Parameters.AddWithValue("$retention", retention);
                    trim.ExecuteNonQuery();
                }

                transaction.Commit();
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RunSummary>> ListAsync(ExperimentId experimentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experimentId);
        return RunAsync<IReadOnlyList<RunSummary>>(
            () =>
            {
                using SqliteConnection connection = database.OpenConnection();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT id, experiment_id, experiment_revision, requested_at, completed_at, outcome
                    FROM execution_history WHERE experiment_id = $experiment_id
                    ORDER BY completed_at DESC, id DESC;
                    """;
                command.Parameters.AddWithValue("$experiment_id", experimentId.Value.ToString());
                using SqliteDataReader reader = command.ExecuteReader();
                List<RunSummary> list = [];
                while (reader.Read())
                {
                    try
                    {
                        list.Add(new RunSummary(
                            new ExecutionId(Guid.Parse(reader.GetString(0))),
                            new ExperimentId(Guid.Parse(reader.GetString(1))),
                            reader.GetInt64(2),
                            ParseTime(reader.GetString(3)),
                            ParseTime(reader.GetString(4)),
                            Enum.Parse<ExecutionOutcome>(reader.GetString(5))));
                    }
                    catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
                    {
                        // One damaged row must not hide the other runs. GetAsync still reports the row.
                    }
                }

                return list;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RunRecord?> GetAsync(ExecutionId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        return RunAsync(
            () =>
            {
                using SqliteConnection connection = database.OpenConnection();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT id, experiment_id, experiment_revision, requested_at, completed_at, outcome, policy_json, source, input,
                           source_revision, diagnostics_json, value_json, stdout, stdout_original_len, stderr, stderr_original_len, host_json
                    FROM execution_history WHERE id = $id;
                    """;
                command.Parameters.AddWithValue("$id", id.Value.ToString());
                using SqliteDataReader reader = command.ExecuteReader();
                return reader.Read() ? Read(reader) : null;
            },
            cancellationToken);
    }

    private static string FormatTime(DateTimeOffset time) => time.ToUniversalTime().ToString(TIME_FORMAT, CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string text) => DateTimeOffset.ParseExact(text, TIME_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static RunRecord Read(SqliteDataReader reader)
    {
        try
        {
            ExecutionId id = new ExecutionId(Guid.Parse(reader.GetString(0)));
            ExperimentId experimentId = new ExperimentId(Guid.Parse(reader.GetString(1)));
            DateTimeOffset requestedAt = ParseTime(reader.GetString(3));
            OperationPolicy policy = JsonSerializer.Deserialize<PolicyData>(reader.GetString(6), s_json)!.ToPolicy();

            // A request can only be made from an experiment. The stored values are exactly what the snapshot had.
            Experiment snapshot = Experiment.Restore(experimentId, policy.Kind, "history", reader.GetString(7), reader.GetString(8), requestedAt, requestedAt, reader.GetInt64(2));
            ExecutionRequest request = ExecutionRequest.Snapshot(id, snapshot, policy, requestedAt);

            ExecutionOutcome outcome = Enum.Parse<ExecutionOutcome>(reader.GetString(5));
            Diagnostic[] diagnostics = JsonSerializer.Deserialize<Diagnostic[]>(reader.GetString(10), s_json)!;
            ResultNode? value = reader.IsDBNull(11) ? null : JsonSerializer.Deserialize<ResultNode>(reader.GetString(11), s_json);
            WorkerHostInfo? host = reader.IsDBNull(16) ? null : JsonSerializer.Deserialize<WorkerHostInfo>(reader.GetString(16), s_json);
            RunReport report = new RunReport(
                id,
                reader.GetInt64(9),
                new ExecutionResult(outcome, diagnostics, value),
                new CapturedText(reader.GetString(12), reader.IsDBNull(13) ? null : reader.GetInt32(13)),
                new CapturedText(reader.GetString(14), reader.IsDBNull(15) ? null : reader.GetInt32(15)),
                host);
            return new RunRecord(request, ParseTime(reader.GetString(4)), report);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException or JsonException or NotSupportedException or InvalidOperationException or NullReferenceException)
        {
            // The message can hold stored text, so it stays out of the reason.
            throw new ExperimentStorageException("corrupt", exception);
        }
    }

    // The work is short and local. Running it on the thread pool keeps the caller free during a slow fsync.
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

    private sealed record PolicyData(string Kind, int Version, string Trigger, double TimeLimitSeconds, int MaxDepth, int MaxItems, int MaxTextLength, int MaxNodes, bool InvokePropertyGetters)
    {
        public static PolicyData From(OperationPolicy policy) => new PolicyData(
            policy.Kind.ToString(),
            policy.Version,
            policy.Trigger.ToString(),
            policy.TimeLimit.TotalSeconds,
            policy.Results.MaxDepth,
            policy.Results.MaxItems,
            policy.Results.MaxTextLength,
            policy.Results.MaxNodes,
            policy.Results.InvokePropertyGetters);

        public OperationPolicy ToPolicy() => new OperationPolicy(
            Enum.Parse<OperationKind>(Kind),
            Version,
            Enum.Parse<OperationTrigger>(Trigger),
            TimeSpan.FromSeconds(TimeLimitSeconds),
            new ResultPolicy(MaxDepth, MaxItems, MaxTextLength, MaxNodes, InvokePropertyGetters));
    }
}
