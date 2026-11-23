using System.Text.Json.Serialization;

namespace Codepad.Domain.Executions;

/// <summary>
/// How a completed execution ended. A process exit code is not an outcome: the supervisor maps what happened to
/// one of these values.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExecutionOutcome>))]
public enum ExecutionOutcome
{
    /// <summary>The operation completed. The result can hold a value.</summary>
    Success,

    /// <summary>The source did not compile or parse. User code did not run. The result has at least one error diagnostic.</summary>
    CompilationFailed,

    /// <summary>User code ran and threw an exception.</summary>
    RuntimeFailed,

    /// <summary>The user or the caller stopped the execution.</summary>
    Canceled,

    /// <summary>The execution went over the time limit of its policy.</summary>
    TimedOut,

    /// <summary>The worker crashed, exited early or sent a message that is not valid.</summary>
    WorkerFailed,
}
