namespace Codepad.Contracts;

/// <summary>
/// The constants of the worker protocol. The app starts one worker process for each run. It sends one
/// <see cref="WorkerRunRequest"/> and reads one <see cref="WorkerRunResponse"/>. Each message is one frame
/// (see <see cref="FrameCodec"/>) on the standard input and the standard output of the worker. The worker
/// replaces <c>Console.Out</c> and <c>Console.Error</c> for the user code, so the output of the user code
/// never reaches the protocol stream.
/// </summary>
public static class WorkerProtocol
{
    /// <summary>The version of the messages. A receiver rejects a message with another version.</summary>
    public static int Version => 1;

    /// <summary>The largest allowed frame payload, in bytes. A receiver rejects a larger frame before it reads the payload.</summary>
    public static int MaxMessageBytes => 4 * 1024 * 1024;

    /// <summary>The default limit for the captured text of one output stream, in UTF-16 code units.</summary>
    public static int DefaultMaxOutputChars => 100_000;

    /// <summary>The exit code of a worker that did not get a valid request. The code is a hint for diagnosis. It is never an outcome.</summary>
    public static int ExitBadRequest => 64;
}
