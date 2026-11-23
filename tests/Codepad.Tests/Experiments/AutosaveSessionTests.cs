using Codepad.Application.Experiments;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;

namespace Codepad.Tests.Experiments;

public sealed class AutosaveSessionTests
{
    private static readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(500);
    private static readonly DateTimeOffset _clock = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ManualTimeProvider time = new ManualTimeProvider();
    private readonly FakeExperimentRepository repository = new FakeExperimentRepository();
    private readonly Experiment opened = Experiment.Create(ExperimentId.New(), OperationKind.CSharp, "Scratch", "Console.WriteLine(1);", _clock);

    private AutosaveSession OpenStored()
    {
        repository.Seed(opened);
        return AutosaveSession.Open(repository, opened, time, _debounce);
    }

    private static Experiment Typed(Experiment from, string source) => from.WithSource(source, _clock.AddSeconds(1));

    [Fact]
    public void OpenedExperimentIsCleanAndWritesNothing()
    {
        AutosaveSession session = OpenStored();

        time.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(SaveState.Clean, session.State);
        Assert.Equal(0, repository.StartedWrites);
    }

    [Fact]
    public void RapidTypingWritesOnlyTheFinalRevisionOnce()
    {
        AutosaveSession session = OpenStored();
        Experiment current = opened;
        for (int i = 0; i < 50; i++)
        {
            current = Typed(current, $"Console.WriteLine({i});");
            session.Edit(current);
            time.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(0, repository.StartedWrites);
        Assert.Equal(SaveState.Dirty, session.State);

        time.Advance(_debounce);

        Assert.Equal([current.Revision], repository.CommittedRevisions);
        Assert.Equal(current, repository.Stored(opened.Id));
        Assert.Equal(SaveState.Saved, session.State);
    }

    [Fact]
    public void EditsAfterIdlePeriodsWriteEachCommittedRevisionOnce()
    {
        AutosaveSession session = OpenStored();
        Experiment first = Typed(opened, "a");
        Experiment second = Typed(first, "b");

        session.Edit(first);
        time.Advance(_debounce);
        session.Edit(second);
        time.Advance(_debounce);

        Assert.Equal([first.Revision, second.Revision], repository.CommittedRevisions);
        Assert.Equal(SaveState.Saved, session.State);
    }

    [Fact]
    public async Task FlushWritesAtOnceWithoutWaitingForTheIdlePeriod()
    {
        AutosaveSession session = OpenStored();
        Experiment edited = Typed(opened, "flush me");
        session.Edit(edited);

        await session.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(edited, repository.Stored(opened.Id));
        Assert.Equal(SaveState.Saved, session.State);

        time.Advance(_debounce);
        Assert.Equal(1, repository.StartedWrites);
    }

    [Fact]
    public async Task StateIsSavingAndNeverSavedBeforeTheCommit()
    {
        AutosaveSession session = OpenStored();
        session.Edit(Typed(opened, "slow"));
        repository.Hold();

        Task flush = session.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SaveState.Saving, session.State);
        repository.Release();
        await flush;
        Assert.Equal(SaveState.Saved, session.State);
    }

    [Fact]
    public async Task EditDuringAWriteStaysDirtyAndSavesTheNewerRevisionAfterwards()
    {
        AutosaveSession session = OpenStored();
        Experiment first = Typed(opened, "first");
        Experiment second = Typed(first, "second");
        session.Edit(first);
        repository.Hold();
        Task flush = session.FlushAsync(TestContext.Current.CancellationToken);

        session.Edit(second);
        repository.Release();
        await flush;

        Assert.Equal(second, repository.Stored(opened.Id));
        Assert.Equal(SaveState.Saved, session.State);
        Assert.Equal([first.Revision, second.Revision], repository.CommittedRevisions);
    }

