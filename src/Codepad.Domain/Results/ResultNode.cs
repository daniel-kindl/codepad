using System.Text.Json.Serialization;

namespace Codepad.Domain.Results;

/// <summary>
/// One node of a bounded, serializable result tree. The worker (C#) and the JSON tool convert values to it,
/// and the UI shows it.
/// </summary>
/// <remarks>
/// <para>
/// The set of node kinds is closed. The <c>kind</c> JSON property names the kind: <c>null</c>, <c>scalar</c>,
/// <c>text</c>, <c>object</c>, <c>sequence</c>, <c>truncated</c> or <c>unsupported</c>.
/// </para>
/// <para>
/// A result is a tree, not a graph. Each node is immutable and copies its children, so a node cannot contain
/// itself. A converter that finds a cycle in a .NET value writes a <see cref="TruncatedNode"/> with
/// <see cref="TruncationReason.Cycle"/> in place of the repeated value.
/// </para>
/// <para>
/// The domain defines the limits in <c>ResultPolicy</c>. It does not convert .NET values and calls no getters.
/// </para>
/// <para>
/// Deserialization runs the same checks as the constructors. Bad wire data can throw <c>JsonException</c>,
/// or an <see cref="ArgumentException"/> (also <see cref="ArgumentNullException"/> and
/// <see cref="ArgumentOutOfRangeException"/>) from a constructor. A receiver must treat both kinds as bad data,
/// for example as a <c>WorkerFailed</c> outcome. Then it checks the tree with <c>ResultPolicy.FindExceededLimit</c>.
/// </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(NullNode), "null")]
[JsonDerivedType(typeof(ScalarNode), "scalar")]
[JsonDerivedType(typeof(TextNode), "text")]
[JsonDerivedType(typeof(ObjectNode), "object")]
[JsonDerivedType(typeof(SequenceNode), "sequence")]
[JsonDerivedType(typeof(TruncatedNode), "truncated")]
[JsonDerivedType(typeof(UnsupportedNode), "unsupported")]
public abstract class ResultNode
{
    // Only this assembly can add a kind.
    private protected ResultNode()
    {
    }
}

/// <summary>A null value.</summary>
public sealed class NullNode : ResultNode
{
}

/// <summary>A number, a Boolean, a date, an enum value or another value that one short text shows completely.</summary>
/// <param name="typeName">The type of the value, for example <c>System.Int32</c>, or null if the source has no type.</param>
/// <param name="value">The value as invariant-culture text.</param>
/// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
public sealed class ScalarNode(string? typeName, string value) : ResultNode
{
    /// <summary>Gets the type of the value, or null.</summary>
    public string? TypeName { get; } = typeName;

    /// <summary>Gets the value as invariant-culture text.</summary>
    public string Value { get; } = value ?? throw new ArgumentNullException(nameof(value));
}

/// <summary>A string value, complete or cut to a maximum length.</summary>
public sealed class TextNode : ResultNode
{
    /// <summary>Makes a text node.</summary>
    /// <param name="value">The text, or its start if the converter cut it.</param>
    /// <param name="originalLength">The length of the full text if the converter cut it. Otherwise null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="originalLength"/> is not longer than <paramref name="value"/>.</exception>
    public TextNode(string value, int? originalLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (originalLength is int length)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, value.Length, nameof(originalLength));
        }

        Value = value;
        OriginalLength = originalLength;
    }

    /// <summary>Gets the text, or its start if the converter cut it.</summary>
    public string Value { get; }

    /// <summary>Gets the length of the full text if the converter cut it. Otherwise null.</summary>
    public int? OriginalLength { get; }

    /// <summary>Gets a value indicating whether the converter cut the text.</summary>
    [JsonIgnore]
    public bool IsTruncated => OriginalLength is not null;
}

/// <summary>One named value of an <see cref="ObjectNode"/>.</summary>
public sealed class ResultProperty
{
    /// <summary>Makes a property.</summary>
    /// <param name="name">The name. It can be empty.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ResultProperty(string name, ResultNode value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        Name = name;
        Value = value;
    }

    /// <summary>Gets the name.</summary>
    public string Name { get; }

    /// <summary>Gets the value.</summary>
    public ResultNode Value { get; }
}

