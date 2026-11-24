using Codepad.Application.Execution;
using Codepad.Application.Experiments;
using Codepad.Application.History;
using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Tests.Experiments;
using Codepad.Tests.History;

namespace Codepad.Tests.Catalog;

public sealed class ExperimentCatalogTests
{
    private readonly FakeExperimentRepository repository = new FakeExperimentRepository();
    private readonly ManualTimeProvider clock = new ManualTimeProvider();

    private ExperimentCatalog Catalog() => new ExperimentCatalog(repository, clock);

    [Fact]
    public async Task CreateStoresANewExperimentAtRevisionOne()
    {
        Experiment created = await Catalog().CreateAsync(OperationKind.Json, "  Payload  ", "{}", TestContext.Current.CancellationToken);

        Assert.Equal("Payload", created.Title);
        Assert.Equal(1, created.Revision);
        Assert.Equal(OperationKind.Json, created.Kind);
        Assert.Equal(created, repository.Stored(created.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateRefusesAnEmptyTitle(string title)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Catalog().CreateAsync(OperationKind.CSharp, title, "1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateRefusesATitleThatIsTooLong()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Catalog().CreateAsync(OperationKind.CSharp, new string('x', ExperimentRules.MaxTitleLength + 1), "1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateChangesTheSourceAndTheTitleAndRaisesTheRevision()
    {
        ExperimentCatalog catalog = Catalog();
        Experiment created = await catalog.CreateAsync(OperationKind.CSharp, "Old", "1", TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));

        CatalogUpdate update = await catalog.UpdateAsync(created.Id, "New", "2", null, TestContext.Current.CancellationToken);

        Assert.Equal(CatalogStatus.Updated, update.Status);
        Assert.Equal("New", update.Experiment!.Title);
        Assert.Equal("2", update.Experiment.Source);
        Assert.True(update.Experiment.Revision > created.Revision);
        Assert.Equal(update.Experiment, repository.Stored(created.Id));
    }

    [Fact]
    public async Task UpdateOfOnlyTheTitleKeepsTheSource()
    {
        ExperimentCatalog catalog = Catalog();
        Experiment created = await catalog.CreateAsync(OperationKind.CSharp, "Old", "keep me", TestContext.Current.CancellationToken);

        CatalogUpdate update = await catalog.UpdateAsync(created.Id, "New", null, null, TestContext.Current.CancellationToken);

        Assert.Equal("keep me", update.Experiment!.Source);
    }

    [Fact]
    public async Task UpdateOfAMissingExperimentIsNotFound()
    {
        CatalogUpdate update = await Catalog().UpdateAsync(ExperimentId.New(), "x", null, null, TestContext.Current.CancellationToken);

        Assert.Equal(CatalogStatus.NotFound, update.Status);
        Assert.Null(update.Experiment);
    }

    [Fact]
    public async Task AnExpectedRevisionThatDoesNotMatchIsAConflictAndNothingChanges()
    {
        ExperimentCatalog catalog = Catalog();
        Experiment created = await catalog.CreateAsync(OperationKind.CSharp, "T", "1", TestContext.Current.CancellationToken);

        CatalogUpdate update = await catalog.UpdateAsync(created.Id, null, "2", 7, TestContext.Current.CancellationToken);

        Assert.Equal(CatalogStatus.Conflict, update.Status);
        Assert.Equal(created, repository.Stored(created.Id));
        Assert.Equal(created.Revision, update.Experiment!.Revision);
    }

    [Fact]
    public async Task AWriteThatLosesToANewerRevisionIsAConflictNotALostWrite()
    {
        ExperimentCatalog catalog = Catalog();
        Experiment created = await catalog.CreateAsync(OperationKind.CSharp, "T", "1", TestContext.Current.CancellationToken);

        // The desktop app saved revision 2 between the read of the catalog and its write.
        RacingRepository racing = new RacingRepository(repository, created.WithSource("desktop edit", clock.GetUtcNow()));
        CatalogUpdate update = await new ExperimentCatalog(racing, clock).UpdateAsync(created.Id, null, "cli edit", null, TestContext.Current.CancellationToken);

        Assert.Equal(CatalogStatus.Conflict, update.Status);
        Assert.Equal("desktop edit", repository.Stored(created.Id)?.Source);
    }

    [Fact]
    public async Task ChangingTheTitleAndTheSourceTogetherRaisesTheRevisionByOne()
    {
        ExperimentCatalog catalog = Catalog();
        Experiment created = await catalog.CreateAsync(OperationKind.CSharp, "T", "1", TestContext.Current.CancellationToken);

        CatalogUpdate update = await catalog.UpdateAsync(created.Id, "New title", "2", null, TestContext.Current.CancellationToken);

        Assert.Equal(created.Revision + 1, update.Experiment!.Revision);
    }

    [Fact]
    public async Task ATwoFieldWriteThatLosesToAConcurrentRevisionIsAConflictNotAnOverwrite()
    {
        ExperimentCatalog catalog = Catalog();
        Experiment created = await catalog.CreateAsync(OperationKind.CSharp, "T", "1", TestContext.Current.CancellationToken);
        RacingRepository racing = new RacingRepository(repository, created.WithSource("desktop edit", clock.GetUtcNow()));

        CatalogUpdate update = await new ExperimentCatalog(racing, clock).UpdateAsync(created.Id, "cli title", "cli source", null, TestContext.Current.CancellationToken);

        Assert.Equal(CatalogStatus.Conflict, update.Status);
        Experiment stored = repository.Stored(created.Id)!;
        Assert.Equal("desktop edit", stored.Source);
        Assert.Equal("T", stored.Title);
    }

    [Fact]
    public async Task UpdateWithNothingNewIsUpdatedAndDoesNotRaiseTheRevision()
    {
        ExperimentCatalog catalog = Catalog();
        Experiment created = await catalog.CreateAsync(OperationKind.CSharp, "T", "1", TestContext.Current.CancellationToken);

        CatalogUpdate update = await catalog.UpdateAsync(created.Id, "T", "1", null, TestContext.Current.CancellationToken);

        Assert.Equal(CatalogStatus.Updated, update.Status);
        Assert.Equal(created.Revision, update.Experiment!.Revision);
    }

    [Fact]
    public async Task GetReturnsTheStoredExperimentOrNull()
    {
        ExperimentCatalog catalog = Catalog();
        Experiment created = await catalog.CreateAsync(OperationKind.CSharp, "T", "1", TestContext.Current.CancellationToken);

        Assert.Equal(created, await catalog.GetAsync(created.Id, TestContext.Current.CancellationToken));
        Assert.Null(await catalog.GetAsync(ExperimentId.New(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunNewCreatesTheExperimentRunsItAndStoresARecord()
    {
        FakeRunner runner = new FakeRunner();
        FakeHistory history = new FakeHistory();
        ExperimentRunService service = new ExperimentRunService(Catalog(), new RunHistoryService(runner, history, clock));

        ExperimentRunResult result = await service.RunNewAsync("Snippet", "1 + 1", TestContext.Current.CancellationToken);

        Assert.Equal(CatalogStatus.Updated, result.Status);
        Assert.Equal("1 + 1", Assert.Single(runner.Requests).Source);
        Assert.Equal(result.Experiment!.Id, result.Run!.Record.Request.ExperimentId);
        Assert.Single(history.Added);
        Assert.NotNull(repository.Stored(result.Experiment.Id));
    }

    [Fact]
    public async Task RunExistingRunsTheStoredSourceWithoutChangingIt()
    {
        FakeRunner runner = new FakeRunner();
        ExperimentCatalog catalog = Catalog();
        Experiment created = await catalog.CreateAsync(OperationKind.CSharp, "T", "stored source", TestContext.Current.CancellationToken);
        ExperimentRunService service = new ExperimentRunService(catalog, new RunHistoryService(runner, new FakeHistory(), clock));

        ExperimentRunResult result = await service.RunExistingAsync(created.Id, null, null, TestContext.Current.CancellationToken);

        Assert.Equal("stored source", Assert.Single(runner.Requests).Source);
        Assert.Equal(created, repository.Stored(created.Id));
    }

    [Fact]
    public async Task RunExistingWithNewSourceStoresItThenRunsThatRevision()
    {
        FakeRunner runner = new FakeRunner();
        ExperimentCatalog catalog = Catalog();
        Experiment created = await catalog.CreateAsync(OperationKind.CSharp, "T", "old", TestContext.Current.CancellationToken);
        ExperimentRunService service = new ExperimentRunService(catalog, new RunHistoryService(runner, new FakeHistory(), clock));

        ExperimentRunResult result = await service.RunExistingAsync(created.Id, "new", null, TestContext.Current.CancellationToken);

        Assert.Equal("new", Assert.Single(runner.Requests).Source);
        Assert.Equal("new", repository.Stored(created.Id)?.Source);
        Assert.Equal(result.Experiment!.Revision, result.Run!.Record.Request.ExperimentRevision);
    }

    [Fact]
    public async Task RunOfAJsonExperimentOrAMissingOneRunsNothing()
    {
        FakeRunner runner = new FakeRunner();
        ExperimentCatalog catalog = Catalog();
        Experiment json = await catalog.CreateAsync(OperationKind.Json, "J", "{}", TestContext.Current.CancellationToken);
        ExperimentRunService service = new ExperimentRunService(catalog, new RunHistoryService(runner, new FakeHistory(), clock));

        Assert.Equal(CatalogStatus.WrongKind, (await service.RunExistingAsync(json.Id, null, null, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(CatalogStatus.NotFound, (await service.RunExistingAsync(ExperimentId.New(), null, null, TestContext.Current.CancellationToken)).Status);
        Assert.Empty(runner.Requests);
    }

    private sealed class RacingRepository(FakeExperimentRepository inner, Experiment concurrent) : IExperimentRepository
    {
        private bool raced;

        public async Task<SaveResult> SaveAsync(Experiment experiment, CancellationToken cancellationToken = default)
        {
            if (!raced)
            {
                raced = true;
                await inner.SaveAsync(concurrent, cancellationToken);
            }

            return await inner.SaveAsync(experiment, cancellationToken);
        }

        public Task<Experiment?> GetAsync(ExperimentId id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);

        public Task<IReadOnlyList<Experiment>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
    }

    private sealed class FakeRunner : ICSharpRunner
    {
        public List<ExecutionRequest> Requests { get; } = [];

        public Task<RunReport> RunAsync(ExecutionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(RunSamples.Report(request, ExecutionOutcome.Success));
        }
    }

    private sealed class FakeHistory : IRunHistoryRepository
    {
        public List<RunRecord> Added { get; } = [];

        public Task AddAsync(RunRecord record, CancellationToken cancellationToken = default)
        {
            Added.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RunSummary>> ListAsync(ExperimentId experimentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RunRecord?> GetAsync(ExecutionId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> ClearAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
