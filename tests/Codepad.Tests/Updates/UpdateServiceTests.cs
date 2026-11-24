using System.Text;
using Codepad.Application.Updates;
using Codepad.Tests.Experiments;

namespace Codepad.Tests.Updates;

public sealed class UpdateServiceTests
{
    private readonly FakeSource source = new FakeSource();
    private readonly FakeVerifier verifier = new FakeVerifier();
    private readonly FakeDownloader downloader = new FakeDownloader();
    private readonly FakeInstaller installer = new FakeInstaller();
    private readonly FakeStore store = new FakeStore();
    private readonly ManualTimeProvider clock = new ManualTimeProvider();
    private readonly List<UpdateEvent> events = [];
    private readonly List<UpdateStatus> statuses = [];

    private UpdateService Service(string installed = "0.1.0", string platform = "linux-x64")
    {
        UpdateService service = new UpdateService(source, verifier, downloader, installer, store, clock, SemanticVersion.TryParse(installed, out SemanticVersion? version) ? version! : throw new InvalidOperationException(installed), platform, events.Add);
        service.StatusChanged += statuses.Add;
        return service;
    }

    private void Publish(string version = "0.2.0") => source.Next = new SignedManifest(Encoding.UTF8.GetBytes(UpdateManifestTests.Valid(version).ToJsonString()), [1, 2, 3]);

    [Fact]
    public async Task AnUpdateToANewerStableReleaseBecomesAvailable()
    {
        Publish();
        UpdateService service = Service();

        await service.CheckAsync(userRequested: true, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.Available, service.Status.State);
        Assert.Equal("0.2.0", service.Status.Offer!.Version.ToString());
        Assert.Contains(UpdateState.Checking, statuses.Select(status => status.State));
    }

    [Fact]
    public async Task NoNewReleaseIsUpToDateForAManualCheckAndSilentForAnAutomaticOne()
    {
        Publish("0.1.0");
        UpdateService manual = Service();
        await manual.CheckAsync(true, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.UpToDate, manual.Status.State);

        UpdateService automatic = Service();
        await automatic.CheckAsync(false, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Idle, automatic.Status.State);
    }

