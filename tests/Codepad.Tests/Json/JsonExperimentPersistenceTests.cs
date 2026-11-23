using Codepad.Application.Experiments;
using Codepad.Application.History;
using Codepad.Application.Json;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Infrastructure.Storage;
using Codepad.Tests.Experiments;

namespace Codepad.Tests.Json;

/// <summary>A JSON experiment uses the same autosave and storage as a C# experiment. It works offline.</summary>
public sealed class JsonExperimentPersistenceTests : IDisposable
{
    private readonly TempDatabase temp = new TempDatabase();

    public void Dispose() => temp.Dispose();

    [Fact]
    public async Task AJsonExperimentAutosavesAndRestoresAfterARestart()
    {
        ManualTimeProvider time = new ManualTimeProvider();
        SqliteExperimentRepository repository = new SqliteExperimentRepository(temp.Open());
        Experiment created = Experiment.Create(ExperimentId.New(), OperationKind.Json, "Payload", "{}", time.GetUtcNow());
        await using AutosaveSession session = AutosaveSession.StartNew(repository, created, time, AutosaveSession.DefaultDebounce);

        // Malformed text is saved exactly as typed. Saving never needs valid JSON.
        session.Edit(created.WithSource("{ \"a\": [1, 2,", time.GetUtcNow().AddSeconds(1)));
        await session.FlushAsync(TestContext.Current.CancellationToken);

        // A new repository over the same file is a restart.
        SqliteExperimentRepository restarted = new SqliteExperimentRepository(temp.Open());
        Experiment? restored = await restarted.GetAsync(created.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(restored);
        Assert.Equal(OperationKind.Json, restored.Kind);
        Assert.Equal("{ \"a\": [1, 2,", restored.Source);
        JsonEvaluation evaluation = new JsonEvaluator().Evaluate(restored.Source);
        Assert.False(evaluation.IsValid);
        Assert.Equal(new Codepad.Domain.Results.SourceLocation(1, 13), evaluation.Problem?.Location);
    }

    [Fact]
    public async Task EditingJsonCreatesNoRunHistory()
    {
        SqliteDatabase database = temp.Open();
        ManualTimeProvider time = new ManualTimeProvider();
        SqliteExperimentRepository repository = new SqliteExperimentRepository(database);
        Experiment created = Experiment.Create(ExperimentId.New(), OperationKind.Json, "Payload", "[1]", time.GetUtcNow());
        await using AutosaveSession session = AutosaveSession.StartNew(repository, created, time, AutosaveSession.DefaultDebounce);
        JsonEvaluator evaluator = new JsonEvaluator();

        for (int key = 0; key < 20; key++)
        {
            session.Edit(session.Current.WithSource("[" + new string('1', key + 1) + "]", time.GetUtcNow().AddSeconds(key)));
            Assert.True(evaluator.Evaluate(session.Current.Source).IsValid);
        }

        await session.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await new SqliteRunHistoryRepository(database).ListAsync(created.Id, TestContext.Current.CancellationToken));
    }
}
