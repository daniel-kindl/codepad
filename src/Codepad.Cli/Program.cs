using System.Text;

namespace Codepad.Cli;

/// <summary>The entry point of <c>codepad</c>. It connects the console and gives the exit code of <see cref="CliApp"/>.</summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        UTF8Encoding utf8 = new UTF8Encoding(false);
        using StreamReader stdin = new StreamReader(Console.OpenStandardInput(), utf8);
        using StreamWriter stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = false };
        using StreamWriter stderr = new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true };
        using CancellationTokenSource cancel = new CancellationTokenSource();

        // Ctrl+C stops a run in progress. The run then has the outcome Canceled, and the worker and its children are stopped.
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancel.Cancel();
        };

        CliEnvironment environment = new CliEnvironment(
            stdin,
            Console.IsInputRedirected,
            stdout,
            stderr,
            Environment.GetEnvironmentVariable("CODEPAD_DATA_DIR"),
            Environment.GetEnvironmentVariable("CODEPAD_CONFIG_DIR"),
            TimeProvider.System);
        return await CliApp.RunAsync(args, environment, cancel.Token).ConfigureAwait(false);
    }
}
