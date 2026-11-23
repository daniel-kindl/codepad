using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Codepad.Domain.Operations;
using Codepad.Domain.Results;

namespace Codepad.Worker;

/// <summary>
/// Converts a .NET value to a bounded <see cref="ResultNode"/> tree. The tree always respects the
/// <see cref="ResultPolicy"/>: <see cref="ResultPolicy.FindExceededLimit"/> finds no limit that it breaks.
/// A value that is too deep, too wide or too long becomes a marked cut. A cycle becomes a <see cref="TruncatedNode"/>.
/// The converter calls property getters only when the policy allows it. With getters off it reads public fields and
/// the stored value of auto-properties, which run no user code.
/// </summary>
/// <remarks>
/// The time budget stops a slow or endless sequence between two items. One call that never returns cannot be
/// interrupted here. The supervisor time limit of the whole run stops it.
/// </remarks>
/// <param name="policy">The limits.</param>
/// <param name="timeBudget">The time for the whole conversion. Null uses <see cref="DefaultTimeBudget"/>.</param>
internal sealed class ResultNodeConverter(ResultPolicy policy, TimeSpan? timeBudget = null)
{
    /// <summary>The default time for one conversion.</summary>
    public static readonly TimeSpan DefaultTimeBudget = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _budget = timeBudget ?? DefaultTimeBudget;
    private readonly Stopwatch _clock = new Stopwatch();
    private int _nodes;

    /// <summary>Converts one value. One converter object converts one value.</summary>
    /// <param name="value">The value, or null.</param>
    /// <returns>The tree.</returns>
    public ResultNode Convert(object? value)
    {
        _nodes = 1;
        _clock.Restart();
        return ConvertNode(value, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    private ResultNode ConvertNode(object? value, int depth, HashSet<object> path)
    {
        if (value is null)
        {
            return new NullNode();
        }

        Type type = value.GetType();
        string typeName = type.FullName ?? type.Name;

        if (value is string text)
        {
            return Text(text);
        }

        if (IsScalar(type))
        {
            string formatted = value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? string.Empty;
            return formatted.Length <= policy.MaxTextLength ? new ScalarNode(typeName, formatted) : Text(formatted);
        }

        // A container at the last allowed depth cannot show its children.
        if (depth >= policy.MaxDepth)
        {
            return new TruncatedNode(typeName, TruncationReason.DepthLimit);
        }

        if (path.Contains(value))
        {
            return new TruncatedNode(typeName, TruncationReason.Cycle);
        }

        if (IsPair(type))
        {
            return ConvertPair(typeName, value, type, depth, path);
        }

        if (value is IEnumerable enumerable)
        {
            path.Add(value);
            try
            {
                return ConvertSequence(typeName, enumerable, depth, path);
            }
            finally
            {
                path.Remove(value);
            }
        }

        path.Add(value);
        try
        {
            return policy.InvokePropertyGetters
                ? ConvertObject(typeName, value, type, depth, path)
                : ConvertData(typeName, value, type, depth, path);
        }
        finally
        {
            path.Remove(value);
        }
    }

    private SequenceNode ConvertSequence(string typeName, IEnumerable source, int depth, HashSet<object> path)
    {
        int room = Math.Max(0, policy.MaxNodes - _nodes);
        int take = Math.Min(policy.MaxItems, room);
        List<object?> items = [];
        TruncationReason? truncation = null;

        IEnumerator? enumerator = null;
        try
        {
            enumerator = source.GetEnumerator();
            while (items.Count < take)
            {
                if (_clock.Elapsed > _budget)
                {
                    truncation = TruncationReason.TimeLimit;
                    break;
                }

                if (!enumerator.MoveNext())
                {
                    break;
                }

                items.Add(enumerator.Current);
            }

            if (truncation is null && items.Count == take && enumerator.MoveNext())
            {
                truncation = take == policy.MaxItems ? TruncationReason.ItemLimit : TruncationReason.NodeLimit;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The sequence threw while the converter read it. Keep what it gave before.
            truncation = TruncationReason.Error;
        }
        finally
        {
            try
            {
                (enumerator as IDisposable)?.Dispose();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A failing Dispose of the user sequence does not change what the converter already read.
            }
        }

        _nodes += items.Count;
        List<ResultNode> nodes = [];
        foreach (object? item in items)
        {
            nodes.Add(ConvertNode(item, depth + 1, path));
        }

        return new SequenceNode(typeName, nodes, truncation);
    }

    private static bool IsPair(Type type) =>
        type == typeof(DictionaryEntry) || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);

    private ResultNode ConvertPair(string typeName, object value, Type type, int depth, HashSet<object> path)
    {
        // The entry itself is already counted as an item. Its key and value are two more nodes, so check the room first.
        if (policy.MaxNodes - _nodes < 2)
        {
            return new TruncatedNode(typeName, TruncationReason.NodeLimit);
        }

        // The key and the value of a dictionary entry are plain data. Reading them runs no user code.
        object? key = type.GetProperty("Key")!.GetValue(value);
        object? entryValue = type.GetProperty("Value")!.GetValue(value);
        _nodes += 2;
        return new ObjectNode(typeName, [new ResultProperty("Key", ConvertNode(key, depth + 1, path)), new ResultProperty("Value", ConvertNode(entryValue, depth + 1, path))], null);
    }

    private ResultNode ConvertData(string typeName, object value, Type type, int depth, HashSet<object> path)
    {
        List<(string Name, Func<object, object?> Read)> members = [];
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            FieldInfo? stored = property.GetIndexParameters().Length == 0 && property.GetMethod is not null ? FindStoredField(property) : null;
            if (stored is not null && names.Add(property.Name))
            {
                members.Add((property.Name, stored.GetValue));
            }
        }

        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (names.Add(field.Name))
            {
                members.Add((field.Name, field.GetValue));
            }
        }

        if (members.Count == 0)
        {
            return new UnsupportedNode(typeName, SafeToString(value));
        }

        int room = Math.Max(0, policy.MaxNodes - _nodes);
        int take = Math.Min(policy.MaxItems, room);
        TruncationReason? truncation = null;
        if (members.Count > take)
        {
            truncation = take == policy.MaxItems ? TruncationReason.ItemLimit : TruncationReason.NodeLimit;
            members = members.Take(take).ToList();
        }

        _nodes += members.Count;
        List<ResultProperty> result = [];
        foreach ((string name, Func<object, object?> read) in members)
        {
            result.Add(new ResultProperty(name, ConvertNode(read(value), depth + 1, path)));
        }

        return new ObjectNode(typeName, result, truncation);
    }

