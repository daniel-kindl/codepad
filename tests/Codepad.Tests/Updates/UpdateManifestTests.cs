using System.Text;
using System.Text.Json.Nodes;
using Codepad.Application.Updates;

namespace Codepad.Tests.Updates;

public sealed class UpdateManifestTests
{
    private const string URL = "https://github.com/daniel-kindl/codepad/releases/download/v0.2.0/codepad-0.2.0-linux-x64.tar.gz";

    internal static JsonObject Valid(string version = "0.2.0") => new JsonObject
    {
        ["schemaVersion"] = 1,
        ["protocol"] = 1,
        ["version"] = version,
        ["releaseUrl"] = "https://github.com/daniel-kindl/codepad/releases/tag/v" + version,
        ["releaseNotes"] = "Fixes and a new thing.",
        ["publishedAt"] = "2026-12-01T10:00:00Z",
        ["assets"] = new JsonArray
        {
            Asset("linux-x64", $"codepad-{version}-linux-x64.tar.gz", 1_234_567, new string('a', 64), version),
            Asset("win-x64", $"codepad-{version}-win-x64.zip", 2_345_678, new string('b', 64), version),
        },
    };

    internal static JsonObject Asset(string platform, string name, long size, string sha, string version) => new JsonObject
    {
        ["platform"] = platform,
        ["name"] = name,
        ["size"] = size,
        ["sha256"] = sha,
        ["url"] = $"https://github.com/daniel-kindl/codepad/releases/download/v{version}/{name}",
    };

    private static byte[] Bytes(JsonObject json) => Encoding.UTF8.GetBytes(json.ToJsonString());

    private static UpdateManifestException Fails(JsonObject json) => Assert.Throws<UpdateManifestException>(() => UpdateManifestParser.Parse(Bytes(json)));

    [Fact]
    public void AValidManifestParses()
    {
        UpdateManifest manifest = UpdateManifestParser.Parse(Bytes(Valid()));

        Assert.Equal("0.2.0", manifest.Version.ToString());
        Assert.Equal("Fixes and a new thing.", manifest.ReleaseNotes);
        Assert.Equal(2, manifest.Assets.Count);
        UpdateAsset linux = manifest.Assets.Single(asset => asset.Platform == "linux-x64");
        Assert.Equal("codepad-0.2.0-linux-x64.tar.gz", linux.Name);
        Assert.Equal(1_234_567, linux.Size);
        Assert.Equal(new string('a', 64), linux.Sha256);
        Assert.Equal(URL, linux.Url.ToString());
    }

    [Fact]
    public void TheDigestIsKeptInLowerCase()
    {
        JsonObject json = Valid();
        json["assets"]![0]!["sha256"] = new string('A', 64);

        Assert.Equal(new string('a', 64), UpdateManifestParser.Parse(Bytes(json)).Assets[0].Sha256);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("")]
    public void BadJsonIsRejected(string text)
    {
        UpdateManifestException error = Assert.Throws<UpdateManifestException>(() => UpdateManifestParser.Parse(Encoding.UTF8.GetBytes(text)));

        Assert.Equal("manifest_invalid", error.Code);
    }

    [Fact]
    public void ATooLargeManifestIsRejectedBeforeItIsParsed()
    {
        byte[] big = new byte[UpdateManifestParser.MaxBytes + 1];

        Assert.Equal("manifest_too_large", Assert.Throws<UpdateManifestException>(() => UpdateManifestParser.Parse(big)).Code);
    }

    [Theory]
    [InlineData("schemaVersion", 2)]
    [InlineData("schemaVersion", 0)]
    [InlineData("protocol", 2)]
    [InlineData("protocol", 0)]
    public void AnotherSchemaOrProtocolVersionIsRejected(string name, int value)
    {
        JsonObject json = Valid();
        json[name] = value;

        Assert.Equal("manifest_unsupported", Fails(json).Code);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("latest")]
    [InlineData("")]
    public void AnInvalidVersionIsRejected(string version)
    {
        JsonObject json = Valid();
        json["version"] = version;

        Assert.Equal("manifest_invalid", Fails(json).Code);
    }

    [Fact]
    public void APreReleaseVersionParsesButIsNotStable()
    {
        UpdateManifest manifest = UpdateManifestParser.Parse(Bytes(Valid("0.3.0-rc.1")));

        Assert.False(manifest.Version.IsStable);
    }

    [Theory]
    [InlineData("http://github.com/daniel-kindl/codepad/releases/download/v0.2.0/a.zip")]
    [InlineData("https://evil.example/daniel-kindl/codepad/releases/download/v0.2.0/a.zip")]
    [InlineData("https://github.com/someone-else/codepad/releases/download/v0.2.0/a.zip")]
    [InlineData("https://github.com/daniel-kindl/codepad/archive/main.zip")]
    [InlineData("https://github.com.evil.example/daniel-kindl/codepad/releases/download/v0.2.0/a.zip")]
    [InlineData("https://user:pw@github.com/daniel-kindl/codepad/releases/download/v0.2.0/a.zip")]
    [InlineData("https://github.com:8443/daniel-kindl/codepad/releases/download/v0.2.0/a.zip")]
    [InlineData("ftp://github.com/daniel-kindl/codepad/releases/download/v0.2.0/a.zip")]
    [InlineData("file:///etc/passwd")]
    [InlineData("not a url")]
    [InlineData("https://github.com/daniel-kindl/codepad/releases/download/v0.2.0/../../../../evil/a.zip")]
    public void AnAssetUrlOutsideTheOfficialReleasesIsRejected(string url)
    {
        JsonObject json = Valid();
        json["assets"]![0]!["url"] = url;

        Assert.Equal("manifest_untrusted_url", Fails(json).Code);
    }

