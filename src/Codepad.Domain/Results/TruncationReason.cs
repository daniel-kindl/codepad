using System.Text.Json.Serialization;

namespace Codepad.Domain.Results;

/// <summary>Why a result tree holds less than the full value.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TruncationReason>))]
public enum TruncationReason
{
    /// <summary>The value refers back to a value on the path from the root. The tree holds no cycles.</summary>
    Cycle,

    /// <summary>The value is deeper than <c>ResultPolicy.MaxDepth</c>.</summary>
    DepthLimit,

    /// <summary>The object or sequence has more than <c>ResultPolicy.MaxItems</c> properties or items.</summary>
    ItemLimit,

    /// <summary>The text is longer than <c>ResultPolicy.MaxTextLength</c>.</summary>
    TextLimit,

    /// <summary>The tree reached <c>ResultPolicy.MaxNodes</c> nodes.</summary>
    NodeLimit,

    /// <summary>The conversion used all of its time, for example on a slow or endless sequence.</summary>
    TimeLimit,
}
