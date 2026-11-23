namespace Codepad.Domain.Executions;

/// <summary>Where an execution is in its lifecycle.</summary>
public enum ExecutionStatus
{
    /// <summary>The request exists. The operation has not started.</summary>
    Pending,

    /// <summary>The operation started.</summary>
    Running,

    /// <summary>The execution has a result. This status is final.</summary>
    Completed,
}

/// <summary>
/// One execution of a request, with an explicit lifecycle.
/// </summary>
/// <remarks>
/// <para>The allowed transitions:</para>
/// <list type="bullet">
/// <item><description>Pending to Running, with <see cref="Start"/>.</description></item>
/// <item><description>Running to Completed, with <see cref="Complete"/> and any outcome.</description></item>
/// <item><description>Pending to Completed, with <see cref="Complete"/> and only the <see cref="ExecutionOutcome.Canceled"/> outcome.</description></item>
/// </list>
/// <para>
/// Each other transition throws <see cref="InvalidOperationException"/>. An execution is immutable: a transition
/// returns a new execution. The request never changes.
/// </para>
/// </remarks>
public sealed class Execution
{
    private Execution(ExecutionRequest request, ExecutionStatus status, DateTimeOffset? startedAt, DateTimeOffset? completedAt, ExecutionResult? result)
    {
        Request = request;
        Status = status;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Result = result;
    }

    /// <summary>Gets the ID. It is the ID of the request.</summary>
    public ExecutionId Id => Request.Id;

    /// <summary>Gets the request snapshot.</summary>
    public ExecutionRequest Request { get; }

    /// <summary>Gets the lifecycle status.</summary>
    public ExecutionStatus Status { get; }

    /// <summary>Gets the start time, or null if the execution did not start.</summary>
    public DateTimeOffset? StartedAt { get; }

    /// <summary>Gets the completion time, or null if the execution is not complete.</summary>
    public DateTimeOffset? CompletedAt { get; }

    /// <summary>Gets the result, or null if the execution is not complete.</summary>
    public ExecutionResult? Result { get; }

    /// <summary>Makes a pending execution for a request.</summary>
    /// <param name="request">The request.</param>
    /// <returns>A pending execution.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public static Execution Create(ExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new Execution(request, ExecutionStatus.Pending, null, null, null);
    }

    /// <summary>Records that the operation started.</summary>
    /// <param name="startedAt">The start time.</param>
    /// <returns>A running execution.</returns>
    /// <exception cref="InvalidOperationException">The execution is not pending.</exception>
    public Execution Start(DateTimeOffset startedAt)
    {
        if (Status != ExecutionStatus.Pending)
        {
            throw new InvalidOperationException($"Execution {Id} cannot start: its status is {Status}, not Pending.");
        }

        return new Execution(Request, ExecutionStatus.Running, startedAt, null, null);
    }

    /// <summary>Records the result of the execution.</summary>
    /// <param name="result">The result.</param>
    /// <param name="completedAt">The completion time.</param>
    /// <returns>A completed execution.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The execution is complete, or it is pending and the outcome is not <see cref="ExecutionOutcome.Canceled"/>.
    /// </exception>
    public Execution Complete(ExecutionResult result, DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (Status == ExecutionStatus.Completed)
        {
            throw new InvalidOperationException($"Execution {Id} is already complete.");
        }

        if (Status == ExecutionStatus.Pending && result.Outcome != ExecutionOutcome.Canceled)
        {
            throw new InvalidOperationException($"Execution {Id} did not start, so it can complete only as Canceled, not as {result.Outcome}.");
        }

        return new Execution(Request, ExecutionStatus.Completed, StartedAt, completedAt, result);
    }
}