    private static FieldInfo? FindStoredField(PropertyInfo property)
    {
        // A compiler-made auto-property, a record property or an anonymous-type property keeps its value in a hidden field.
        // A property with a computed getter has no such field, so it is not shown.
        const BindingFlags FLAGS = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        Type? owner = property.DeclaringType;
        return owner?.GetField($"<{property.Name}>k__BackingField", FLAGS) ?? owner?.GetField($"<{property.Name}>i__Field", FLAGS);
    }

    private ObjectNode ConvertObject(string typeName, object value, Type type, int depth, HashSet<object> path)
    {
        List<PropertyInfo> properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is not null)
            .ToList();

        int room = Math.Max(0, policy.MaxNodes - _nodes);
        int take = Math.Min(policy.MaxItems, room);
        TruncationReason? truncation = null;
        if (properties.Count > take)
        {
            truncation = take == policy.MaxItems ? TruncationReason.ItemLimit : TruncationReason.NodeLimit;
            properties = properties.Take(take).ToList();
        }

        _nodes += properties.Count;
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
        List<ResultProperty> result = [];
        foreach (PropertyInfo property in properties)
        {
            if (!names.Add(property.Name))
            {
                // A property that hides a property of a base type has the same name. Show the first one.
                _nodes--;
                continue;
            }

            result.Add(new ResultProperty(property.Name, ReadProperty(property, value, depth, path)));
        }

        return new ObjectNode(typeName, result, truncation);
    }

    private ResultNode ReadProperty(PropertyInfo property, object owner, int depth, HashSet<object> path)
    {
        object? propertyValue;
        try
        {
            propertyValue = property.GetValue(owner);
        }
        catch (TargetInvocationException exception)
        {
            return new UnsupportedNode(property.PropertyType.FullName, Cut("The getter threw " + (exception.InnerException?.GetType().FullName ?? "an exception") + "."));
        }

        return ConvertNode(propertyValue, depth + 1, path);
    }

    private TextNode Text(string text)
    {
        return text.Length <= policy.MaxTextLength
            ? new TextNode(text, null)
            : new TextNode(text[..policy.MaxTextLength], text.Length);
    }

    private string? SafeToString(object value)
    {
        try
        {
            return Cut(value.ToString());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The ToString method of the user type threw. The type name is still useful.
            return null;
        }
    }

    private string? Cut(string? text)
    {
        return text is not null && text.Length > policy.MaxTextLength ? text[..policy.MaxTextLength] : text;
    }

    private static bool IsScalar(Type type)
    {
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan)
            || type == typeof(DateOnly)
            || type == typeof(TimeOnly)
            || type == typeof(Guid)
            || type == typeof(System.Numerics.BigInteger)
            || type == typeof(Int128)
            || type == typeof(UInt128)
            || type == typeof(Half);
    }
}
