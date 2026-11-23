using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;

namespace Codepad.Tests.Domain;

public sealed class ExperimentTests
{
    private static readonly DateTimeOffset s_created = new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset s_edited = s_created.AddMinutes(5);

    [Fact]
    public void CreateStartsAtRevisionOneWithEmptyInput()
    {
        ExperimentId id = new ExperimentId(new Guid("0193a0c0-0000-7000-8000-000000000001"));

        Experiment experiment = Experiment.Create(id, OperationKind.CSharp, "Sum", "1 + 2", s_created);

        Assert.Equal(id, experiment.Id);
        Assert.Equal(OperationKind.CSharp, experiment.Kind);
        Assert.Equal("Sum", experiment.Title);
        Assert.Equal("1 + 2", experiment.Source);
        Assert.Equal(string.Empty, experiment.Input);
        Assert.Equal(1, experiment.Revision);
        Assert.Equal(s_created, experiment.CreatedAt);
        Assert.Equal(s_created, experiment.ModifiedAt);
    }

    [Fact]
    public void WithSourceReturnsNextRevisionAndKeepsTheOriginal()
    {
        Experiment original = NewExperiment();

        Experiment edited = original.WithSource("1 + 3", s_edited);

        Assert.Equal("1 + 3", edited.Source);
        Assert.Equal(2, edited.Revision);
        Assert.Equal(s_edited, edited.ModifiedAt);
        Assert.Equal(s_created, edited.CreatedAt);
        Assert.Equal("1 + 2", original.Source);
        Assert.Equal(1, original.Revision);
    }

    [Fact]
    public void WithTitleReturnsNextRevision()
    {
        Experiment edited = NewExperiment().WithTitle("Renamed", s_edited);

        Assert.Equal("Renamed", edited.Title);
        Assert.Equal(2, edited.Revision);
    }

    [Fact]
    public void WithInputReturnsNextRevision()
    {
        Experiment edited = NewExperiment().WithInput("stdin text", s_edited);

        Assert.Equal("stdin text", edited.Input);
        Assert.Equal(2, edited.Revision);
    }

    [Fact]
    public void EditToTheSameValueKeepsTheRevision()
    {
        Experiment original = NewExperiment();

        Assert.Same(original, original.WithSource("1 + 2", s_edited));
        Assert.Same(original, original.WithTitle("Sum", s_edited));
        Assert.Same(original, original.WithInput(string.Empty, s_edited));
    }

    [Fact]
    public void IdentityStaysAcrossEdits()
    {
        Experiment original = NewExperiment();

        Experiment edited = original.WithSource("2", s_edited).WithTitle("Two", s_edited);

        Assert.Equal(original.Id, edited.Id);
        Assert.Equal(original.Kind, edited.Kind);
    }

    [Fact]
    public void CreateRejectsANullId()
    {
        Assert.Throws<ArgumentNullException>(() => Experiment.Create(null!, OperationKind.Json, "t", "{}", s_created));
    }

    [Fact]
    public void CreateRejectsAnUndefinedKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Experiment.Create(ExperimentId.New(), (OperationKind)99, "t", "{}", s_created));
    }

    [Fact]
    public void CreateAndEditsRejectNullText()
    {
        Experiment experiment = NewExperiment();

        Assert.Throws<ArgumentNullException>(() => Experiment.Create(ExperimentId.New(), OperationKind.Json, null!, "{}", s_created));
        Assert.Throws<ArgumentNullException>(() => Experiment.Create(ExperimentId.New(), OperationKind.Json, "t", null!, s_created));
        Assert.Throws<ArgumentNullException>(() => experiment.WithSource(null!, s_edited));
        Assert.Throws<ArgumentNullException>(() => experiment.WithTitle(null!, s_edited));
        Assert.Throws<ArgumentNullException>(() => experiment.WithInput(null!, s_edited));
    }

    [Fact]
    public void ExperimentIdRejectsTheEmptyGuid()
    {
        Assert.Throws<ArgumentException>(() => new ExperimentId(Guid.Empty));
    }

    [Fact]
    public void ExperimentIdsWithTheSameValueAreEqual()
    {
        Guid value = new Guid("0193a0c0-0000-7000-8000-000000000002");

        Assert.Equal(new ExperimentId(value), new ExperimentId(value));
        Assert.NotEqual(ExperimentId.New(), ExperimentId.New());
    }

    private static Experiment NewExperiment() =>
        Experiment.Create(new ExperimentId(new Guid("0193a0c0-0000-7000-8000-000000000001")), OperationKind.CSharp, "Sum", "1 + 2", s_created);
}