    [Fact]
    public void TheAssetUrlMustBelongToTheVersionOfTheManifest()
    {
        JsonObject json = Valid();
        json["assets"]![0]!["url"] = "https://github.com/daniel-kindl/codepad/releases/download/v0.1.0/codepad-0.2.0-linux-x64.tar.gz";

        Assert.Equal("manifest_untrusted_url", Fails(json).Code);
    }

    [Fact]
    public void TheAssetUrlMustEndWithTheAssetName()
    {
        JsonObject json = Valid();
        json["assets"]![0]!["name"] = "other-name.tar.gz";

        Assert.Equal("manifest_invalid", Fails(json).Code);
    }

    [Theory]
    [InlineData("../evil.zip")]
    [InlineData("a/b.zip")]
    [InlineData("a\\b.zip")]
    [InlineData("")]
    [InlineData("name with\nnewline.zip")]
    [InlineData("CON.zip")]
    [InlineData("CON.tar.gz")]
    [InlineData("name.")]
    public void AnAssetNameThatIsNotAPlainFileNameIsRejected(string name)
    {
        JsonObject json = Valid();
        json["assets"]![0]!["name"] = name;

        Assert.Equal("manifest_invalid", Fails(json).Code);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(1_073_741_825L)]
    public void AnAssetSizeOutsideTheLimitIsRejected(long size)
    {
        JsonObject json = Valid();
        json["assets"]![0]!["size"] = size;

        Assert.Equal("manifest_invalid", Fails(json).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void ABadDigestIsRejected(string digest)
    {
        JsonObject json = Valid();
        json["assets"]![0]!["sha256"] = digest;

        Assert.Equal("manifest_invalid", Fails(json).Code);
    }

    [Fact]
    public void AnUnknownPlatformIsKeptOutOfTheListButTheManifestStillParses()
    {
        JsonObject json = Valid();
        ((JsonArray)json["assets"]!).Add(Asset("macos-arm64", "codepad-0.2.0-macos.zip", 10, new string('c', 64), "0.2.0"));

        UpdateManifest manifest = UpdateManifestParser.Parse(Bytes(json));

        Assert.DoesNotContain(manifest.Assets, asset => asset.Platform == "macos-arm64");
    }

    [Fact]
    public void TwoAssetsForOnePlatformAreRejected()
    {
        JsonObject json = Valid();
        ((JsonArray)json["assets"]!).Add(Asset("linux-x64", "codepad-0.2.0-linux-x64-b.tar.gz", 10, new string('c', 64), "0.2.0"));

        Assert.Equal("manifest_invalid", Fails(json).Code);
    }

    [Fact]
    public void TooManyAssetsAreRejected()
    {
        JsonObject json = Valid();
        JsonArray assets = (JsonArray)json["assets"]!;
        for (int index = 0; index < UpdateManifestParser.MaxAssets + 1; index++)
        {
            assets.Add(Asset("linux-x64", $"x{index}.tar.gz", 10, new string('c', 64), "0.2.0"));
        }

        Assert.Equal("manifest_invalid", Fails(json).Code);
    }

    [Fact]
    public void LongReleaseNotesAreCutAtTheLimit()
    {
        JsonObject json = Valid();
        json["releaseNotes"] = new string('n', UpdateManifestParser.MaxNotesChars + 500);

        UpdateManifest manifest = UpdateManifestParser.Parse(Bytes(json));

        Assert.Equal(UpdateManifestParser.MaxNotesChars, manifest.ReleaseNotes.Length);
    }

    [Fact]
    public void TheReleasePageMustBeAnOfficialUrl()
    {
        JsonObject json = Valid();
        json["releaseUrl"] = "https://evil.example/release";

        Assert.Equal("manifest_untrusted_url", Fails(json).Code);
    }

    [Fact]
    public void AMissingRequiredPropertyIsRejected()
    {
        foreach (string name in new[] { "schemaVersion", "protocol", "version", "releaseUrl", "assets" })
        {
            JsonObject json = Valid();
            json.Remove(name);

            Assert.Equal("manifest_invalid", Fails(json).Code);
        }
    }

    [Fact]
    public void MessagesNeverHoldTheContentOfTheManifest()
    {
        JsonObject json = Valid();
        json["version"] = "SECRET-VERSION-TEXT";

        UpdateManifestException error = Fails(json);

        Assert.DoesNotContain("SECRET", error.Message, StringComparison.Ordinal);
    }
}
