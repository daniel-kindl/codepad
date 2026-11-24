namespace Codepad.Cli;

/// <summary>The command line is wrong. The message says what to change. It never holds the content of a file.</summary>
internal sealed class UsageException(string message) : Exception(message);

/// <summary>A parsed command line: the words and the options. An option is <c>--name value</c>, <c>--name=value</c> or a flag.</summary>
internal sealed class Args
{
    private readonly Dictionary<string, string?> options;

    private Args(List<string> positional, Dictionary<string, string?> options)
    {
        Positional = positional;
        this.options = options;
    }

    /// <summary>Gets the words that are not options, in order.</summary>
    public IReadOnlyList<string> Positional { get; }

    /// <summary>Parses tokens against the options that a command allows.</summary>
    /// <param name="tokens">The tokens after the command name.</param>
    /// <param name="allowed">The allowed option names. The value tells whether the option takes a value.</param>
    /// <returns>The parsed line.</returns>
    /// <exception cref="UsageException">An option is unknown, repeated or has no value.</exception>
    public static Args Parse(IReadOnlyList<string> tokens, IReadOnlyDictionary<string, bool> allowed)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(allowed);
        List<string> positional = [];
        Dictionary<string, string?> found = new Dictionary<string, string?>(StringComparer.Ordinal);
        bool onlyPositional = false;
        for (int index = 0; index < tokens.Count; index++)
        {
            string token = tokens[index];
            if (onlyPositional || token == "-" || !token.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(token);
                continue;
            }

            if (token == "--")
            {
                onlyPositional = true;
                continue;
            }

            string name = token;
            string? inline = null;
            int equals = token.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                name = token[..equals];
                inline = token[(equals + 1)..];
            }

            if (!allowed.TryGetValue(name, out bool takesValue))
            {
                throw new UsageException($"Unknown option {name}. Run with --help to see the options.");
            }

            if (found.ContainsKey(name))
            {
                throw new UsageException($"The option {name} is given twice.");
            }

            if (!takesValue)
            {
                if (inline is not null)
                {
                    throw new UsageException($"The option {name} takes no value.");
                }

                found[name] = null;
                continue;
            }

            string? value = inline;
            if (value is null)
            {
                if (index + 1 >= tokens.Count)
                {
                    throw new UsageException($"The option {name} needs a value.");
                }

                value = tokens[++index];
            }

            found[name] = value;
        }

        return new Args(positional, found);
    }

    /// <summary>Tells whether an option or flag is present.</summary>
    /// <param name="name">The option name with the two dashes.</param>
    /// <returns>True when present.</returns>
    public bool Has(string name) => options.ContainsKey(name);

    /// <summary>Gets the value of an option.</summary>
    /// <param name="name">The option name with the two dashes.</param>
    /// <returns>The value, or null.</returns>
    public string? Value(string name) => options.GetValueOrDefault(name);

    /// <summary>Gets a whole number option.</summary>
    /// <param name="name">The option name.</param>
    /// <param name="min">The smallest allowed value.</param>
    /// <param name="max">The largest allowed value.</param>
    /// <returns>The number, or null when the option is absent.</returns>
    /// <exception cref="UsageException">The value is not a whole number in the range.</exception>
    public long? Number(string name, long min, long max)
    {
        string? text = Value(name);
        if (text is null)
        {
            return null;
        }

        if (!long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long value) || value < min || value > max)
        {
            throw new UsageException($"The option {name} needs a whole number from {min} to {max}.");
        }

        return value;
    }
}
