using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace Codepad.Domain;

/// <summary>Argument checks that the domain types share.</summary>
internal static class Guard
{
    /// <summary>Rejects an enum value that has no named member, for example <c>(OperationKind)99</c>.</summary>
    public static T Defined<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"The value is not a defined {typeof(T).Name}.");
        }

        return value;
    }

    /// <summary>Rejects a limit or count below one.</summary>
    public static int AtLeastOne(int value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, paramName);
        return value;
    }

    /// <summary>
    /// Copies the items into a new read-only list. A later change to the caller's list does not change the owner,
    /// and a reader cannot change the copy, also through a cast.
    /// </summary>
    public static ReadOnlyCollection<T> ReadOnlyCopyWithNoNulls<T>(IReadOnlyList<T> items, [CallerArgumentExpression(nameof(items))] string? paramName = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(items, paramName);
        T[] copy = [.. items];
        if (Array.IndexOf(copy, null) >= 0)
        {
            throw new ArgumentException("The list cannot contain null.", paramName);
        }

        return Array.AsReadOnly(copy);
    }
}
