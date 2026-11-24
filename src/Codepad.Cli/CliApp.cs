using System.Reflection;
using System.Text.Json;
using Codepad.Application.Experiments;

namespace Codepad.Cli;

/// <summary>
/// The command line. <see cref="RunAsync"/> takes the arguments and the surroundings and gives the exit code. The standard output
/// gets one JSON document (or the help text for --help and --version). Everything else goes to the standard error.
/// </summary>
internal static class CliApp
{
    private static readonly IReadOnlyDictionary<string, bool> s_global = new Dictionary<string, bool>
    {
        ["--data-dir"] = true,
        ["--config-dir"] = true,
        ["--pretty"] = false,
        ["--help"] = false,
        ["-h"] = false,
        ["--version"] = false,
    };

    private static readonly string[] s_commands = ["run", "json", "list", "search", "get", "create", "update", "history"];

    public static async Task<int> RunAsync(string[] arguments, CliEnvironment environment, CancellationToken cancellationToken)
    {
        string command = "codepad";
        bool pretty = false;
        try
        {
            (Args global, string? name, List<string> rest) = SplitGlobal(arguments);
            pretty = global.Has("--pretty");
            if (global.Has("--version"))
            {
                await Line(environment.Stdout, Version()).ConfigureAwait(false);
                return ExitCodes.SUCCESS;
            }

            bool helpRequested = global.Has("--help") || global.Has("-h") || rest.Contains("--help", StringComparer.Ordinal) || rest.Contains("-h", StringComparer.Ordinal);
            if (name == "help")
            {
                await Line(environment.Stdout, rest.Count > 0 ? HelpText.For(rest[0]) : HelpText.General()).ConfigureAwait(false);
                return ExitCodes.SUCCESS;
            }

            if (name is null)
            {
                if (helpRequested)
                {
                    await Line(environment.Stdout, HelpText.General()).ConfigureAwait(false);
                    return ExitCodes.SUCCESS;
                }

                throw new UsageException("Give a command. Run 'codepad --help' to see the commands.");
            }

            command = name;
            if (!s_commands.Contains(name, StringComparer.Ordinal))
            {
                throw new UsageException($"Unknown command '{name}'. Run 'codepad --help' to see the commands.");
            }

            if (helpRequested)
            {
                await Line(environment.Stdout, HelpText.For(name)).ConfigureAwait(false);
                return ExitCodes.SUCCESS;
            }

            CliEnvironment effective = environment with { DataFolder = global.Value("--data-dir") ?? environment.DataFolder, ConfigFolder = global.Value("--config-dir") ?? environment.ConfigFolder };
            Args args = Args.Parse(rest, Commands.Spec(name));
            (object output, int exitCode) = await ExecuteAsync(name, args, effective, cancellationToken).ConfigureAwait(false);
            await WriteAsync(environment, output, pretty).ConfigureAwait(false);
            return exitCode;
        }
        catch (UsageException exception)
        {
            return await FailAsync(environment, command, ExitCodes.USAGE, "usage", exception.Message, pretty).ConfigureAwait(false);
        }
        catch (InputException exception)
        {
            return await FailAsync(environment, command, ExitCodes.INPUT, exception.Code, exception.Message, pretty).ConfigureAwait(false);
        }
        catch (StorageUnavailableException exception)
        {
            return await FailAsync(environment, command, ExitCodes.STORAGE, "storage_unavailable", exception.Message, pretty).ConfigureAwait(false);
        }
        catch (ExperimentStorageException exception)
        {
            return await FailAsync(environment, command, ExitCodes.STORAGE, "storage_failed", exception.Message, pretty).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await FailAsync(environment, command, ExitCodes.CANCELED, "canceled", "The command was canceled.", pretty).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A bug. The message can hold a path or text of the user, so only the type goes out.
            await Line(environment.Stderr, $"Internal error: {exception.GetType().Name}.").ConfigureAwait(false);
            return await FailAsync(environment, command, ExitCodes.INTERNAL_ERROR, "internal_error", "CodePad stopped on an unexpected error.", pretty).ConfigureAwait(false);
        }
    }

    private static async Task<(object Output, int ExitCode)> ExecuteAsync(string name, Args args, CliEnvironment environment, CancellationToken cancellationToken)
    {
        // The JSON command needs no database.
        if (name == "json")
        {
            return await Commands.JsonAsync(args, environment, cancellationToken).ConfigureAwait(false);
        }

        using Services services = Services.Open(environment);
        return name switch
        {
            "run" => await Commands.RunAsync(args, services, environment, cancellationToken).ConfigureAwait(false),
            "list" or "search" => await Commands.ListAsync(name, args, services, cancellationToken).ConfigureAwait(false),
            "get" => await Commands.GetAsync(args, services, environment.Clock, cancellationToken).ConfigureAwait(false),
            "create" => await Commands.CreateAsync(args, services, environment, cancellationToken).ConfigureAwait(false),
            "update" => await Commands.UpdateAsync(args, services, environment, cancellationToken).ConfigureAwait(false),
            _ => await Commands.HistoryAsync(args, services, environment.Clock, cancellationToken).ConfigureAwait(false),
        };
    }

    // Global options may stand before the command. The first word that is not an option is the command.
    private static (Args Global, string? Command, List<string> Remaining) SplitGlobal(string[] arguments)
    {
        List<string> before = [];
        int index = 0;
        for (; index < arguments.Length; index++)
        {
            string token = arguments[index];
            if (!token.StartsWith('-') || token == "-")
            {
                break;
            }

            before.Add(token);
            if ((token == "--data-dir" || token == "--config-dir") && index + 1 < arguments.Length)
            {
                before.Add(arguments[++index]);
            }
        }

        string? command = index < arguments.Length ? arguments[index] : null;
        List<string> rest = index + 1 < arguments.Length ? [.. arguments[(index + 1)..]] : [];

        // A global option after the command works too: split it out of the rest.
        List<string> remaining = [];
        bool afterSeparator = false;
        for (int position = 0; position < rest.Count; position++)
        {
            string token = rest[position];
            if (token == "--")
            {
                afterSeparator = true;
            }

            if (!afterSeparator && token is "--pretty" or "--data-dir" or "--config-dir")
            {
                before.Add(token);
                if (token != "--pretty" && position + 1 < rest.Count)
                {
                    before.Add(rest[++position]);
                }
            }
            else
            {
                remaining.Add(token);
            }
        }

        return (Args.Parse(before, s_global), command, remaining);
    }

    private static async Task<int> FailAsync(CliEnvironment environment, string command, int exitCode, string code, string message, bool pretty)
    {
        (object output, int _) = Commands.Error(command, exitCode, code, message);
        await WriteAsync(environment, output, pretty).ConfigureAwait(false);
        await Line(environment.Stderr, $"codepad: {message}").ConfigureAwait(false);
        return exitCode;
    }

    private static async Task WriteAsync(CliEnvironment environment, object output, bool pretty)
    {
        string json = JsonSerializer.Serialize(output, output.GetType(), pretty ? CliJson.Indented : CliJson.Compact);
        await Line(environment.Stdout, json).ConfigureAwait(false);
        await environment.Stdout.FlushAsync().ConfigureAwait(false);
    }

    // The writers take no token for a string. The write is short and local, so the call passes None on purpose.
    private static Task Line(TextWriter writer, string text) => writer.WriteLineAsync(text.AsMemory(), CancellationToken.None);

    private static string Version() => typeof(CliApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
}
