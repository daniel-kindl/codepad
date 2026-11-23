using Codepad.Domain.Results;

namespace Codepad.Desktop.Results;

/// <summary>One row of a result table. The table binds each column to an index.</summary>
/// <param name="Cells">The cell texts, one for each column.</param>
public sealed record ResultRow(IReadOnlyList<string> Cells)
{
    /// <summary>Gets the cell text of a column.</summary>
    /// <param name="column">The zero-based column index.</param>
    /// <returns>The text.</returns>
    public string this[int column] => column >= 0 && column < Cells.Count ? Cells[column] : string.Empty;
}

/// <summary>A table view of a homogeneous list of objects: one row for each object, one column for each property.</summary>
public sealed class ResultTable
{
    private ResultTable(IReadOnlyList<string> columns, IReadOnlyList<ResultRow> rows, TruncationReason? truncation)
    {
        Columns = columns;
        Rows = rows;
        Truncation = truncation;
    }

    /// <summary>Gets the column names.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Gets the rows.</summary>
    public IReadOnlyList<ResultRow> Rows { get; }

    /// <summary>Gets the reason that the list holds less than the full value, or null.</summary>
    public TruncationReason? Truncation { get; }

    /// <summary>Makes a table when the sequence is a finite list of objects that all have the same properties in the same order.</summary>
    /// <param name="sequence">The sequence.</param>
    /// <returns>The table, or null when the sequence is not homogeneous.</returns>
    public static ResultTable? TryCreate(SequenceNode sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        if (sequence.Items.Count == 0 || sequence.Items[0] is not ObjectNode first || first.Properties.Count == 0)
        {
            return null;
        }

        string[] columns = [.. first.Properties.Select(property => property.Name)];
        List<ResultRow> rows = new List<ResultRow>(sequence.Items.Count);
        foreach (ResultNode item in sequence.Items)
        {
            if (item is not ObjectNode obj || obj.Properties.Count != columns.Length)
            {
                return null;
            }

            string[] cells = new string[columns.Length];
            for (int index = 0; index < columns.Length; index++)
            {
                if (!string.Equals(obj.Properties[index].Name, columns[index], StringComparison.Ordinal))
                {
                    return null;
                }

                cells[index] = ResultText.Cell(obj.Properties[index].Value);
            }

            rows.Add(new ResultRow(cells));
        }

        return new ResultTable(columns, rows, sequence.Truncation);
    }
}
