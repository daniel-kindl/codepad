using System.Net;
using System.Security.Cryptography;
using Codepad.Application.Updates;
using Codepad.Infrastructure.Updates;

namespace Codepad.Tests.Updates;

public sealed class HttpUpdateDownloaderTests : IDisposable
{
    private readonly string staging = Directory.CreateTempSubdirectory("codepad-staging-").FullName;
    private readonly FakeHttp http = new FakeHttp();
    private readonly byte[] content = RandomNumberGenerator.GetBytes(300_000);

    public void Dispose()
    {
        http.Dispose();
        Directory.Delete(staging, recursive: true);
    }

    private UpdateOffer Offer(byte[]? signedContent = null, long? size = null, string? digest = null)
    {
        byte[] data = signedContent ?? content;
        string url = "https://github.com/daniel-kindl/codepad/releases/download/v0.2.0/codepad-0.2.0-linux-x64.tar.gz";
        UpdateAsset asset = new UpdateAsset("linux-x64", "codepad-0.2.0-linux-x64.tar.gz", size ?? data.Length, digest ?? Convert.ToHexStringLower(SHA256.HashData(data)), new Uri(url));
        return new UpdateOffer(SemanticVersion.TryParse("0.2.0", out SemanticVersion? version) ? version! : throw new InvalidOperationException(), new Uri("https://github.com/daniel-kindl/codepad/releases/tag/v0.2.0"), "notes", asset);
    }

    private HttpUpdateDownloader Downloader() => new HttpUpdateDownloader(new HttpClient(http), staging);

    private string[] Files() => Directory.Exists(staging) ? [.. Directory.GetFiles(staging, "*", SearchOption.AllDirectories)] : [];

    [Fact]
    public async Task AValidArchiveIsStagedWithItsContentAndProgress()
    {
        UpdateOffer offer = Offer();
        http.Add(offer.Asset.Url.ToString(), HttpStatusCode.OK, content);
        List<UpdateProgress> steps = [];

        StagedUpdate staged = await Downloader().DownloadAsync(offer, new Progress(steps), TestContext.Current.CancellationToken);

        Assert.Equal(content, await File.ReadAllBytesAsync(staged.FilePath, TestContext.Current.CancellationToken));
        Assert.StartsWith(staging, staged.FilePath, StringComparison.Ordinal);
        Assert.Equal(content.Length, steps[^1].BytesReceived);
        Assert.Equal(1.0, steps[^1].Fraction);
        Assert.Equal([staged.FilePath], Files());
    }

    [Fact]
    public async Task ADigestThatDoesNotMatchTheSignedManifestIsRejectedAndNothingStays()
    {
        UpdateOffer offer = Offer(digest: new string('0', 64));
        http.Add(offer.Asset.Url.ToString(), HttpStatusCode.OK, content);

        UpdateDownloadException error = await Assert.ThrowsAsync<UpdateDownloadException>(() => Downloader().DownloadAsync(offer, null, TestContext.Current.CancellationToken));

        Assert.Equal("digest_mismatch", error.Code);
        Assert.Empty(Files());
    }

    [Fact]
    public async Task ATamperedArchiveWithTheRightSizeIsRejected()
    {
        UpdateOffer offer = Offer();
        byte[] tampered = (byte[])content.Clone();
        tampered[1234] ^= 0xFF;
        http.Add(offer.Asset.Url.ToString(), HttpStatusCode.OK, tampered);

        UpdateDownloadException error = await Assert.ThrowsAsync<UpdateDownloadException>(() => Downloader().DownloadAsync(offer, null, TestContext.Current.CancellationToken));

        Assert.Equal("digest_mismatch", error.Code);
        Assert.Empty(Files());
    }

    [Fact]
    public async Task ATooShortArchiveIsASizeMismatch()
    {
        UpdateOffer offer = Offer();
        http.AddStream(offer.Asset.Url.ToString(), () => new MemoryStream(content[..1000]));

        UpdateDownloadException error = await Assert.ThrowsAsync<UpdateDownloadException>(() => Downloader().DownloadAsync(offer, null, TestContext.Current.CancellationToken));

        Assert.Equal("size_mismatch", error.Code);
        Assert.Empty(Files());
    }

    [Fact]
    public async Task ABodyLongerThanTheSignedSizeStopsAtOnceAndKeepsNothing()
    {
        UpdateOffer offer = Offer();
        long produced = 0;
        http.AddStream(offer.Asset.Url.ToString(), () => new EndlessStream(count => produced += count));

        UpdateDownloadException error = await Assert.ThrowsAsync<UpdateDownloadException>(() => Downloader().DownloadAsync(offer, null, TestContext.Current.CancellationToken));

        Assert.Equal("size_mismatch", error.Code);
        Assert.True(produced < offer.Asset.Size + 4 * 1024 * 1024, $"The downloader read {produced} bytes of an endless body.");
        Assert.Empty(Files());
    }

