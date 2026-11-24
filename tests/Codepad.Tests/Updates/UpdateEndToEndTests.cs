using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Codepad.Application.Updates;
using Codepad.Infrastructure.Updates;
using Codepad.Tests.Experiments;

namespace Codepad.Tests.Updates;

/// <summary>
/// The whole check with the real source, the real signature check and the real parser, over a fake GitHub. It proves the chain for both
/// platforms and every way that signed metadata can be attacked.
/// </summary>
public sealed class UpdateEndToEndTests : IDisposable
{
    private readonly FakeHttp http = new FakeHttp();
    private readonly ECDsa publisher = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ManualTimeProvider clock = new ManualTimeProvider();
    private readonly List<UpdateEvent> events = [];

    public void Dispose()
    {
        http.Dispose();
        publisher.Dispose();
    }

    private static string Base(string version) => $"https://github.com/daniel-kindl/codepad/releases/download/v{version}/";

    private void Serve(string tag, byte[] manifest, byte[] signature, bool prerelease = false)
    {
        JsonObject release = new JsonObject
        {
            ["tag_name"] = tag,
            ["draft"] = false,
            ["prerelease"] = prerelease,
            ["assets"] = new JsonArray
            {
                new JsonObject { ["name"] = "manifest.json", ["browser_download_url"] = Base(tag.TrimStart('v')) + "manifest.json" },
                new JsonObject { ["name"] = "manifest.json.sig", ["browser_download_url"] = Base(tag.TrimStart('v')) + "manifest.json.sig" },
            },
        };
        http.Add("https://api.github.com/repos/daniel-kindl/codepad/releases/latest", release.ToJsonString());
        http.Add(Base(tag.TrimStart('v')) + "manifest.json", HttpStatusCode.OK, manifest);
        http.Add(Base(tag.TrimStart('v')) + "manifest.json.sig", HttpStatusCode.OK, signature);
    }

    private byte[] Sign(byte[] data) => EcdsaManifestVerifierTests.Sign(publisher, data);

    private UpdateService Service(string installed, string platform, ECDsa? trustedKey = null, string? pem = null)
    {
        EcdsaManifestVerifier verifier = new EcdsaManifestVerifier(pem ?? (trustedKey ?? publisher).ExportSubjectPublicKeyInfoPem());
        return new UpdateService(new GitHubUpdateSource(new HttpClient(http)), verifier, new RefusingDownloader(), new RefusingInstaller(), new MemoryStore(), clock, SemanticVersion.TryParse(installed, out SemanticVersion? version) ? version! : throw new InvalidOperationException(), platform, events.Add);
    }

    private static byte[] Manifest(string version) => Encoding.UTF8.GetBytes(UpdateManifestTests.Valid(version).ToJsonString());

