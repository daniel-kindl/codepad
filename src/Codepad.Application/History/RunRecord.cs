using Codepad.Application.Execution;
using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;

namespace Codepad.Application.History;

/// <summary>
/// One completed run, as history keeps it. The record owns the request snapshot (source, input, revision and policy)
/// and the report. A later edit to the experiment does not change it.
/// </summary>
public sealed record RunRecord
{
    /// <summary>Initializes a new instance of the <see cref="RunRecord"/> class.</summary>
    /// <param name="request">The snapshot that ran.</param>
    /// <param name="completedAt">The time that the run ended. It cannot be before the request time.</param>
    /// <param name="report">The report of the run. It must belong to the request.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The report has another execution ID than the request.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="completedAt"/> is before the request time.</exception>
    public RunRecord(ExecutionRequest request, DateTimeOffset completedAt, RunReport report)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentOutOfRangeException.ThrowIfLessThan(completedAt, request.RequestedAt);
        if (report.ExecutionId != request.Id)
        {
            throw new ArgumentException("The report belongs to another execution than the request.", nameof(report));
        }

        Request = request;
        CompletedAt = completedAt;
        Report = report;
    }

    /// <summary>Gets the snapshot that ran.</summary>
    public ExecutionRequest Request { get; }

    /// <summary>Gets the time that the run ended.</summary>
    public DateTimeOffset CompletedAt { get; }

    /// <summary>Gets the report of the run.</summary>
    public RunReport Report { get; }

    /// <summary>Gets the time from the request to the end of the run.</summary>
    public TimeSpan Duration => CompletedAt - Request.RequestedAt;
}

/// <summary>A short description of one stored run. The list of history shows it. The summary has no source and no result.</summary>
/// <param name="Id">The ID of the execution.</param>
/// <param name="ExperimentId">The experiment that the run belongs to.</param>
/// <param name="ExperimentRevision">The experiment revision that ran.</param>
/// <param name="RequestedAt">The time of the request.</param>
/// <param name="CompletedAt">The time that the run ended.</param>
/// <param name="Outcome">How the run ended.</param>
public sealed record RunSummary(
    ExecutionId Id,
    ExperimentId ExperimentId,
    long ExperimentRevision,
    DateTimeOffset RequestedAt,
    DateTimeOffset CompletedAt,
    ExecutionOutcome Outcome)
{
    /// <summary>Gets the time from the request to the end of the run.</summary>
    public TimeSpan Duration => CompletedAt - RequestedAt;
}

/// <summary>The result of one run through <see cref="RunHistoryService"/>.</summary>
/// <param name="Record">The run. It is always there, also when history could not store it.</param>
/// <param name="StorageFailure">The log-safe reason when history could not store the record, or null when it did.</param>
public sealed record CompletedRun(RunRecord Record, string? StorageFailure);