    [Fact]
    public async Task FailureKeepsTheEditedBufferAndReportsTheReason()
    {
        AutosaveSession session = OpenStored();
        Experiment edited = Typed(opened, "unsaved work");
        repository.Failure = new ExperimentStorageException("disk_full");
        session.Edit(edited);

        time.Advance(_debounce);
        await session.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SaveState.Error, session.State);
        Assert.Equal("disk_full", session.ErrorReason);
        Assert.Equal(edited, session.Current);
        Assert.Equal(opened, repository.Stored(opened.Id));
    }

    [Fact]
    public async Task RetryAfterAFailureSavesTheBufferAndClearsTheError()
    {
        AutosaveSession session = OpenStored();
        Experiment edited = Typed(opened, "unsaved work");
        repository.Failure = new ExperimentStorageException("read_only");
        session.Edit(edited);
        await session.FlushAsync(TestContext.Current.CancellationToken);

        repository.Failure = null;
        await session.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SaveState.Saved, session.State);
        Assert.Null(session.ErrorReason);
        Assert.Equal(edited, repository.Stored(opened.Id));
    }

    [Fact]
    public void FailureInTheTimerDoesNotThrowAndDoesNotRetryByItself()
    {
        AutosaveSession session = OpenStored();
        repository.Failure = new ExperimentStorageException("busy");
        session.Edit(Typed(opened, "x"));

        time.Advance(_debounce);
        time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(SaveState.Error, session.State);
        Assert.Equal(1, repository.StartedWrites);
    }

    [Fact]
    public void EditAfterAFailureSavesOnTheNextIdlePeriod()
    {
        AutosaveSession session = OpenStored();
        repository.Failure = new ExperimentStorageException("busy");
        Experiment first = Typed(opened, "x");
        session.Edit(first);
        time.Advance(_debounce);
        repository.Failure = null;
        Experiment second = Typed(first, "y");

        session.Edit(second);
        time.Advance(_debounce);

        Assert.Equal(SaveState.Saved, session.State);
        Assert.Equal(second, repository.Stored(opened.Id));
    }

    [Fact]
    public async Task StaleWriteReportsAnErrorAndKeepsTheBuffer()
    {
        AutosaveSession session = OpenStored();
        Experiment edited = Typed(opened, "mine");
        repository.Seed(opened.WithSource("someone else", _clock.AddSeconds(5)).WithTitle("t", _clock.AddSeconds(5)).WithInput("i", _clock.AddSeconds(5)));
        session.Edit(edited);

        await session.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SaveState.Error, session.State);
        Assert.Equal("stale_revision", session.ErrorReason);
        Assert.Equal(edited, session.Current);
    }

    [Fact]
    public void StartNewWritesTheNewExperimentAfterTheIdlePeriod()
    {
        AutosaveSession session = AutosaveSession.StartNew(repository, opened, time, _debounce);

        Assert.Equal(SaveState.Dirty, session.State);
        time.Advance(_debounce);

        Assert.Equal(opened, repository.Stored(opened.Id));
        Assert.Equal(SaveState.Saved, session.State);
    }

    [Fact]
    public void RenameSavesLikeAnyOtherEdit()
    {
        AutosaveSession session = OpenStored();
        Experiment renamed = opened.WithTitle("Renamed", _clock.AddSeconds(1));

        session.Edit(renamed);
        time.Advance(_debounce);

        Assert.Equal("Renamed", repository.Stored(opened.Id)?.Title);
    }

    [Fact]
    public void StateChangesAreReportedInOrder()
    {
        AutosaveSession session = OpenStored();
        List<SaveState> seen = [];
        session.StateChanged += () => seen.Add(session.State);

        session.Edit(Typed(opened, "x"));
        time.Advance(_debounce);

        Assert.Equal([SaveState.Dirty, SaveState.Saving, SaveState.Saved], seen);
    }

    [Fact]
    public void EditOfAnotherExperimentIsRejected()
    {
        AutosaveSession session = OpenStored();
        Experiment other = Experiment.Create(ExperimentId.New(), OperationKind.Json, "o", "{}", _clock).WithSource("[]", _clock.AddSeconds(1));

        Assert.Throws<ArgumentException>(() => session.Edit(other));
    }

    [Fact]
    public void EditWithAnOlderRevisionIsRejected()
    {
        AutosaveSession session = OpenStored();
        Experiment first = Typed(opened, "a");
        session.Edit(Typed(first, "b"));

        Assert.Throws<ArgumentException>(() => session.Edit(first));
    }

    [Fact]
    public async Task DisposeStopsThePendingWrite()
    {
        AutosaveSession session = OpenStored();
        session.Edit(Typed(opened, "x"));

        await session.DisposeAsync();
        time.Advance(_debounce);

        Assert.Equal(0, repository.StartedWrites);
    }
    [Fact]
    public async Task CancelledFlushStillSavesTheEditsAfterTheNextIdlePeriod()
    {
        AutosaveSession session = OpenStored();
        Experiment edited = Typed(opened, "keep me");
        session.Edit(edited);
        repository.Hold();
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        Task flush = session.FlushAsync(cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flush);
        Assert.Equal(SaveState.Dirty, session.State);

        repository.Release();
        time.Advance(_debounce);

        Assert.Equal(edited, repository.Stored(opened.Id));
        Assert.Equal(SaveState.Saved, session.State);
    }

    [Fact]
    public async Task CancelledWaitForTheWriteGateStillSavesTheEditsAfterTheNextIdlePeriod()
    {
        AutosaveSession session = OpenStored();
        session.Edit(Typed(opened, "first"));
        repository.Hold();
        Task running = session.FlushAsync(TestContext.Current.CancellationToken);
        Experiment second = Typed(session.Current, "second");
        session.Edit(second);
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        Task waiting = session.FlushAsync(cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        repository.Release();
        await running;

        Assert.Equal(second, repository.Stored(opened.Id));
    }

    [Fact]
    public async Task SubscriberThatThrowsDoesNotBreakSaving()
    {
        AutosaveSession session = OpenStored();
        session.StateChanged += () => throw new InvalidOperationException("faulty subscriber");
        Experiment edited = Typed(opened, "x");

        session.Edit(edited);
        await session.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SaveState.Saved, session.State);
        Assert.Equal(edited, repository.Stored(opened.Id));
    }

    [Fact]
    public async Task EditAfterDisposeKeepsTheBufferButWritesNothing()
    {
        AutosaveSession session = OpenStored();
        await session.DisposeAsync();
        Experiment edited = Typed(opened, "late edit");

        session.Edit(edited);
        time.Advance(_debounce);

        Assert.Equal(edited, session.Current);
        Assert.Equal(SaveState.Dirty, session.State);
        Assert.Equal(0, repository.StartedWrites);
    }
}