    [Theory]
    [InlineData("linux-x64")]
    [InlineData("win-x64")]
    public async Task ANewerStableReleaseWithASignedManifestIsOfferedForEachPlatform(string platform)
    {
        byte[] manifest = Manifest("0.2.0");
        Serve("v0.2.0", manifest, Sign(manifest));
        UpdateService service = Service("0.1.0", platform);

        await service.CheckAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.Available, service.Status.State);
        Assert.Equal(platform, service.Status.Offer!.Asset.Platform);
        Assert.Equal("0.2.0", service.Status.Offer.Version.ToString());
    }

    [Fact]
    public async Task AManifestChangedAfterSigningIsRejected()
    {
        byte[] manifest = Manifest("0.2.0");
        byte[] signature = Sign(manifest);
        JsonObject tampered = UpdateManifestTests.Valid("0.2.0");
        tampered["assets"]![0]!["sha256"] = new string('f', 64);
        Serve("v0.2.0", Encoding.UTF8.GetBytes(tampered.ToJsonString()), signature);
        UpdateService service = Service("0.1.0", "linux-x64");

        await service.CheckAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal("signature_invalid", service.Status.ErrorCode);
        Assert.Null(service.Status.Offer);
    }

    [Fact]
    public async Task AManifestSignedByAnotherKeyIsRejected()
    {
        using ECDsa attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] manifest = Manifest("0.9.0");
        Serve("v0.9.0", manifest, EcdsaManifestVerifierTests.Sign(attacker, manifest));
        UpdateService service = Service("0.1.0", "linux-x64");

        await service.CheckAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal("signature_invalid", service.Status.ErrorCode);
    }

    [Fact]
    public async Task ABuildWithoutAKeyInstallsNothingAndMakesNoRequest()
    {
        byte[] manifest = Manifest("0.2.0");
        Serve("v0.2.0", manifest, Sign(manifest));
        UpdateService service = Service("0.1.0", "linux-x64", pem: string.Empty);

        await service.CheckAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal("no_signing_key", service.Status.ErrorCode);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task AnOldSignedManifestShownUnderANewTagIsRejected()
    {
        // The attacker re-uses a real, signed 0.1.5 manifest and a new tag. The signature is good, but the version does not match the tag.
        byte[] manifest = Manifest("0.1.5");
        Serve("v0.9.0", manifest, Sign(manifest));
        UpdateService service = Service("0.1.0", "linux-x64");

        await service.CheckAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal("manifest_version_mismatch", service.Status.ErrorCode);
        Assert.Null(service.Status.Offer);
    }

    [Fact]
    public async Task APreReleaseAndAnOlderOrEqualReleaseAreNotOfferedEvenWithAValidSignature()
    {
        byte[] pre = Manifest("0.3.0-rc.1");
        Serve("v0.3.0-rc.1", pre, Sign(pre), prerelease: true);
        UpdateService first = Service("0.1.0", "linux-x64");
        await first.CheckAsync(true, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.UpToDate, first.Status.State);

        byte[] older = Manifest("0.1.0");
        Serve("v0.1.0", older, Sign(older));
        UpdateService second = Service("0.1.0", "linux-x64");
        await second.CheckAsync(true, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.UpToDate, second.Status.State);
    }

    [Fact]
    public async Task AWrongPlatformIsRejectedWithAClearCode()
    {
        byte[] manifest = Manifest("0.2.0");
        Serve("v0.2.0", manifest, Sign(manifest));
        UpdateService service = Service("0.1.0", "macos-arm64");

        await service.CheckAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal("no_asset_for_platform", service.Status.ErrorCode);
    }

    [Fact]
    public async Task AnUntrustedRedirectOnTheManifestDownloadIsRejected()
    {
        byte[] manifest = Manifest("0.2.0");
        Serve("v0.2.0", manifest, Sign(manifest));
        http.Add(Base("0.2.0") + "manifest.json", HttpStatusCode.Found, [], location: "https://evil.example/manifest.json");
        UpdateService service = Service("0.1.0", "linux-x64");

        await service.CheckAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal("untrusted_redirect", service.Status.ErrorCode);
        Assert.DoesNotContain(http.Requests, request => request.Host == "evil.example");
    }

    [Fact]
    public async Task OfflineAndRateLimitFailuresAreSilentForTheAutomaticCheckAndNeverThrow()
    {
        http.Failure = new HttpRequestException("offline");
        UpdateService offline = Service("0.1.0", "linux-x64");
        await offline.CheckIfDueAsync(true, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Idle, offline.Status.State);

        http.Failure = null;
        http.Add("https://api.github.com/repos/daniel-kindl/codepad/releases/latest", HttpStatusCode.Forbidden, []);
        clock.Advance(TimeSpan.FromDays(2));
        UpdateService limited = Service("0.1.0", "linux-x64");
        await limited.CheckIfDueAsync(true, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Idle, limited.Status.State);
        Assert.Contains(events, item => item.Code == "rate_limited");
    }

    [Fact]
    public async Task TheLogEventsNeverHoldAnAddressAPathOrAToken()
    {
        byte[] manifest = Manifest("0.2.0");
        Serve("v0.2.0", manifest, Sign(manifest));
        await Service("0.1.0", "linux-x64").CheckAsync(true, TestContext.Current.CancellationToken);

        string text = string.Join(' ', events.Select(item => item.Name + item.Version + item.Code));

        Assert.DoesNotContain("http", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/", text, StringComparison.Ordinal);
    }

    private sealed class RefusingDownloader : IUpdateDownloader
    {
        public Task<StagedUpdate> DownloadAsync(UpdateOffer offer, IProgress<UpdateProgress>? progress, CancellationToken cancellationToken) => throw new InvalidOperationException("The check must not download.");
    }

    private sealed class RefusingInstaller : IUpdateInstaller
    {
        public string? CheckSupported() => null;

        public Task HandOffAsync(StagedUpdate staged, CancellationToken cancellationToken) => throw new InvalidOperationException("The check must not install.");
    }

    private sealed class MemoryStore : IUpdateStore
    {
        private DateTimeOffset? last;

        public Task<DateTimeOffset?> GetLastCheckAsync() => Task.FromResult(last);

        public Task SetLastCheckAsync(DateTimeOffset time)
        {
            last = time;
            return Task.CompletedTask;
        }

        public Task<bool> GetPromptShownAsync() => Task.FromResult(true);

        public Task SetPromptShownAsync() => Task.CompletedTask;
    }
}
