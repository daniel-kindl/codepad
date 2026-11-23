namespace Codepad.Application.Json;

/// <summary>The limits of the live JSON provider. A document over a limit fails with a message. It never crashes the app.</summary>
/// <param name="MaxBytes">The largest document size in UTF-8 bytes.</param>
/// <param name="MaxDepth">The largest nesting depth of arrays and objects.</param>
public sealed record JsonLimits(int MaxBytes, int MaxDepth)
{
    /// <summary>Gets the default limits: 1,000,000 bytes and 64 levels.</summary>
    public static JsonLimits Default { get; } = new JsonLimits(1_000_000, 64);

    /// <summary>Gets the largest document size in UTF-8 bytes.</summary>
    public int MaxBytes { get; } = MaxBytes > 0 ? MaxBytes : throw new ArgumentOutOfRangeException(nameof(MaxBytes), "The size limit must be at least 1.");

    /// <summary>Gets the largest nesting depth.</summary>
    public int MaxDepth { get; } = MaxDepth > 0 ? MaxDepth : throw new ArgumentOutOfRangeException(nameof(MaxDepth), "The depth limit must be at least 1.");
}
