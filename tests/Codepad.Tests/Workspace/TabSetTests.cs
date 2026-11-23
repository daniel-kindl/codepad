using Codepad.Application.Workspace;
using Codepad.Domain.Experiments;

namespace Codepad.Tests.Workspace;

public sealed class TabSetTests
{
    private readonly ExperimentId a = ExperimentId.New();
    private readonly ExperimentId b = ExperimentId.New();
    private readonly ExperimentId c = ExperimentId.New();

    [Fact]
    public void AnEmptySetHasNoActiveTab()
    {
        Assert.Empty(TabSet.Empty.Open);
        Assert.Null(TabSet.Empty.Active);
    }

    [Fact]
    public void OpeningATabAddsItAtTheEndAndActivatesIt()
    {
        TabSet set = TabSet.Empty.OpenTab(a).OpenTab(b);

        Assert.Equal([a, b], set.Open);
        Assert.Equal(b, set.Active);
    }

    [Fact]
    public void OpeningAnOpenExperimentFocusesTheExistingTabWithoutADuplicate()
    {
        TabSet set = TabSet.Empty.OpenTab(a).OpenTab(b).OpenTab(a);

        Assert.Equal([a, b], set.Open);
        Assert.Equal(a, set.Active);
    }

    [Fact]
    public void ClosingTheActiveTabActivatesTheRightNeighbourThenTheLeftOne()
    {
        TabSet set = TabSet.Empty.OpenTab(a).OpenTab(b).OpenTab(c).Activate(b);

        set = set.Close(b);
        Assert.Equal([a, c], set.Open);
        Assert.Equal(c, set.Active);

        set = set.Close(c);
        Assert.Equal(a, set.Active);

        set = set.Close(a);
        Assert.Empty(set.Open);
        Assert.Null(set.Active);
    }

    [Fact]
    public void ClosingAnotherTabKeepsTheActiveTab()
    {
        TabSet set = TabSet.Empty.OpenTab(a).OpenTab(b).OpenTab(c).Activate(a);

        Assert.Equal(a, set.Close(c).Active);
    }

    [Fact]
    public void ClosingOrActivatingAnUnknownTabChangesNothing()
    {
        TabSet set = TabSet.Empty.OpenTab(a);

        Assert.Equal(set, set.Close(b));
        Assert.Equal(set, set.Activate(b));
    }

    [Fact]
    public void MovingATabChangesTheOrderAndKeepsTheActiveTab()
    {
        TabSet set = TabSet.Empty.OpenTab(a).OpenTab(b).OpenTab(c).Activate(b);

        TabSet moved = set.Move(a, 2);

        Assert.Equal([b, c, a], moved.Open);
        Assert.Equal(b, moved.Active);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(99, 2)]
    public void MovingPastTheEndStopsAtTheEnd(int target, int expected)
    {
        TabSet set = TabSet.Empty.OpenTab(a).OpenTab(b).OpenTab(c);

        Assert.Equal(expected, set.Move(b, target).Open.ToList().IndexOf(b));
    }

    [Fact]
    public void NextAndPreviousCycleAroundTheEnds()
    {
        TabSet set = TabSet.Empty.OpenTab(a).OpenTab(b).OpenTab(c);

        Assert.Equal(a, set.Next().Active);
        Assert.Equal(b, set.Previous().Active);
        Assert.Equal(b, set.Next().Next().Active);
        Assert.Null(TabSet.Empty.Next().Active);
    }

    [Fact]
    public void RestoreKeepsTheOrderDropsUnknownAndDuplicateIdsAndRepairsTheActiveTab()
    {
        ExperimentId gone = ExperimentId.New();

        TabSet restored = TabSet.Restore([a, gone, b, a, c], gone, valid: id => id != gone);

        Assert.Equal([a, b, c], restored.Open);
        Assert.Equal(c, restored.Active);
    }

    [Fact]
    public void RestoreKeepsAValidActiveTab()
    {
        Assert.Equal(b, TabSet.Restore([a, b, c], b, _ => true).Active);
    }

    [Fact]
    public void ASetIsImmutable()
    {
        TabSet set = TabSet.Empty.OpenTab(a);

        _ = set.OpenTab(b);

        Assert.Equal([a], set.Open);
    }
}
