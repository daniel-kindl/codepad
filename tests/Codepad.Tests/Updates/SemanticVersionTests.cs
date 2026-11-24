using Codepad.Application.Updates;

namespace Codepad.Tests.Updates;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("0.1.0", 0, 1, 0)]
    [InlineData("10.20.30", 10, 20, 30)]
    [InlineData("1.2.3+build.5", 1, 2, 3)]
    public void AStableVersionParses(string text, int major, int minor, int patch)
    {
        Assert.True(SemanticVersion.TryParse(text, out SemanticVersion? version));
        Assert.Equal(major, version!.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.True(version.IsStable);
    }

    [Theory]
    [InlineData("1.2.3-rc.1")]
    [InlineData("1.2.3-alpha")]
    [InlineData("v2.0.0-beta.2+x")]
    public void APreReleaseParsesAndIsNotStable(string text)
    {
        Assert.True(SemanticVersion.TryParse(text, out SemanticVersion? version));
        Assert.False(version!.IsStable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("a.b.c")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("-1.2.3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3 ")]
    [InlineData("vv1.2.3")]
    [InlineData("99999999999.1.1")]
    public void BadTextIsRejected(string text)
    {
        Assert.False(SemanticVersion.TryParse(text, out _));
    }

    [Fact]
    public void NullIsRejected()
    {
        Assert.False(SemanticVersion.TryParse(null, out _));
    }

    [Theory]
    [InlineData("1.0.0", "2.0.0", -1)]
    [InlineData("1.2.0", "1.10.0", -1)]
    [InlineData("1.0.9", "1.0.10", -1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0+a", "1.0.0+b", 0)]
    [InlineData("1.0.0-rc.1", "1.0.0", -1)]
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.10", -1)]
    [InlineData("1.0.0-1", "1.0.0-alpha", -1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    public void VersionsOrderByTheSemVerRules(string left, string right, int expected)
    {
        SemanticVersion a = Parse(left);
        SemanticVersion b = Parse(right);

        Assert.Equal(expected, Math.Sign(a.CompareTo(b)));
        Assert.Equal(-expected, Math.Sign(b.CompareTo(a)));
    }

    [Fact]
    public void ToStringHasNoPrefixAndNoBuildMetadata()
    {
        Assert.Equal("1.2.3", Parse("v1.2.3+meta").ToString());
        Assert.Equal("1.2.3-rc.1", Parse("1.2.3-rc.1").ToString());
    }

    [Fact]
    public void TheInstalledVersionComesFromTheInformationalVersionWithoutTheCommit()
    {
        Assert.True(SemanticVersion.TryParse("0.1.0+2626c4fc0376c91c3a77d5cc103463d0f648d984", out SemanticVersion? version));
        Assert.Equal("0.1.0", version!.ToString());
    }

    private static SemanticVersion Parse(string text)
    {
        Assert.True(SemanticVersion.TryParse(text, out SemanticVersion? version));
        return version!;
    }
}
