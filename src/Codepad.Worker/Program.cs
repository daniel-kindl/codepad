using Codepad.Contracts;

namespace Codepad.Worker;

/// <summary>
/// The worker process. It reads one request from the standard input, compiles and runs the script, and writes one
/// response to the standard output. The protocol uses the raw streams. The script sees only <c>Console.Out</c>
/// and <c>Console.Error</c> that capture the text, and an empty <c>Console.In</c>.
/// The process is a crash boundary. It is not a security sandbox.
/// </summary>
internal static class Program
{
    /// <summary>Runs the worker.</summary>
    /// <returns>0 after a response. <see cref="WorkerProtocol.ExitBadRequest"/> when the request is not valid.</returns>
    public static async Task<int> Main()
    {
        using Stream input = Console.OpenStandardInput();
        using Stream output = Console.OpenStandardOutput();

        WorkerRunRequest? request = await ReadRequestAsync(input).ConfigureAwait(false);
        if (request is null)
        {
            return WorkerProtocol.ExitBadRequest;
        }

        BoundedTextWriter stdout = new BoundedTextWriter(request.MaxOutputChars);
        BoundedTextWriter stderr = new BoundedTextWriter(request.MaxOutputChars);
        Console.SetOut(stdout);
        Console.SetError(stderr);
        Console.SetIn(TextReader.Null);
        StopWhenTheAppIsGone(input);

        WorkerRunResponse response = await ScriptEngine.RunAsync(request, stdout, stderr).ConfigureAwait(false);
        await WriteResponseAsync(output, response).ConfigureAwait(false);
        return 0;
    }

    private static async Task<WorkerRunRequest?> ReadRequestAsync(Stream input)
    {
        try
        {
            WorkerRunRequest? request = await FrameCodec.ReadAsync<WorkerRunRequest>(input, WorkerProtocol.MaxMessageBytes).ConfigureAwait(false);
            bool valid = request is not null
                && request.ProtocolVersion == WorkerProtocol.Version
                && request.Source is not null
                && request.Results is not null
                && request.MaxOutputChars is >= 1 and <= 1_000_000;
            return valid ? request : null;
        }
        catch (FrameException)
        {
            return null;
        }
    }

    private static async Task WriteResponseAsync(Stream output, WorkerRunResponse response)
    {
        try
        {
            await FrameCodec.WriteAsync(output, response, WorkerProtocol.MaxMessageBytes).ConfigureAwait(false);
        }
        catch (FrameException)
        {
            // The value is larger than a message can be. Send the response again with a marked cut instead of the value.
            List<Codepad.Domain.Results.Diagnostic> diagnostics = [.. response.Diagnostics];
            diagnostics.Add(new Codepad.Domain.Results.Diagnostic(Codepad.Domain.Results.DiagnosticSeverity.Warning, "ResultTooLarge", "The result is larger than one message can be. The value is cut.", null));
            WorkerRunResponse smaller = response with
            {
                Diagnostics = diagnostics,
                Value = response.Value is null ? null : new Codepad.Domain.Results.TruncatedNode(null, Codepad.Domain.Results.TruncationReason.TextLimit),
            };
            await FrameCodec.WriteAsync(output, smaller, WorkerProtocol.MaxMessageBytes).ConfigureAwait(false);
        }
    }

    private static void StopWhenTheAppIsGone(Stream input)
    {
        // The app keeps the input open until the run ends. A closed input means that the app is gone, so the worker exits.
        _ = Task.Run(async () =>
        {
            byte[] buffer = new byte[1];
            try
            {
                while (await input.ReadAsync(buffer).ConfigureAwait(false) > 0)
                {
                }
            }
            catch (IOException)
            {
                // A broken pipe also means that the app is gone.
            }

            Environment.Exit(WorkerProtocol.ExitBadRequest);
        });
    }
}
