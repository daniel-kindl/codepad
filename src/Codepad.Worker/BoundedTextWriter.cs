using System.Text;
using Codepad.Contracts;

namespace Codepad.Worker;

/// <summary>
/// A text writer that keeps the first <paramref name="maxChars"/> characters and counts the rest.
/// The user code writes to it through <c>Console.Out</c> and <c>Console.Error</c>. It is safe for many threads.
/// </summary>
/// <param name="maxChars">The most characters to keep.</param>
internal sealed class BoundedTextWriter(int maxChars) : TextWriter
{
    private readonly StringBuilder _text = new StringBuilder();
    private readonly Lock _gate = new Lock();
    private long _total;

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        Append(new ReadOnlySpan<char>(in value));
    }

    public override void Write(string? value)
    {
        Append(value);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        Append(buffer.AsSpan(index, count));
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        Append(buffer);
    }

    /// <summary>Takes the text that the writer holds now.</summary>
    /// <returns>The kept text. It has the original length when the writer cut text.</returns>
    public CapturedText Snapshot()
    {
        lock (_gate)
        {
            string text = _text.ToString();
            return _total > text.Length ? new CapturedText(text, (int)Math.Min(_total, int.MaxValue)) : new CapturedText(text, null);
        }
    }

    private void Append(ReadOnlySpan<char> value)
    {
        lock (_gate)
        {
            _total += value.Length;
            int room = maxChars - _text.Length;
            if (room > 0)
            {
                _text.Append(value[..Math.Min(room, value.Length)]);
            }
        }
    }
}
