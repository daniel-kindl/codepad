using System.Text.Json;
using Codepad.Cli;

namespace Codepad.Tests.Cli;

/// <summary>Runs the command line in the process with its own data and config folders. The worker is the real one.</summary>
public sealed class CliHarness : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("codepad-cli-").FullName;

    public CliHarness()
    {
        DataFolder = Path.Combine(root, "data");
        ConfigFolder = Path.Combine(root, "config");
    }

    public string DataFolder { get; }

    public string ConfigFolder { get; }

    public string Root => root;

    /// <summary>Gets or sets the folder with the worker program. Null uses the folder of the tests, which has the real worker.</summary>
    public string? WorkerFolder { get; set; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // A child process can hold a folder for a moment on Windows. The folder is temporary.
        }
    }

    public async Task<CliResult> RunAsync(string[] args, string? stdin = null, CancellationToken? cancellationToken = null)
    {
        using StringWriter stdout = new StringWriter();
        using StringWriter stderr = new StringWriter();
        CliEnvironment environment = new CliEnvironment(
            new StringReader(stdin ?? string.Empty),
            stdin is not null,
            stdout,
            stderr,
            DataFolder,
            ConfigFolder,
            TimeProvider.System,
            WorkerFolder ?? AppContext.BaseDirectory);
        int exit = await CliApp.RunAsync(args, environment, cancellationToken ?? TestContext.Current.CancellationToken);
        return new CliResult(exit, stdout.ToString(), stderr.ToString());
    }
}

public sealed record CliResult(int ExitCode, string Stdout, string Stderr)
{
    /// <summary>Parses the standard output. It must be exactly one JSON document.</summary>
    public JsonElement Json
    {
        get
        {
            string text = Stdout.Trim();
            Assert.False(string.IsNullOrEmpty(text), $"The standard output is empty. Stderr: {Stderr}");
            using JsonDocument document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
    }

    public string Outcome => Json.GetProperty("outcome").GetString()!;
}
