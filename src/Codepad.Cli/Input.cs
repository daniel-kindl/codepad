using System.Text;

namespace Codepad.Cli;

/// <summary>The input of a command could not be read. The message never holds the content.</summary>
internal sealed class InputException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Reads the text that a command works on: a file, or the standard input. The size is bounded.</summary>
internal static class Input
{
    /// <summary>The largest input in characters. It matches the largest message that the worker protocol takes.</summary>
    public const int MAX_CHARS = 2_000_000;

    /// <summary>Reads from a file, or from the standard input when the path is a dash.</summary>
    /// <param name="path">The path, or <c>-</c>.</param>
    /// <param name="environment">The surroundings.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The text, without a byte order mark.</returns>
    public static async Task<string> ReadAsync(string path, CliEnvironment environment, CancellationToken cancellationToken)
    {
        if (path == "-")
        {
            return await ReadStdinAsync(environment, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            FileInfo info = new FileInfo(path);
            if (!info.Exists)
            {
                throw new InputException("input_not_found", "The input file does not exist.");
            }

            if (info.Length > MAX_CHARS * 4L)
            {
                throw new InputException("input_too_large", $"The input is larger than {MAX_CHARS:N0} characters.");
            }

            using StreamReader reader = new StreamReader(path, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            return await ReadBoundedAsync(reader, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The message of these exceptions holds the path. The caller gets a code.
            throw new InputException("input_unreadable", "The input file cannot be read.");
        }
    }

    /// <summary>Reads the standard input. It does not wait on a terminal.</summary>
    /// <param name="environment">The surroundings.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The text.</returns>
    public static async Task<string> ReadStdinAsync(CliEnvironment environment, CancellationToken cancellationToken)
    {
        if (!environment.StdinIsRedirected)
        {
            throw new UsageException("There is no input. Give a file with --file PATH, or pipe the text to the standard input.");
        }

        return await ReadBoundedAsync(environment.Stdin, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadBoundedAsync(TextReader reader, CancellationToken cancellationToken)
    {
        StringBuilder builder = new StringBuilder();
        char[] buffer = new char[16 * 1024];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            builder.Append(buffer, 0, read);
            if (builder.Length > MAX_CHARS)
            {
                throw new InputException("input_too_large", $"The input is larger than {MAX_CHARS:N0} characters.");
            }
        }

        return builder.ToString();
    }
}