/// <summary>An object with named properties, for example a record or a JSON object.</summary>
public sealed class ObjectNode : ResultNode
{
    private readonly ResultProperty[] _properties;

    /// <summary>Makes an object node.</summary>
    /// <param name="typeName">The type of the object, or null if the source has no type.</param>
    /// <param name="properties">The properties in display order. The node keeps its own copy. Names must be unique (ordinal).</param>
    /// <param name="truncation">The reason that the converter left out properties, or null if the node holds all of them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="properties"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="properties"/> contains null or two properties with the same name.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="truncation"/> is not defined.</exception>
    public ObjectNode(string? typeName, IReadOnlyList<ResultProperty> properties, TruncationReason? truncation)
    {
        _properties = Guard.CopyWithNoNulls(properties);
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ResultProperty property in _properties)
        {
            if (!names.Add(property.Name))
            {
                throw new ArgumentException($"The object has two properties with the name '{property.Name}'.", nameof(properties));
            }
        }

        TypeName = typeName;
        Truncation = truncation is TruncationReason reason ? Guard.Defined(reason, nameof(truncation)) : null;
    }

    /// <summary>Gets the type of the object, or null.</summary>
    public string? TypeName { get; }

    /// <summary>Gets the properties in display order.</summary>
    public IReadOnlyList<ResultProperty> Properties => _properties;

    /// <summary>Gets the reason that the converter left out properties, or null if the node holds all of them.</summary>
    public TruncationReason? Truncation { get; }
}

/// <summary>An ordered sequence of values, for example an array, a list or a JSON array.</summary>
public sealed class SequenceNode : ResultNode
{
    private readonly ResultNode[] _items;

    /// <summary>Makes a sequence node.</summary>
    /// <param name="typeName">The type of the sequence, or null if the source has no type.</param>
    /// <param name="items">The items in order. The node keeps its own copy.</param>
    /// <param name="truncation">
    /// The reason that the converter stopped before the end, or null if the node holds all items.
    /// The full count is not always known, for example for an endless sequence.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="items"/> contains null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="truncation"/> is not defined.</exception>
    public SequenceNode(string? typeName, IReadOnlyList<ResultNode> items, TruncationReason? truncation)
    {
        _items = Guard.CopyWithNoNulls(items);
        TypeName = typeName;
        Truncation = truncation is TruncationReason reason ? Guard.Defined(reason, nameof(truncation)) : null;
    }

    /// <summary>Gets the type of the sequence, or null.</summary>
    public string? TypeName { get; }

    /// <summary>Gets the items in order.</summary>
    public IReadOnlyList<ResultNode> Items => _items;

    /// <summary>Gets the reason that the converter stopped before the end, or null.</summary>
    public TruncationReason? Truncation { get; }
}

/// <summary>A value that the converter did not expand, because of a cycle or a limit.</summary>
public sealed class TruncatedNode : ResultNode
{
    /// <summary>Makes a truncated node.</summary>
    /// <param name="typeName">The type of the value that the node replaces, or null.</param>
    /// <param name="reason">Why the converter did not expand the value.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reason"/> is not defined.</exception>
    public TruncatedNode(string? typeName, TruncationReason reason)
    {
        TypeName = typeName;
        Reason = Guard.Defined(reason);
    }

    /// <summary>Gets the type of the value that the node replaces, or null.</summary>
    public string? TypeName { get; }

    /// <summary>Gets why the converter did not expand the value.</summary>
    public TruncationReason Reason { get; }
}

/// <summary>
/// A value that the converter cannot show as data, for example a pointer, a delegate or a property whose getter
/// the policy does not let it call.
/// </summary>
/// <param name="typeName">The type of the value, or null.</param>
/// <param name="text">A safe text for the value, or null if there is none.</param>
public sealed class UnsupportedNode(string? typeName, string? text) : ResultNode
{
    /// <summary>Gets the type of the value, or null.</summary>
    public string? TypeName { get; } = typeName;

    /// <summary>Gets a safe text for the value, or null.</summary>
    public string? Text { get; } = text;
}
