using System.Collections;
using System.Globalization;
using System.Reflection;
using Codepad.Domain.Operations;
using Codepad.Domain.Results;

namespace Codepad.Worker;

/// <summary>
/// Converts a .NET value to a bounded <see cref="ResultNode"/> tree. The tree always respects the
/// <see cref="ResultPolicy"/>: <see cref="ResultPolicy.FindExceededLimit"/> finds no limit that it breaks.
/// A value that is too deep, too wide or too long becomes a marked cut. A cycle becomes a <see cref="TruncatedNode"/>.
/// The converter calls property getters only when the policy allows it.
/// </summary>
/// <param name="policy">The limits.</param>
internal sealed class ResultNodeConverter(ResultPolicy policy)
{
    private int _nodes;

    /// <summary>Converts one value. One converter object converts one value.</summary>
    /// <param name="value">The value, or null.</param>
    /// <returns>The tree.</returns>
    public ResultNode Convert(object? value)
    {
        _nodes = 1;
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

        if (!policy.InvokePropertyGetters)
        {
            return new UnsupportedNode(typeName, SafeToString(value));
        }

        path.Add(value);
        try
        {
            return ConvertObject(typeName, value, type, depth, path);
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

        IEnumerator enumerator = source.GetEnumerator();
        try
        {
            while (items.Count < take && enumerator.MoveNext())
            {
                items.Add(enumerator.Current);
            }

            if (items.Count == take && enumerator.MoveNext())
            {
                truncation = take == policy.MaxItems ? TruncationReason.ItemLimit : TruncationReason.NodeLimit;
            }
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }

        _nodes += items.Count;
        List<ResultNode> nodes = [];
        foreach (object? item in items)
        {
            nodes.Add(ConvertNode(item, depth + 1, path));
        }

        return new SequenceNode(typeName, nodes, truncation);
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