    [Fact]
    public async Task ADeclaredLengthThatDiffersFromTheSignedSizeIsRejectedBeforeTheBodyIsRead()
    {
        UpdateOffer offer = Offer();
        http.Add(offer.Asset.Url.ToString(), HttpStatusCode.OK, content, declaredLength: content.Length + 5);

        UpdateDownloadException error = await Assert.ThrowsAsync<UpdateDownloadException>(() => Downloader().DownloadAsync(offer, null, TestContext.Current.CancellationToken));

        Assert.Equal("size_mismatch", error.Code);
    }

    [Fact]
    public async Task ARedirectToAnUntrustedHostIsRejected()
    {
        UpdateOffer offer = Offer();
        http.Add(offer.Asset.Url.ToString(), HttpStatusCode.Found, [], location: "https://evil.example/codepad.tar.gz");

        UpdateDownloadException error = await Assert.ThrowsAsync<UpdateDownloadException>(() => Downloader().DownloadAsync(offer, null, TestContext.Current.CancellationToken));

        Assert.Equal("untrusted_redirect", error.Code);
        Assert.DoesNotContain(http.Requests, request => request.Host == "evil.example");
    }

    [Fact]
    public async Task ARedirectToTheGitHubCdnWorks()
    {
        UpdateOffer offer = Offer();
        http.Add(offer.Asset.Url.ToString(), HttpStatusCode.Found, [], location: "https://release-assets.githubusercontent.com/x/y?sig=1");
        http.Add("https://release-assets.githubusercontent.com/x/y?sig=1", HttpStatusCode.OK, content);

        StagedUpdate staged = await Downloader().DownloadAsync(offer, null, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(staged.FilePath));
    }

    [Fact]
    public async Task ANetworkFailureAndAServerErrorAreCodes()
    {
        UpdateOffer offer = Offer();
        http.Failure = new HttpRequestException("reset");
        Assert.Equal("network", (await Assert.ThrowsAsync<UpdateDownloadException>(() => Downloader().DownloadAsync(offer, null, TestContext.Current.CancellationToken))).Code);

        http.Failure = null;
        http.Add(offer.Asset.Url.ToString(), HttpStatusCode.BadGateway, []);
        Assert.Equal("http_502", (await Assert.ThrowsAsync<UpdateDownloadException>(() => Downloader().DownloadAsync(offer, null, TestContext.Current.CancellationToken))).Code);
        Assert.Empty(Files());
    }

    [Fact]
    public async Task CancelStopsTheDownloadAndDeletesThePartialFile()
    {
        UpdateOffer offer = Offer();
        using CancellationTokenSource cancel = new CancellationTokenSource();
        http.AddStream(offer.Asset.Url.ToString(), () => new CancelAfterStream(content, cancel));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Downloader().DownloadAsync(offer, null, cancel.Token));

        Assert.Empty(Files());
    }

    [Fact]
    public async Task ARetryAfterAFailureReplacesTheOldFiles()
    {
        UpdateOffer offer = Offer();
        http.Add(offer.Asset.Url.ToString(), HttpStatusCode.OK, new byte[10], declaredLength: 10);
        await Assert.ThrowsAsync<UpdateDownloadException>(() => Downloader().DownloadAsync(offer, null, TestContext.Current.CancellationToken));
        http.Add(offer.Asset.Url.ToString(), HttpStatusCode.OK, content);

        StagedUpdate staged = await Downloader().DownloadAsync(offer, null, TestContext.Current.CancellationToken);

        Assert.Equal([staged.FilePath], Files());
    }

    [Fact]
    public async Task AnAssetUrlOutsideTheOfficialHostsIsRefusedWithoutARequest()
    {
        UpdateOffer good = Offer();
        UpdateOffer bad = good with { Asset = good.Asset with { Url = new Uri("https://evil.example/a.tar.gz") } };

        UpdateDownloadException error = await Assert.ThrowsAsync<UpdateDownloadException>(() => Downloader().DownloadAsync(bad, null, TestContext.Current.CancellationToken));

        Assert.Equal("untrusted_url", error.Code);
        Assert.Empty(http.Requests);
    }

    private sealed class Progress(List<UpdateProgress> steps) : IProgress<UpdateProgress>
    {
        public void Report(UpdateProgress value) => steps.Add(value);
    }

    private sealed class EndlessStream(Action<int> counted) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            counted(count);
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CancelAfterStream(byte[] data, CancellationTokenSource cancel) : Stream
    {
        private int position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => data.Length;

        public override long Position { get => position; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int take = Math.Min(Math.Min(count, 50_000), data.Length - position);
            Array.Copy(data, position, buffer, offset, take);
            position += take;
            if (position >= 100_000)
            {
                cancel.Cancel();
            }

            return take;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
