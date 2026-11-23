using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;

namespace Codepad.Domain.Executions;

/// <summary>
/// An immutable snapshot of what one execution runs: the source, the input and the policy at one experiment revision.
/// </summary>
/// <remarks>
/// The request owns copies of its values. A later edit to the experiment, or a change to the settings, does not
/// change it. The worker gets only the request. It never reads the editor or the database.
/// </remarks>
public sealed record ExecutionRequest
{
    private ExecutionRequest(ExecutionId id, ExperimentId experimentId, long experimentRevision, OperationPolicy policy, string source, string input, DateTimeOffset requestedAt)
    {
        Id = id;
        ExperimentId = experimentId;
        ExperimentRevision = experimentRevision;
        Policy = policy;
        Source = source;
        Input = input;
        RequestedAt = requestedAt;
    }

    /// <summary>Gets the unique ID of the request. The execution of the request has the same ID.</summary>
    public ExecutionId Id { get; }

    /// <summary>Gets the ID of the experiment that the snapshot comes from.</summary>
    public ExperimentId ExperimentId { get; }

    /// <summary>Gets the experiment revision that the snapshot comes from.</summary>
    public long ExperimentRevision { get; }

    /// <summary>Gets the operation kind, version, trigger, time limit and result limits.</summary>
    public OperationPolicy Policy { get; }

    /// <summary>Gets the source at the snapshot.</summary>
    public string Source { get; }

    /// <summary>Gets the input at the snapshot.</summary>
    public string Input { get; }

    /// <summary>Gets the time of the request.</summary>
    public DateTimeOffset RequestedAt { get; }

    /// <summary>Takes a snapshot of an experiment for one execution.</summary>
    /// <param name="id">The ID of the new request.</param>
    /// <param name="experiment">The experiment at the revision to run.</param>
    /// <param name="policy">The policy for the run. Its kind must be the kind of the experiment.</param>
    /// <param name="requestedAt">The time of the request.</param>
    /// <returns>The request.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The policy is for a different operation kind.</exception>
    public static ExecutionRequest Snapshot(ExecutionId id, Experiment experiment, OperationPolicy policy, DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.Kind != experiment.Kind)
        {
            throw new ArgumentException($"The policy is for {policy.Kind}, but the experiment is {experiment.Kind}.", nameof(policy));
        }

        return new ExecutionRequest(id, experiment.Id, experiment.Revision, policy, experiment.Source, experiment.Input, requestedAt);
    }

    /// <summary>
    /// Makes a request to run this snapshot again. The new request has the same source, input, revision and policy.
    /// This request does not change.
    /// </summary>
    /// <param name="newId">The ID of the new request. It must differ from <see cref="Id"/>.</param>
    /// <param name="requestedAt">The time of the new request.</param>
    /// <returns>The new request.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="newId"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="newId"/> is the ID of this request.</exception>
    public ExecutionRequest Rerun(ExecutionId newId, DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(newId);
        if (newId == Id)
        {
            throw new ArgumentException("A rerun needs a new execution ID.", nameof(newId));
        }

        return new ExecutionRequest(newId, ExperimentId, ExperimentRevision, Policy, Source, Input, requestedAt);
    }
}
