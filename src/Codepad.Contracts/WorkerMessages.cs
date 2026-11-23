using Codepad.Domain.Operations;
using Codepad.Domain.Results;
using Codepad.Domain.Executions;

namespace Codepad.Contracts;

/// <summary>The request to compile and run one snapshot of the source. The worker never reads the editor or the database.</summary>
/// <param name="ProtocolVersion">The version of the protocol. See <see cref="WorkerProtocol.Version"/>.</param>
/// <param name="ExecutionId">The ID of the execution. The response repeats it.</param>
/// <param name="SourceRevision">The revision of the experiment that the source comes from. The response repeats it.</param>
/// <param name="Source">The C# script text.</param>
/// <param name="Results">The limits for the converted value.</param>
/// <param name="MaxOutputChars">The limit for the captured text of each of stdout and stderr.</param>
public sealed record WorkerRunRequest(
    int ProtocolVersion,
    Guid ExecutionId,
    long SourceRevision,
    string Source,
    ResultPolicy Results,
    int MaxOutputChars);

/// <summary>Text that the worker captured from one output stream.</summary>
/// <param name="Text">The captured text. It is cut at the output limit.</param>
/// <param name="OriginalLength">The length of the text that the code wrote, when the worker cut the text. Otherwise null.</param>
public sealed record CapturedText(string Text, int? OriginalLength)
{
    /// <summary>Gets the capture of a stream that wrote nothing.</summary>
    public static CapturedText Empty { get; } = new CapturedText(string.Empty, null);

    /// <summary>Gets a value that tells whether the worker cut the text.</summary>
    public bool IsTruncated => OriginalLength is not null;
}

/// <summary>The facts about the host that ran the script. They make a run reproducible.</summary>
/// <param name="Runtime">The .NET runtime description.</param>
/// <param name="OperatingSystem">The operating system description.</param>
/// <param name="Architecture">The process architecture.</param>
/// <param name="WorkerVersion">The version of the worker assembly.</param>
/// <param name="Language">The C# language version of the script. See ARCHITECTURE.md, "C# script semantics".</param>
/// <param name="WorkingDirectory">The working directory of the script.</param>
public sealed record WorkerHostInfo(
    string Runtime,
    string OperatingSystem,
    string Architecture,
    string WorkerVersion,
    string Language,
    string WorkingDirectory);

/// <summary>The result of one run, from the worker. The outcome is one of Success, CompilationFailed and RuntimeFailed.</summary>
/// <param name="ProtocolVersion">The version of the protocol.</param>
/// <param name="ExecutionId">The ID from the request.</param>
/// <param name="SourceRevision">The source revision from the request. The diagnostic locations belong to this revision.</param>
/// <param name="Outcome">How the run ended.</param>
/// <param name="Diagnostics">The compiler diagnostics. For RuntimeFailed, one error diagnostic holds the exception.</param>
/// <param name="Value">The converted return value. Only Success has a value.</param>
/// <param name="Stdout">The captured standard output of the script.</param>
/// <param name="Stderr">The captured standard error of the script.</param>
/// <param name="Host">The facts about the host.</param>
public sealed record WorkerRunResponse(
    int ProtocolVersion,
    Guid ExecutionId,
    long SourceRevision,
    ExecutionOutcome Outcome,
    IReadOnlyList<Diagnostic> Diagnostics,
    ResultNode? Value,
    CapturedText Stdout,
    CapturedText Stderr,
    WorkerHostInfo Host);
