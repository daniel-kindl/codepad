using Codepad.Application.Experiments;
using Codepad.Domain.Experiments;

namespace Codepad.Tests.Experiments;

/// <summary>An in-memory repository for session tests. A test can hold a write, make it fail, or count the writes.</summary>
internal sealed class FakeExperimentRepository : IExperimentRepository
{
    private readonly Dictionary<ExperimentId, Experiment> stored = [];
    private TaskCompletionSource? gate;

    /// <summary>Gets the revisions of the writes that committed, in order.</summary>
    public List<long> CommittedRevisions { get; } = [];

    /// <summary>Gets or sets the exception that the next writes throw. Null means the writes work.</summary>
    public ExperimentStorageException? Failure { get; set; }

    /// <summary>Gets the number of writes that started.</summary>
    public int StartedWrites { get; private set; }

    /// <summary>Holds each write that starts until <see cref="Release"/>.</summary>
    public void Hold() => gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Lets the held writes continue.</summary>
    public void Release() => gate?.SetResult();

    public Experiment? Stored(ExperimentId id) => stored.GetValueOrDefault(id);

    public void Seed(Experiment experiment) => stored[experiment.Id] = experiment;

    public async Task<SaveResult> SaveAsync(Experiment experiment, CancellationToken cancellationToken = default)
    {
        StartedWrites++;
        if (gate is not null)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        if (Failure is not null)
        {
            throw Failure;
        }

        if (stored.TryGetValue(experiment.Id, out Experiment? current) && current.Revision >= experiment.Revision)
        {
            return SaveResult.Stale;
        }

        stored[experiment.Id] = experiment;
        CommittedRevisions.Add(experiment.Revision);
        return SaveResult.Committed;
    }

    public Task<Experiment?> GetAsync(ExperimentId id, CancellationToken cancellationToken = default) => Task.FromResult(stored.GetValueOrDefault(id));

    public Task<IReadOnlyList<Experiment>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Experiment>>([.. stored.Values]);
}
