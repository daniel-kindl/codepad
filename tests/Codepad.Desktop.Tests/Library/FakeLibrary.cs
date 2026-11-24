using Codepad.Application.Experiments;
using Codepad.Application.Library;
using Codepad.Domain.Experiments;
using Codepad.Tests.Experiments;

namespace Codepad.Desktop.Tests.Library;

/// <summary>A library search over an in-memory experiment store, with a gate and a failure switch for tests.</summary>
internal sealed class FakeLibrary(FakeExperimentRepository source) : ILibraryRepository
{
    private readonly TaskCompletionSource started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => started.Task;

    public TaskCompletionSource? Gate { get; set; }

    public ExperimentStorageException? Failure { get; set; }

    public async Task<LibraryPage> SearchAsync(LibraryQuery query, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource? gate = Gate;
        started.TrySetResult();
        if (Failure is not null)
        {
            throw Failure;
        }

        if (gate is not null)
        {
            await gate.Task.ConfigureAwait(false);
        }

        IReadOnlyList<Experiment> all = await source.ListAsync(cancellationToken).ConfigureAwait(false);
        List<Experiment> hits = [.. all
            .Where(item => (query.Kind is null || item.Kind == query.Kind)
                && (query.Text.Length == 0 || item.Title.Contains(query.Text, StringComparison.OrdinalIgnoreCase) || item.Source.Contains(query.Text, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.ModifiedAt)
            .ThenByDescending(item => item.Id.Value)];
        List<ExperimentSummary> page = [.. hits.Skip(query.Offset).Take(query.Limit).Select(item => new ExperimentSummary(item.Id, item.Kind, item.Title, item.Source, item.ModifiedAt))];
        return new LibraryPage(page, hits.Count > query.Offset + query.Limit, query.Offset + page.Count);
    }
}
