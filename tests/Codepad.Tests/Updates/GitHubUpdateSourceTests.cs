using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Codepad.Application.Updates;
using Codepad.Infrastructure.Updates;

namespace Codepad.Tests.Updates;

public sealed class GitHubUpdateSourceTests : IDisposable
{
    private const string API = "https://api.github.com/repos/daniel-kindl/codepad/releases/latest";
    private const string BASE = "https://github.com/daniel-kindl/codepad/releases/download/v0.2.0/";

    private readonly FakeHttp http = new FakeHttp();

    public void Dispose() => http.Dispose();

    private GitHubUpdateSource Source() => new GitHubUpdateSource(new HttpClient(http));

    private static JsonObject Release(string tag = "v0.2.0", bool draft = false, bool prerelease = false, string? manifestUrl = null, string? signatureUrl = null, bool withSignature = true) => new JsonObject
    {
        ["tag_name"] = tag,
        ["draft"] = draft,
        ["prerelease"] = prerelease,
        ["assets"] = new JsonArray
        {
            new JsonObject { ["name"] = "manifest.json", ["browser_download_url"] = manifestUrl ?? BASE + "manifest.json" },
            withSignature ? new JsonObject { ["name"] = "manifest.json.sig", ["browser_download_url"] = signatureUrl ?? BASE + "manifest.json.sig" } : new JsonObject { ["name"] = "other.txt", ["browser_download_url"] = BASE + "other.txt" },
            new JsonObject { ["name"] = "codepad-0.2.0-linux-x64.tar.gz", ["browser_download_url"] = BASE + "codepad-0.2.0-linux-x64.tar.gz" },
        },
    };

    private void Serve(JsonObject release)
    {
        http.Add(API, release.ToJsonString());
        http.Add(BASE + "manifest.json", "{\"manifest\":true}");
        http.Add(BASE + "manifest.json.sig", HttpStatusCode.OK, [1, 2, 3, 4]);
    }