    [Fact]
    public async Task NoStableReleaseAtAllIsUpToDate()
    {
        source.Next = null;
        UpdateService service = Service();

        await service.CheckAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.UpToDate, service.Status.State);
    }

    [Fact]
    public async Task AnInvalidSignatureIsRejectedBeforeTheManifestIsRead()
    {
        Publish();
        verifier.Valid = false;
        UpdateService service = Service();

        await service.CheckAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.Failed, service.Status.State);
        Assert.Equal("signature_invalid", service.Status.ErrorCode);
        Assert.Null(service.Status.Offer);
        Assert.Contains(events, item => item.Code == "signature_invalid");
    }

    [Fact]
    public async Task ABuildWithoutAPublicKeyTrustsNothing()
    {
        Publish();
        verifier.Key = false;
        UpdateService service = Service();

        await service.CheckAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal("no_signing_key", service.Status.ErrorCode);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task ASignedButInvalidManifestIsRejected()
    {
        source.Next = new SignedManifest(Encoding.UTF8.GetBytes("{\"nope\":1}"), [1]);
        UpdateService service = Service();

        await service.CheckAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal("manifest_invalid", service.Status.ErrorCode);
    }

    [Fact]
    public async Task APreReleaseAndAnOlderReleaseAreNotOffered()
    {
        Publish("0.3.0-rc.1");
        UpdateService pre = Service();
        await pre.CheckAsync(true, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.UpToDate, pre.Status.State);

        Publish("0.0.9");
        UpdateService older = Service();
        await older.CheckAsync(true, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.UpToDate, older.Status.State);
    }

    [Fact]
    public async Task AWrongPlatformIsNotOfferedAndSaysSo()
    {
        Publish();
        UpdateService service = Service(platform: "macos-arm64");

        await service.CheckAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.Failed, service.Status.State);
        Assert.Equal("no_asset_for_platform", service.Status.ErrorCode);
    }

    [Fact]
    public async Task ANetworkFailureIsVisibleForAManualCheckAndSilentForAnAutomaticOne()
    {
        source.Failure = new UpdateSourceException("network");
        UpdateService manual = Service();
        await manual.CheckAsync(true, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Failed, manual.Status.State);
        Assert.Equal("network", manual.Status.ErrorCode);

        UpdateService automatic = Service();
        await automatic.CheckAsync(false, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Idle, automatic.Status.State);
        Assert.Contains(events, item => item.Name == "update.check_failed" && item.Code == "network");
    }

    [Fact]
    public async Task AnUnexpectedExceptionInTheSourceNeverEscapesAndOnlyTheTypeIsReported()
    {
        source.Failure = new InvalidOperationException("secret token abc");
        UpdateService service = Service();

        await service.CheckAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.Idle, service.Status.State);
        Assert.DoesNotContain(events, item => (item.Code ?? string.Empty).Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACancelledCheckGoesBackToIdle()
    {
        Publish();
        source.Delay = new TaskCompletionSource();
        UpdateService service = Service();
        using CancellationTokenSource cancel = new CancellationTokenSource();

        Task check = service.CheckAsync(true, cancel.Token);
        await cancel.CancelAsync();
        await check;

        Assert.Equal(UpdateState.Idle, service.Status.State);
    }

    [Fact]
    public async Task AnAutomaticCheckRunsOnlyWhenEnabledAndNoMoreThanOnceIn24Hours()
    {
        Publish();
        UpdateService service = Service();

        Assert.False(await service.CheckIfDueAsync(enabled: false, TestContext.Current.CancellationToken));
        Assert.Equal(0, source.Calls);

        Assert.True(await service.CheckIfDueAsync(true, TestContext.Current.CancellationToken));
        Assert.Equal(1, source.Calls);

        clock.Advance(TimeSpan.FromHours(23));
        Assert.False(await service.CheckIfDueAsync(true, TestContext.Current.CancellationToken));

        clock.Advance(TimeSpan.FromHours(2));
        Assert.True(await service.CheckIfDueAsync(true, TestContext.Current.CancellationToken));
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task AFailedAutomaticCheckStillCountsSoGitHubIsNotCalledAgainAtOnce()
    {
        source.Failure = new UpdateSourceException("rate_limited");
        UpdateService service = Service();

        await service.CheckIfDueAsync(true, TestContext.Current.CancellationToken);
        bool again = await Service().CheckIfDueAsync(true, TestContext.Current.CancellationToken);

        Assert.False(again);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task AManualCheckAlwaysRunsAndNeverDependsOnTheTimer()
    {
        Publish("0.1.0");
        UpdateService service = Service();
        await service.CheckIfDueAsync(true, TestContext.Current.CancellationToken);

        await service.CheckAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task DownloadShowsProgressThenVerifyingThenReady()
    {
        Publish();
        UpdateService service = Service();
        await service.CheckAsync(true, TestContext.Current.CancellationToken);
        downloader.ProgressSteps = [new UpdateProgress(100, 1_234_567), new UpdateProgress(1_234_567, 1_234_567)];

        await service.DownloadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.ReadyToInstall, service.Status.State);
        Assert.Contains(statuses, status => status.State == UpdateState.Downloading && status.Progress is { BytesReceived: 100 });
        Assert.Contains(statuses, status => status.State == UpdateState.Verifying);
        Assert.NotNull(service.Status.Staged);
    }

    [Fact]
    public async Task ACancelledDownloadReturnsToAvailableWithTheOffer()
    {
        Publish();
        UpdateService service = Service();
        await service.CheckAsync(true, TestContext.Current.CancellationToken);
        downloader.Hold = new TaskCompletionSource();
        using CancellationTokenSource cancel = new CancellationTokenSource();

        Task download = service.DownloadAsync(cancel.Token);
        await cancel.CancelAsync();
        await download;

        Assert.Equal(UpdateState.Available, service.Status.State);
        Assert.NotNull(service.Status.Offer);
        Assert.Null(service.Status.ErrorCode);
    }

    [Theory]
    [InlineData("digest_mismatch")]
    [InlineData("size_mismatch")]
    [InlineData("untrusted_redirect")]
    [InlineData("network")]
    public async Task AFailedDownloadIsFailedWithItsCodeAndCanBeRetried(string code)
    {
        Publish();
        UpdateService service = Service();
        await service.CheckAsync(true, TestContext.Current.CancellationToken);
        downloader.Failure = new UpdateDownloadException(code);

        await service.DownloadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.Failed, service.Status.State);
        Assert.Equal(code, service.Status.ErrorCode);
        Assert.NotNull(service.Status.Offer);
        Assert.Null(service.Status.Staged);

        downloader.Failure = null;
        await service.DownloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.ReadyToInstall, service.Status.State);
    }

    [Fact]
    public async Task DownloadWithoutAnOfferDoesNothing()
    {
        UpdateService service = Service();

        await service.DownloadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.Idle, service.Status.State);
        Assert.Equal(0, downloader.Calls);
    }

    [Fact]
    public async Task InstallHandsOffOnlyAfterAVerifiedDownload()
    {
        Publish();
        UpdateService service = Service();
        await service.CheckAsync(true, TestContext.Current.CancellationToken);

        await service.InstallAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, installer.Calls);

        await service.DownloadAsync(TestContext.Current.CancellationToken);
        await service.InstallAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, installer.Calls);
        Assert.Equal(UpdateState.Installing, service.Status.State);
    }

    [Fact]
    public async Task AFailedHandoffIsFailedWithTheCodeAndTheStagedUpdateStaysForARetry()
    {
        Publish();
        UpdateService service = Service();
        await service.CheckAsync(true, TestContext.Current.CancellationToken);
        await service.DownloadAsync(TestContext.Current.CancellationToken);
        installer.Failure = new UpdateInstallException("helper_failed");

        await service.InstallAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.Failed, service.Status.State);
        Assert.Equal("helper_failed", service.Status.ErrorCode);
        Assert.NotNull(service.Status.Staged);

        installer.Failure = null;
        await service.InstallAsync(TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Installing, service.Status.State);
    }

    [Fact]
    public async Task AnInstallationThatCannotBeWrittenOffersTheUpdateButNotTheDownload()
    {
        Publish();
        installer.Unsupported = "CodePad is installed in a folder that you cannot write.";
        UpdateService service = Service();

        await service.CheckAsync(true, TestContext.Current.CancellationToken);
        await service.DownloadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.Available, service.Status.State);
        Assert.Equal("CodePad is installed in a folder that you cannot write.", service.Status.ManualInstallReason);
        Assert.Equal(0, downloader.Calls);
    }

    [Fact]
    public async Task LaterHidesTheOfferUntilTheNextCheck()
    {
        Publish();
        UpdateService service = Service();
        await service.CheckAsync(true, TestContext.Current.CancellationToken);

        service.Postpone();

        Assert.Equal(UpdateState.Idle, service.Status.State);
        Assert.Null(service.Status.Offer);

        await service.CheckAsync(true, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Available, service.Status.State);
    }

    [Fact]
    public async Task OnlyOneOperationRunsAtATime()
    {
        Publish();
        source.Delay = new TaskCompletionSource();
        UpdateService service = Service();

        Task first = service.CheckAsync(true, TestContext.Current.CancellationToken);
        await service.CheckAsync(true, TestContext.Current.CancellationToken);
        source.Delay.SetResult();
        await first;

        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task TheEventsCarryVersionsAndCodesAndNeverUrlsOrPaths()
    {
        Publish();
        UpdateService service = Service();
        await service.CheckAsync(true, TestContext.Current.CancellationToken);
        await service.DownloadAsync(TestContext.Current.CancellationToken);

        Assert.Contains(events, item => item.Name == "update.available" && item.Version == "0.2.0");
        Assert.Contains(events, item => item.Name == "update.staged" && item.Version == "0.2.0");
        Assert.DoesNotContain(events, item => (item.Code ?? string.Empty).Contains("http", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePromptIsRememberedInTheStore()
    {
        Assert.False(store.PromptShown);
    }

    private sealed class FakeSource : IUpdateSource
    {
        public SignedManifest? Next { get; set; }

        public Exception? Failure { get; set; }

        public TaskCompletionSource? Delay { get; set; }

        public int Calls { get; private set; }

        public async Task<SignedManifest?> FetchLatestAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Delay is not null)
            {
                await Delay.Task.WaitAsync(cancellationToken);
            }

            return Failure is not null ? throw Failure : Next;
        }
    }

    private sealed class FakeVerifier : IManifestVerifier
    {
        public bool Valid { get; set; } = true;

        public bool Key { get; set; } = true;

        public bool HasKey => Key;

        public bool Verify(ReadOnlySpan<byte> manifestBytes, ReadOnlySpan<byte> signature) => Valid;
    }

    private sealed class FakeDownloader : IUpdateDownloader
    {
        public UpdateDownloadException? Failure { get; set; }

        public TaskCompletionSource? Hold { get; set; }

        public IReadOnlyList<UpdateProgress> ProgressSteps { get; set; } = [];

        public int Calls { get; private set; }

        public async Task<StagedUpdate> DownloadAsync(UpdateOffer offer, IProgress<UpdateProgress>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            foreach (UpdateProgress step in ProgressSteps)
            {
                progress?.Report(step);
            }

            if (Hold is not null)
            {
                await Hold.Task.WaitAsync(cancellationToken);
            }

            return Failure is not null ? throw Failure : new StagedUpdate(offer, "/staging/archive");
        }
    }

    private sealed class FakeInstaller : IUpdateInstaller
    {
        public string? Unsupported { get; set; }

        public UpdateInstallException? Failure { get; set; }

        public int Calls { get; private set; }

        public string? CheckSupported() => Unsupported;

        public Task HandOffAsync(StagedUpdate staged, CancellationToken cancellationToken)
        {
            Calls++;
            return Failure is not null ? Task.FromException(Failure) : Task.CompletedTask;
        }
    }

    private sealed class FakeStore : IUpdateStore
    {
        private DateTimeOffset? last;

        public bool PromptShown { get; private set; }

        public Task<DateTimeOffset?> GetLastCheckAsync() => Task.FromResult(last);

        public Task SetLastCheckAsync(DateTimeOffset time)
        {
            last = time;
            return Task.CompletedTask;
        }

        public Task<bool> GetPromptShownAsync() => Task.FromResult(PromptShown);

        public Task SetPromptShownAsync()
        {
            PromptShown = true;
            return Task.CompletedTask;
        }
    }
}
