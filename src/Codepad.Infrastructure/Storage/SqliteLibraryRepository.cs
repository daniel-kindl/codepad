using System.Globalization;
using Codepad.Application.Experiments;
using Codepad.Application.Library;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Microsoft.Data.Sqlite;

namespace Codepad.Infrastructure.Storage;

/// <summary>
/// Searches the experiments table with one bounded query. A plain ASCII search uses <c>LIKE</c> and the SQLite
/// engine. A search with other letters uses a function that ignores case by Unicode rules, because <c>LIKE</c> ignores
/// case for ASCII only. The query returns one page and a short preview, never the whole source. The search only reads.
/// </summary>
/// <param name="database">The database.</param>
public sealed class SqliteLibraryRepository(SqliteDatabase database) : ILibraryRepository
{
    private const string BASE_SQL = "SELECT id, kind, title, substr(source, 1, $preview), modified_at FROM experiments";
    private const string ORDER_SQL = "ORDER BY modified_at DESC, id DESC LIMIT $limit OFFSET $offset;";
    private const string ASCII_TEXT_SQL = "(title LIKE $pattern ESCAPE '\\' OR source LIKE $pattern ESCAPE '\\' OR kind = $typed)";
    private const string UNICODE_TEXT_SQL = "(contains_ci(title, $needle) OR contains_ci(source, $needle) OR kind = $typed)";

    /// <inheritdoc />
    public Task<LibraryPage> SearchAsync(LibraryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Task.Run(
            () =>
            {
                try
                {
                    return Search(query, cancellationToken);
                }
                catch (SqliteException exception)
                {
                    throw SqliteFailure.From(exception);
                }
            },
            cancellationToken);
    }

    private static string? TypedKind(string text) => text.ToLowerInvariant() switch
    {
        "json" => nameof(OperationKind.Json),
        "c#" or "csharp" => nameof(OperationKind.CSharp),
        _ => null,
    };

    private static string EscapeLike(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private static string ParseTitle(SqliteDataReader reader) => reader.GetString(2);

    private LibraryPage Search(LibraryQuery query, CancellationToken cancellationToken)
    {
        using SqliteConnection connection = database.OpenConnection();
        connection.CreateFunction("contains_ci", (string? text, string needle) => text is not null && text.Contains(needle, StringComparison.OrdinalIgnoreCase), true);
        using SqliteCommand command = connection.CreateCommand();

        List<string> conditions = [];
        if (query.Kind is OperationKind kind)
        {
            conditions.Add("kind = $kind");
            command.Parameters.AddWithValue("$kind", kind.ToString());
        }

        if (query.Text.Length > 0)
        {
            conditions.Add(query.Text.All(char.IsAscii) ? ASCII_TEXT_SQL : UNICODE_TEXT_SQL);
            command.Parameters.AddWithValue("$pattern", "%" + EscapeLike(query.Text) + "%");
            command.Parameters.AddWithValue("$needle", query.Text);
            command.Parameters.AddWithValue("$typed", (object?)TypedKind(query.Text) ?? DBNull.Value);
        }

        command.CommandText = $"{BASE_SQL} {(conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty)} {ORDER_SQL}";
        command.Parameters.AddWithValue("$preview", LibraryQuery.PreviewLength);

        // One more row than the page tells whether another page follows.
        command.Parameters.AddWithValue("$limit", query.Limit + 1);
        command.Parameters.AddWithValue("$offset", query.Offset);

        List<ExperimentSummary> items = [];
        bool hasMore = false;
        int read = 0;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++read > query.Limit)
            {
                hasMore = true;
                break;
            }

            if (TryRead(reader) is ExperimentSummary summary)
            {
                items.Add(summary);
            }
        }

        return new LibraryPage(items, hasMore);
    }

    private static ExperimentSummary? TryRead(SqliteDataReader reader)
    {
        try
        {
            return new ExperimentSummary(
                new ExperimentId(Guid.Parse(reader.GetString(0))),
                Enum.Parse<OperationKind>(reader.GetString(1)),
                ParseTitle(reader),
                reader.GetString(3),
                DateTimeOffset.ParseExact(reader.GetString(4), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
        {
            // One damaged row must not hide the other experiments. The row stays in storage.
            return null;
        }
    }
}