    [Fact]
    public async Task TheLatestStableReleaseGivesTheSignedManifestAndItsTag()
    {
        Serve(Release());

        SignedManifest? signed = await Source().FetchLatestAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(signed);
        Assert.Equal("{\"manifest\":true}", Encoding.UTF8.GetString(signed.ManifestBytes));
        Assert.Equal([1, 2, 3, 4], signed.Signature);
        Assert.Equal("v0.2.0", signed.ReleaseTag);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ADraftOrAPreReleaseIsIgnored(bool draft, bool prerelease)
    {
        Serve(Release(draft: draft, prerelease: prerelease));

        Assert.Null(await Source().FetchLatestAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("v0.3.0-rc.1")]
    [InlineData("latest")]
    [InlineData("")]
    public async Task ATagThatIsNotAStableVersionIsIgnored(string tag)
    {
        Serve(Release(tag));

        Assert.Null(await Source().FetchLatestAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AReleaseWithoutTheSignatureFileIsNotAnUpdate()
    {
        Serve(Release(withSignature: false));

        Assert.Null(await Source().FetchLatestAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("https://evil.example/daniel-kindl/codepad/releases/download/v0.2.0/manifest.json")]
    [InlineData("https://github.com/someone/codepad/releases/download/v0.2.0/manifest.json")]
    [InlineData("https://github.com/daniel-kindl/codepad/releases/download/v0.1.0/manifest.json")]
    [InlineData("http://github.com/daniel-kindl/codepad/releases/download/v0.2.0/manifest.json")]
    [InlineData("https://github.com/daniel-kindl/codepad/releases/download/v0.2.0/manifest.json?x=1")]
    public async Task AManifestAddressOutsideTheOfficialReleaseIsRefusedAndNeverRequested(string url)
    {
        Serve(Release(manifestUrl: url));

        UpdateSourceException error = await Assert.ThrowsAsync<UpdateSourceException>(() => Source().FetchLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal("untrusted_url", error.Code);
        Assert.DoesNotContain(http.Requests, request => request.Host == "evil.example");
    }

    [Fact]
    public async Task ARedirectToTheCdnOfGitHubIsFollowed()
    {
        http.Add(API, Release().ToJsonString());
        http.Add(BASE + "manifest.json", HttpStatusCode.Found, [], location: "https://objects.githubusercontent.com/abc/manifest.json?token=x");
        http.Add("https://objects.githubusercontent.com/abc/manifest.json?token=x", "{\"manifest\":true}");
        http.Add(BASE + "manifest.json.sig", HttpStatusCode.OK, [9]);

        SignedManifest? signed = await Source().FetchLatestAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(signed);
    }

    [Theory]
    [InlineData("https://evil.example/manifest.json")]
    [InlineData("http://objects.githubusercontent.com/manifest.json")]
    [InlineData("https://objects.githubusercontent.com.evil.example/manifest.json")]
    [InlineData("https://user:pw@objects.githubusercontent.com/manifest.json")]
    [InlineData("https://objects.githubusercontent.com:8443/manifest.json")]
    [InlineData("file:///etc/passwd")]
    public async Task ARedirectToAnUntrustedTargetIsRejectedBeforeAnyRequestToIt(string target)
    {
        http.Add(API, Release().ToJsonString());
        http.Add(BASE + "manifest.json", HttpStatusCode.Found, [], location: target);

        UpdateSourceException error = await Assert.ThrowsAsync<UpdateSourceException>(() => Source().FetchLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal("untrusted_redirect", error.Code);
        Assert.DoesNotContain(http.Requests, request => request.ToString() == target);
    }

    [Fact]
    public async Task ARedirectLoopStopsAfterAFewHops()
    {
        http.Add(API, Release().ToJsonString());
        http.Add(BASE + "manifest.json", HttpStatusCode.Found, [], location: BASE + "manifest.json");

        UpdateSourceException error = await Assert.ThrowsAsync<UpdateSourceException>(() => Source().FetchLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal("too_many_redirects", error.Code);
        Assert.True(http.Requests.Count <= 1 + 4);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "rate_limited")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited")]
    [InlineData(HttpStatusCode.NotFound, "http_404")]
    [InlineData(HttpStatusCode.InternalServerError, "http_500")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "http_503")]
    public async Task AnErrorStatusFromGitHubIsACodeAndNotAnException(HttpStatusCode status, string code)
    {
        http.Add(API, status, []);

        UpdateSourceException error = await Assert.ThrowsAsync<UpdateSourceException>(() => Source().FetchLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal(code, error.Code);
    }

    [Fact]
    public async Task ANetworkFailureIsANetworkCode()
    {
        http.Failure = new HttpRequestException("no route to host");

        UpdateSourceException error = await Assert.ThrowsAsync<UpdateSourceException>(() => Source().FetchLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal("network", error.Code);
        Assert.DoesNotContain("no route", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    public async Task ANonObjectAnswerIsReleaseInvalid(string body)
    {
        http.Add(API, body);

        UpdateSourceException error = await Assert.ThrowsAsync<UpdateSourceException>(() => Source().FetchLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal("release_invalid", error.Code);
    }

    [Fact]
    public async Task AnOversizedManifestIsRefusedByItsDeclaredLength()
    {
        Serve(Release());
        http.Add(BASE + "manifest.json", HttpStatusCode.OK, new byte[10], declaredLength: UpdateManifestParser.MaxBytes + 1L);

        UpdateSourceException error = await Assert.ThrowsAsync<UpdateSourceException>(() => Source().FetchLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal("too_large", error.Code);
    }

    [Fact]
    public async Task AManifestThatIsLargerThanItClaimsIsCutByTheLimitWhileReading()
    {
        Serve(Release());
        http.AddStream(BASE + "manifest.json", () => new MemoryStream(new byte[UpdateManifestParser.MaxBytes + 10]));

        UpdateSourceException error = await Assert.ThrowsAsync<UpdateSourceException>(() => Source().FetchLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal("too_large", error.Code);
    }

    [Fact]
    public async Task ASignatureFileThatIsTooLargeIsRefused()
    {
        Serve(Release());
        http.Add(BASE + "manifest.json.sig", HttpStatusCode.OK, new byte[5000]);

        UpdateSourceException error = await Assert.ThrowsAsync<UpdateSourceException>(() => Source().FetchLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal("too_large", error.Code);
    }

    [Fact]
    public async Task NoRequestCarriesCredentialsOrCookies()
    {
        Serve(Release());

        await Source().FetchLatestAsync(TestContext.Current.CancellationToken);

        // FakeHttp throws when a request has an Authorization header or a cookie. The call above would have failed.
        Assert.Equal(3, http.Requests.Count);
        Assert.All(http.Requests, request => Assert.Equal(Uri.UriSchemeHttps, request.Scheme));
    }

    [Fact]
    public async Task CancelStopsTheRequest()
    {
        Serve(Release());
        using CancellationTokenSource cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Source().FetchLatestAsync(cancel.Token));
    }
}
