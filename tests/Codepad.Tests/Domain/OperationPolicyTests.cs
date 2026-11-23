using Codepad.Domain.Operations;

namespace Codepad.Tests.Domain;

public sealed class OperationPolicyTests
{
    [Fact]
    public void CSharpDefaultIsExplicitOnlyWithTenSecondLimitAndNoGetters()
    {
        OperationPolicy policy = OperationPolicy.CSharpDefault;

        Assert.Equal(OperationKind.CSharp, policy.Kind);
        Assert.Equal(OperationTrigger.ExplicitOnly, policy.Trigger);
        Assert.Equal(TimeSpan.FromSeconds(10), policy.TimeLimit);
        Assert.Equal(1, policy.Version);
        Assert.False(policy.Results.InvokePropertyGetters);
    }

    [Fact]
    public void JsonCanBeLiveEligible()
    {
        OperationPolicy policy = new OperationPolicy(OperationKind.Json, 1, OperationTrigger.LiveEligible, TimeSpan.FromSeconds(1), ResultPolicy.Default);

        Assert.Equal(OperationTrigger.LiveEligible, policy.Trigger);
    }

    [Fact]
    public void CSharpCannotBeLiveEligible()
    {
        // No path runs C# code automatically. See ARCHITECTURE.md, "Worker trust boundary".
        Assert.Throws<ArgumentException>(() => new OperationPolicy(OperationKind.CSharp, 1, OperationTrigger.LiveEligible, TimeSpan.FromSeconds(10), ResultPolicy.Default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PolicyRejectsATimeLimitThatIsNotPositive(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OperationPolicy(OperationKind.Json, 1, OperationTrigger.LiveEligible, TimeSpan.FromMilliseconds(milliseconds), ResultPolicy.Default));
    }

    [Fact]
    public void PolicyRejectsAVersionBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OperationPolicy(OperationKind.Json, 0, OperationTrigger.LiveEligible, TimeSpan.FromSeconds(1), ResultPolicy.Default));
    }

    [Fact]
    public void PolicyRejectsUndefinedEnumValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OperationPolicy((OperationKind)7, 1, OperationTrigger.ExplicitOnly, TimeSpan.FromSeconds(1), ResultPolicy.Default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OperationPolicy(OperationKind.Json, 1, (OperationTrigger)7, TimeSpan.FromSeconds(1), ResultPolicy.Default));
    }

    [Fact]
    public void PolicyRejectsNullResultPolicy()
    {
        Assert.Throws<ArgumentNullException>(() => new OperationPolicy(OperationKind.Json, 1, OperationTrigger.ExplicitOnly, TimeSpan.FromSeconds(1), null!));
    }

    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, 1, 0, 1)]
    [InlineData(1, 1, 1, 0)]
    [InlineData(-1, 1, 1, 1)]
    public void ResultPolicyRejectsALimitBelowOne(int maxDepth, int maxItems, int maxTextLength, int maxNodes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResultPolicy(maxDepth, maxItems, maxTextLength, maxNodes, InvokePropertyGetters: false));
    }

    [Fact]
    public void ResultPolicyDefaultHasFiniteLimitsAndNoGetters()
    {
        ResultPolicy policy = ResultPolicy.Default;

        Assert.Equal(16, policy.MaxDepth);
        Assert.Equal(1_000, policy.MaxItems);
        Assert.Equal(10_000, policy.MaxTextLength);
        Assert.Equal(50_000, policy.MaxNodes);
        Assert.False(policy.InvokePropertyGetters);
    }
}
