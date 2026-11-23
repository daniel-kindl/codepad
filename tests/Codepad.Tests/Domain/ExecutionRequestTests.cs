using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;

namespace Codepad.Tests.Domain;

public sealed class ExecutionRequestTests
{
    private static readonly DateTimeOffset s_created = new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);
    private static readonly ExecutionId s_firstRun = new ExecutionId(new Guid("0193a0c0-0000-7000-8000-0000000000a1"));
    private static readonly ExecutionId s_secondRun = new ExecutionId(new Guid("0193a0c0-0000-7000-8000-0000000000a2"));

    [Fact]
    public void SnapshotCopiesTheExperimentAtItsRevision()
    {
        Experiment experiment = NewExperiment().WithInput("in", s_created.AddSeconds(1));

        ExecutionRequest request = ExecutionRequest.Snapshot(s_firstRun, experiment, OperationPolicy.CSharpDefault, s_created.AddSeconds(2));

        Assert.Equal(s_firstRun, request.Id);
        Assert.Equal(experiment.Id, request.ExperimentId);
        Assert.Equal(2, request.ExperimentRevision);
        Assert.Equal("1 + 2", request.Source);
        Assert.Equal("in", request.Input);
        Assert.Equal(OperationPolicy.CSharpDefault, request.Policy);
        Assert.Equal(TimeSpan.FromSeconds(10), request.Policy.TimeLimit);
        Assert.Equal(s_created.AddSeconds(2), request.RequestedAt);
    }

    [Fact]
    public void ChangingTheExperimentNeverEditsAnExistingRequest()
    {
        Experiment experiment = NewExperiment();
        ExecutionRequest request = ExecutionRequest.Snapshot(s_firstRun, experiment, OperationPolicy.CSharpDefault, s_created);

        Experiment edited = experiment.WithSource("40 + 2", s_created.AddMinutes(1)).WithTitle("Answer", s_created.AddMinutes(1));

        Assert.Equal(3, edited.Revision);
        Assert.Equal("1 + 2", request.Source);
        Assert.Equal(1, request.ExperimentRevision);
    }

    [Fact]
    public void TwoRunsWithEditedSourceKeepDifferentSnapshots()
    {
        Experiment experiment = NewExperiment();
        ExecutionRequest first = ExecutionRequest.Snapshot(s_firstRun, experiment, OperationPolicy.CSharpDefault, s_created);
        Experiment edited = experiment.WithSource("40 + 2", s_created.AddMinutes(1));

        ExecutionRequest second = ExecutionRequest.Snapshot(s_secondRun, edited, OperationPolicy.CSharpDefault, s_created.AddMinutes(2));

        Assert.Equal("1 + 2", first.Source);
        Assert.Equal("40 + 2", second.Source);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void SnapshotRejectsAPolicyForAnotherKind()
    {
        OperationPolicy jsonPolicy = new OperationPolicy(OperationKind.Json, 1, OperationTrigger.LiveEligible, TimeSpan.FromSeconds(1), ResultPolicy.Default);

        Assert.Throws<ArgumentException>(() => ExecutionRequest.Snapshot(s_firstRun, NewExperiment(), jsonPolicy, s_created));
    }

    [Fact]
    public void SnapshotRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => ExecutionRequest.Snapshot(null!, NewExperiment(), OperationPolicy.CSharpDefault, s_created));
        Assert.Throws<ArgumentNullException>(() => ExecutionRequest.Snapshot(s_firstRun, null!, OperationPolicy.CSharpDefault, s_created));
        Assert.Throws<ArgumentNullException>(() => ExecutionRequest.Snapshot(s_firstRun, NewExperiment(), null!, s_created));
    }

    [Fact]
    public void RerunCopiesTheSnapshotWithANewIdAndTime()
    {
        ExecutionRequest first = ExecutionRequest.Snapshot(s_firstRun, NewExperiment(), OperationPolicy.CSharpDefault, s_created);

        ExecutionRequest rerun = first.Rerun(s_secondRun, s_created.AddHours(1));

        Assert.Equal(s_secondRun, rerun.Id);
        Assert.Equal(s_created.AddHours(1), rerun.RequestedAt);
        Assert.Equal(first.Source, rerun.Source);
        Assert.Equal(first.Input, rerun.Input);
        Assert.Equal(first.ExperimentId, rerun.ExperimentId);
        Assert.Equal(first.ExperimentRevision, rerun.ExperimentRevision);
        Assert.Equal(first.Policy, rerun.Policy);
        Assert.Equal(s_firstRun, first.Id);
    }

    [Fact]
    public void RerunRejectsTheSameId()
    {
        ExecutionRequest first = ExecutionRequest.Snapshot(s_firstRun, NewExperiment(), OperationPolicy.CSharpDefault, s_created);

        ArgumentException error = Assert.Throws<ArgumentException>(() => first.Rerun(new ExecutionId(s_firstRun.Value), s_created));
        Assert.Equal("newId", error.ParamName);
    }

    [Fact]
    public void RerunRejectsANullId()
    {
        ExecutionRequest first = ExecutionRequest.Snapshot(s_firstRun, NewExperiment(), OperationPolicy.CSharpDefault, s_created);

        Assert.Throws<ArgumentNullException>(() => first.Rerun(null!, s_created));
    }

    [Fact]
    public void ExecutionIdRejectsTheEmptyGuid()
    {
        Assert.Throws<ArgumentException>(() => new ExecutionId(Guid.Empty));
    }

    private static Experiment NewExperiment() =>
        Experiment.Create(new ExperimentId(new Guid("0193a0c0-0000-7000-8000-000000000001")), OperationKind.CSharp, "Sum", "1 + 2", s_created);
}
