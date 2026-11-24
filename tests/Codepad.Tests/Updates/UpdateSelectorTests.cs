using System.Text;
using Codepad.Application.Updates;

namespace Codepad.Tests.Updates;

public sealed class UpdateSelectorTests
{
    private static SemanticVersion V(string text) => SemanticVersion.TryParse(text, out SemanticVersion? version) ? version! : throw new InvalidOperationException(text);

    private static UpdateManifest Manifest(string version, bool linux = true, bool windows = true)
    {
        System.Text.Json.Nodes.JsonObject json = UpdateManifestTests.Valid(version);
        System.Text.Json.Nodes.JsonArray assets = (System.Text.Json.Nodes.JsonArray)json["assets"]!;
        if (!windows)
        {
            assets.RemoveAt(1);
        }

        if (!linux)
        {
            assets.RemoveAt(0);
        }

        return UpdateManifestParser.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));
    }

    [Fact]
    public void ANewerStableReleaseWithAnAssetForThePlatformIsOffered()
    {
        UpdateSelection selection = UpdateSelector.Select(V("0.1.0"), Manifest("0.2.0"), "linux-x64");

        Assert.NotNull(selection.Offer);
        Assert.Equal("0.2.0", selection.Offer.Version.ToString());
        Assert.Equal("linux-x64", selection.Offer.Asset.Platform);
    }

    [Fact]
    public void EachPlatformGetsItsOwnAsset()
    {
        Assert.Equal("win-x64", UpdateSelector.Select(V("0.1.0"), Manifest("0.2.0"), "win-x64").Offer!.Asset.Platform);
    }

    [Theory]
    [InlineData("0.1.0")]
    [InlineData("0.0.9")]
    public void AnEqualOrOlderReleaseIsNotOffered(string released)
    {
        UpdateSelection selection = UpdateSelector.Select(V("0.1.0"), Manifest(released), "linux-x64");

        Assert.Null(selection.Offer);
        Assert.Equal(UpdateRejection.NotNewer, selection.Rejection);
    }

    [Fact]
    public void APreReleaseIsNotOfferedEvenWhenItIsNewer()
    {
        UpdateSelection selection = UpdateSelector.Select(V("0.1.0"), Manifest("0.3.0-rc.1"), "linux-x64");

        Assert.Equal(UpdateRejection.PreRelease, selection.Rejection);
    }

    [Fact]
    public void AReleaseWithoutAnAssetForThePlatformIsNotOffered()
    {
        UpdateSelection selection = UpdateSelector.Select(V("0.1.0"), Manifest("0.2.0", linux: false), "linux-x64");

        Assert.Equal(UpdateRejection.NoAssetForPlatform, selection.Rejection);
    }

    [Fact]
    public void AnInstalledPreReleaseIsUpdatedToTheStableRelease()
    {
        Assert.NotNull(UpdateSelector.Select(V("0.2.0-rc.1"), Manifest("0.2.0"), "linux-x64").Offer);
    }

    [Fact]
    public void ThePublishedDateNeverOrdersVersions()
    {
        // An older version with a later date is still older.
        UpdateManifest older = Manifest("0.0.5");

        Assert.Null(UpdateSelector.Select(V("0.1.0"), older with { PublishedAt = DateTimeOffset.UtcNow.AddYears(5) }, "linux-x64").Offer);
    }
}

public sealed class UpdatePlatformTests
{
    [Theory]
    [InlineData(true, false, System.Runtime.InteropServices.Architecture.X64, "win-x64")]
    [InlineData(false, true, System.Runtime.InteropServices.Architecture.X64, "linux-x64")]
    [InlineData(false, true, System.Runtime.InteropServices.Architecture.Arm64, "linux-arm64")]
    [InlineData(true, false, System.Runtime.InteropServices.Architecture.X86, "win-x86")]
    [InlineData(false, false, System.Runtime.InteropServices.Architecture.X64, "other-x64")]
    public void ThePlatformIdFollowsTheSystemAndTheArchitecture(bool windows, bool linux, System.Runtime.InteropServices.Architecture architecture, string expected)
    {
        Assert.Equal(expected, Codepad.Application.Updates.UpdatePlatform.Describe(windows, linux, architecture));
    }
}
