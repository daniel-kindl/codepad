using System.Buffers.Binary;
using System.Text.Json;

namespace Codepad.Contracts;

/// <summary>Why a frame could not be read.</summary>
public enum FrameErrorKind
{
    /// <summary>The stream ended in the middle of a frame.</summary>
    Truncated,

    /// <summary>The length prefix is negative or larger than the limit.</summary>
    Oversized,

    /// <summary>The payload is not valid JSON for the message type.</summary>
    Malformed,
}

/// <summary>A frame is invalid. The receiver treats the sender as failed.</summary>
public sealed class FrameException : Exception
{
    /// <summary>Makes the exception.</summary>
    /// <param name="kind">Why the frame is invalid.</param>
    /// <param name="message">A log-safe description. It never holds the payload.</param>
    /// <param name="inner">The exception that caused the error, if any.</param>
    public FrameException(FrameErrorKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }

    /// <summary>Gets why the frame is invalid.</summary>
    public FrameErrorKind Kind { get; }
}

/// <summary>
/// Reads and writes one message as one frame: a 4-byte big-endian payload length, then the UTF-8 JSON payload.
/// The reader checks the length before it allocates the payload.
/// </summary>
public static class FrameCodec
{
    private const int HEADER_BYTES = 4;

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    /// <summary>Writes one message and flushes the stream.</summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="message">The message.</param>
    /// <param name="maxBytes">The limit for the payload.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes after the flush.</returns>
    /// <exception cref="FrameException">The payload is larger than <paramref name="maxBytes"/>.</exception>
    public static async Task WriteAsync<T>(Stream stream, T message, int maxBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        if (payload.Length > maxBytes)
        {
            throw new FrameException(FrameErrorKind.Oversized, $"The message is {payload.Length} bytes. The limit is {maxBytes}.");
        }

        byte[] frame = new byte[HEADER_BYTES + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
        payload.CopyTo(frame, HEADER_BYTES);
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one message.</summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="maxBytes">The limit for the payload.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The message, or null when the stream ended before the first byte of a frame.</returns>
    /// <exception cref="FrameException">The frame is cut, too large or not valid for <typeparamref name="T"/>.</exception>
    public static async Task<T?> ReadAsync<T>(Stream stream, int maxBytes, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] header = new byte[HEADER_BYTES];
        int headerRead = await ReadBlockAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (headerRead == 0)
        {
            return null;
        }

        if (headerRead < HEADER_BYTES)
        {
            throw new FrameException(FrameErrorKind.Truncated, "The stream ended in the length prefix.");
        }

        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 0 || length > maxBytes)
        {
            throw new FrameException(FrameErrorKind.Oversized, $"The length prefix is {length}. The limit is {maxBytes}.");
        }

        byte[] payload = new byte[length];
        int payloadRead = await ReadBlockAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        if (payloadRead < length)
        {
            throw new FrameException(FrameErrorKind.Truncated, $"The stream ended after {payloadRead} of {length} payload bytes.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(payload, Options)
                ?? throw new FrameException(FrameErrorKind.Malformed, "The payload is the JSON null.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new FrameException(FrameErrorKind.Malformed, "The payload is not a valid message.", exception);
        }
    }

    private static async Task<int> ReadBlockAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
