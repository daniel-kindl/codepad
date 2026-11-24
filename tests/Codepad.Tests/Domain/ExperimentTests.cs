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
    public void EachEditKeepsTheOtherValues()
    {
        // CreatedAt, ModifiedAt and the edit time all differ, so a mix-up between them fails.
        Experiment original = Experiment.Restore(ExperimentId.New(), OperationKind.Json, "Title", "{}", "in", s_created, s_created.AddMinutes(1), 3);

        Experiment title = original.WithTitle("New", s_edited);
        Experiment source = original.WithSource("[]", s_edited);
        Experiment input = original.WithInput("out", s_edited);

        Assert.Equal(("New", "{}", "in"), (title.Title, title.Source, title.Input));
        Assert.Equal(("Title", "[]", "in"), (source.Title, source.Source, source.Input));
        Assert.Equal(("Title", "{}", "out"), (input.Title, input.Source, input.Input));
        Assert.All([title, source, input], edited =>
        {
            Assert.Equal(original.Id, edited.Id);
            Assert.Equal(original.Kind, edited.Kind);
            Assert.Equal(original.CreatedAt, edited.CreatedAt);
            Assert.Equal(s_edited, edited.ModifiedAt);
            Assert.Equal(4, edited.Revision);
        });
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

    [Theory]
    [InlineData("source")]
    [InlineData("title")]
    [InlineData("input")]
    public void EditWithAnEarlierClockTimeKeepsModifiedAt(string field)
    {
        // A clock change must not lose an edit, so the edit applies and ModifiedAt never goes back.
        Experiment original = NewExperiment();
        DateTimeOffset earlier = s_created.AddMinutes(-10);

        Experiment edited = field switch
        {
            "source" => original.WithSource("2", earlier),
            "title" => original.WithTitle("Two", earlier),
            _ => original.WithInput("stdin", earlier),
        };

        Assert.Equal(2, edited.Revision);
        Assert.Equal(s_created, edited.ModifiedAt);
    }

    [Fact]
    public void RestoreRebuildsAStoredExperiment()
    {
        ExperimentId id = ExperimentId.New();

        Experiment experiment = Experiment.Restore(id, OperationKind.Json, "Doc", "{}", "in", s_created, s_edited, 7);

        Assert.Equal(id, experiment.Id);
        Assert.Equal(OperationKind.Json, experiment.Kind);
        Assert.Equal("Doc", experiment.Title);
        Assert.Equal("{}", experiment.Source);
        Assert.Equal("in", experiment.Input);
        Assert.Equal(s_created, experiment.CreatedAt);
        Assert.Equal(s_edited, experiment.ModifiedAt);
        Assert.Equal(7, experiment.Revision);
        Assert.Equal(8, experiment.WithSource("[]", s_edited).Revision);
    }

    [Fact]
    public void RestoreAcceptsRevisionOneAndModifiedAtEqualToCreatedAt()
    {
        Experiment experiment = Experiment.Restore(ExperimentId.New(), OperationKind.Json, "t", "{}", "", s_created, s_created, 1);

        Assert.Equal(1, experiment.Revision);
        Assert.Equal(experiment.CreatedAt, experiment.ModifiedAt);
    }

    [Fact]
    public void RestoreRejectsValuesThatNoExperimentCanHave()
    {
        ExperimentId id = ExperimentId.New();

        Assert.Throws<ArgumentOutOfRangeException>(() => Experiment.Restore(id, OperationKind.Json, "t", "{}", "", s_created, s_edited, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Experiment.Restore(id, OperationKind.Json, "t", "{}", "", s_edited, s_created, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Experiment.Restore(id, (OperationKind)99, "t", "{}", "", s_created, s_edited, 1));
        Assert.Throws<ArgumentNullException>(() => Experiment.Restore(null!, OperationKind.Json, "t", "{}", "", s_created, s_edited, 1));
        Assert.Throws<ArgumentNullException>(() => Experiment.Restore(id, OperationKind.Json, null!, "{}", "", s_created, s_edited, 1));
        Assert.Throws<ArgumentNullException>(() => Experiment.Restore(id, OperationKind.Json, "t", null!, "", s_created, s_edited, 1));
        Assert.Throws<ArgumentNullException>(() => Experiment.Restore(id, OperationKind.Json, "t", "{}", null!, s_created, s_edited, 1));
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

    [Fact]
    public void WithChangesRaisesTheRevisionOnceForTwoChangesAndNotAtAllForNone()
    {
        Experiment original = Experiment.Create(ExperimentId.New(), OperationKind.CSharp, "t", "1", s_edited);

        Experiment both = original.WithChanges("new", "2", s_edited.AddMinutes(1));

        Assert.Equal(original.Revision + 1, both.Revision);
        Assert.Equal("new", both.Title);
        Assert.Equal("2", both.Source);
        Assert.Same(original, original.WithChanges(null, null, s_edited));
        Assert.Same(original, original.WithChanges("t", "1", s_edited));
    }
}
