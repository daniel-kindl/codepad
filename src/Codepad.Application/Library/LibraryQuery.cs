using Codepad.Domain.Operations;

namespace Codepad.Application.Library;

/// <summary>
/// A search of the library. The text matches the title and the source, ignoring case. It also matches the
/// operation kind when it is exactly <c>json</c>, <c>c#</c> or <c>csharp</c>. An empty text matches all.
/// </summary>
public sealed record LibraryQuery
{
    /// <summary>The longest source preview in characters.</summary>
    public static readonly int PreviewLength = 200;

    /// <summary>The longest page.</summary>
    public static readonly int MaxLimit = 200;

    /// <summary>The longest search text in characters. A longer text is cut.</summary>
    public static readonly int MaxTextLength = 200;

    /// <summary>The page size when the caller gives none.</summary>
    public static readonly int DefaultLimit = 50;

    /// <summary>Initializes a new instance of the <see cref="LibraryQuery"/> class.</summary>
    /// <param name="text">The search text. Null means empty.</param>
    /// <param name="kind">Shows only this kind, or all kinds when null.</param>
    /// <param name="limit">The page size, from 1 to <see cref="MaxLimit"/>.</param>
    /// <param name="offset">The number of results to skip.</param>
    public LibraryQuery(string? text = null, OperationKind? kind = null, int? limit = null, int offset = 0)
    {
        int size = limit ?? DefaultLimit;
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MaxLimit);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        string trimmed = (text ?? string.Empty).Trim();
        Text = trimmed.Length > MaxTextLength ? trimmed[..(char.IsHighSurrogate(trimmed[MaxTextLength - 1]) ? MaxTextLength - 1 : MaxTextLength)] : trimmed;
        Kind = kind;
        Limit = size;
        Offset = offset;
    }

    /// <summary>Gets the trimmed search text.</summary>
    public string Text { get; }

    /// <summary>Gets the kind filter, or null for all kinds.</summary>
    public OperationKind? Kind { get; }

    /// <summary>Gets the page size.</summary>
    public int Limit { get; }

    /// <summary>Gets the number of results to skip.</summary>
    public int Offset { get; }
}
