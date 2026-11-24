using System.Buffers.Binary;
using System.Diagnostics;
using Codepad.Contracts;
using Codepad.Domain.Executions;
using Codepad.Domain.Results;

namespace Codepad.FaultWorker;

/// <summary>
/// A worker that fails in one chosen way. The first argument is the mode. The optional second argument is a file where the
/// program writes its process ID, and the optional third argument is a file for the ID of a child process.
/// The tests use it to check how the app reacts to a crash, a hang, a bad message and an overlong output.
/// </summary>
internal static class Program
{
    private const int FOREVER_MS = 600_000;

    public static async Task<int> Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "ok";
        if (args.Length > 1)
        {
            await File.WriteAllTextAsync(args[1], Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        using Stream input = Console.OpenStandardInput();
        using Stream output = Console.OpenStandardOutput();
        WorkerRunRequest? request = await FrameCodec.ReadAsync<WorkerRunRequest>(input, WorkerProtocol.MaxMessageBytes);
        if (request is null)
        {
            return 3;
        }

        switch (mode)
        {
            case "ok":
                await Send(output, Valid(request));
                return 0;
            case "crash":
                Environment.FailFast("fault injection");
                return 1;
            case "hang":
                await Task.Delay(FOREVER_MS);
                return 0;
            case "hang-with-child":
                StartChild(args.Length > 2 ? args[2] : null);
                await Task.Delay(FOREVER_MS);
                return 0;
            case "slow":
                await Task.Delay(3_000);
                await Send(output, Valid(request));
                return 0;
            case "stderr-flood":
                await FloodStderr();
                await Send(output, Valid(request));
                return 0;
            case "oversized-prefix":
                await Raw(output, Prefix(int.MaxValue));
                await Task.Delay(FOREVER_MS);
                return 0;
            case "negative-prefix":
                await Raw(output, Prefix(-5));
                await Task.Delay(FOREVER_MS);
                return 0;
            case "partial-prefix":
                await Raw(output, [0, 0]);
                return 0;
            case "truncated-payload":
                await Raw(output, [.. Prefix(1_000), .. new byte[10]]);
                return 0;
            case "invalid-json":
                await Raw(output, [.. Prefix(8), .. "not json"u8.ToArray()]);
                return 0;
            case "json-null":
                await Raw(output, [.. Prefix(4), .. "null"u8.ToArray()]);
                return 0;
            case "wrong-id":
                await Send(output, Valid(request) with { ExecutionId = Guid.NewGuid() });
                return 0;
            case "wrong-revision":
                await Send(output, Valid(request) with { SourceRevision = request.SourceRevision + 1 });
                return 0;
            case "wrong-version":
                await Send(output, Valid(request) with { ProtocolVersion = 99 });
                return 0;
            case "bad-outcome":
                await Send(output, Valid(request) with { Outcome = ExecutionOutcome.TimedOut });
                return 0;
            case "deep-value":
                await Send(output, Valid(request) with { Value = Deep(request.Results.MaxDepth + 3) });
                return 0;
            case "huge-output":
                await Send(output, Valid(request) with { Stdout = new CapturedText(new string('x', request.MaxOutputChars + 1), null) });
                return 0;
            case "success-without-value-but-error":
                await Send(output, Valid(request) with { Diagnostics = [new Diagnostic(DiagnosticSeverity.Error, "X", "should not be here", null)] });
                return 0;
            default:
                return 4;
        }
    }

    private static WorkerRunResponse Valid(WorkerRunRequest request) => new WorkerRunResponse(
        WorkerProtocol.Version,
        request.ExecutionId,
        request.SourceRevision,
        ExecutionOutcome.Success,
        [],
        new ScalarNode("System.Int32", "1"),
        CapturedText.Empty,
        CapturedText.Empty,
        new WorkerHostInfo("fault", "fault", "fault", "0", "fault", Directory.GetCurrentDirectory()));

    private static ResultNode Deep(int depth)
    {
        ResultNode node = new ScalarNode("System.Int32", "1");
        for (int level = 0; level < depth; level++)
        {
            node = new SequenceNode("System.Object[]", [node], null);
        }

        return node;
    }

    private static byte[] Prefix(int length)
    {
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, length);
        return header;
    }

    private static Task Send(Stream output, WorkerRunResponse response) => FrameCodec.WriteAsync(output, response, WorkerProtocol.MaxMessageBytes);

    private static async Task Raw(Stream output, byte[] bytes)
    {
        await output.WriteAsync(bytes);
        await output.FlushAsync();
    }

    private static async Task FloodStderr()
    {
        // 20 MB. A supervisor that does not read the error stream blocks here, and the run never ends.
        using Stream error = Console.OpenStandardError();
        byte[] block = new byte[64 * 1024];
        Array.Fill(block, (byte)'e');
        for (int index = 0; index < 320; index++)
        {
            await error.WriteAsync(block);
        }
    }

    private static void StartChild(string? pidFile)
    {
        ProcessStartInfo info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c ping -n 300 127.0.0.1 > NUL")
            : new ProcessStartInfo("sleep", "300");
        info.UseShellExecute = false;
        Process child = Process.Start(info)!;
        if (pidFile is not null)
        {
            File.WriteAllText(pidFile, child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
