using Codepad.Application.Updates;
using Codepad.Desktop.Updates;

namespace Codepad.Desktop.Tests.Updates;

public sealed class UpdateViewModelTests
{
    private readonly FakeSource source = new FakeSource();
    private readonly FakeVerifier verifier = new FakeVerifier();
    private readonly FakeDownloader downloader = new FakeDownloader();
    private readonly FakeInstaller installer = new FakeInstaller();
    private readonly MemoryUpdateStore store = new MemoryUpdateStore { PromptShown = true };
    private readonly List<bool> checksChoice = [];
    private int exits;
    private bool canExit = true;

    private (UpdateViewModel Model, UpdateService Service) Create()
    {
        UpdateService service = new UpdateService(source, verifier, downloader, installer, store, TimeProvider.System, UpdateSamples.Version("0.1.0"), "linux-x64");
        UpdateViewModel model = new UpdateViewModel(service, store, UpdateSamples.Version("0.1.0"), checksChoice.Add, () => Task.FromResult(canExit), () => exits++, action => action());
        return (model, service);
    }

    private void Publish(string version = "0.2.0") => source.Next = new SignedManifest(UpdateSamples.Manifest(version), [1]);

    [Fact]
    public void ItIsHiddenUntilThereIsSomethingToSay()
    {
        (UpdateViewModel model, _) = Create();

        Assert.False(model.IsVisible);
        Assert.False(model.CanDownload);
    }

    [Fact]
    public async Task ANewerReleaseShowsTheVersionsTheSizeTheNotesAndTwoChoices()
    {
        Publish();
        (UpdateViewModel model, UpdateService service) = Create();

        await service.CheckAsync(false, TestContext.Current.CancellationToken);

        Assert.True(model.IsVisible);
        Assert.Equal("CodePad 0.2.0 is available. You have 0.1.0.", model.Title);
        Assert.Contains("12.3 MB", model.Detail, StringComparison.Ordinal);
        Assert.Contains("Nothing changes until you choose", model.Detail, StringComparison.Ordinal);
        Assert.Contains("A new command.", model.ReleaseNotes, StringComparison.Ordinal);
        Assert.True(model.CanDownload);
        Assert.True(model.CanLater);
        Assert.False(model.CanInstall);
        Assert.False(model.IsProblem);
    }

    [Fact]
    public async Task ASilentCheckThatFindsNothingShowsNothingAndAManualOneSaysSo()
    {
        Publish("0.1.0");
        (UpdateViewModel model, UpdateService service) = Create();
        await service.CheckAsync(false, TestContext.Current.CancellationToken);
        Assert.False(model.IsVisible);

        await model.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.True(model.IsVisible);
        Assert.Equal("CodePad 0.1.0 is the newest version.", model.Title);
        Assert.True(model.CanDismiss);
        model.Dismiss();
        Assert.False(model.IsVisible);
    }

    [Fact]
    public async Task DownloadShowsProgressInWordsAndCanBeCancelled()
    {
        Publish();
        (UpdateViewModel model, UpdateService service) = Create();
        await service.CheckAsync(false, TestContext.Current.CancellationToken);
        downloader.Steps = [new UpdateProgress(4_300_000, 12_900_000)];
        downloader.Hold = new TaskCompletionSource();

        Task download = model.DownloadAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal("Downloading CodePad 0.2.0… 33%", model.Title);
        Assert.True(model.ShowProgress);
        Assert.True(model.CanCancel);
        Assert.Equal(33, model.Percent);

        model.CancelDownload();
        await download;

        Assert.Equal(UpdateState.Available, service.Status.State);
        Assert.True(model.CanDownload);
    }

    [Fact]
    public async Task AFinishedDownloadAsksForARestartAndExplainsWhatHappensAndWhatIfItFails()
    {
        Publish();
        (UpdateViewModel model, UpdateService service) = Create();
        await service.CheckAsync(false, TestContext.Current.CancellationToken);

        await model.DownloadAsync();

        Assert.Equal("CodePad 0.2.0 is ready to install.", model.Title);
        Assert.Contains("saves your work", model.Detail, StringComparison.Ordinal);
        Assert.Contains("goes back to this one", model.Detail, StringComparison.Ordinal);
        Assert.True(model.CanInstall);
        Assert.Equal(0, installer.Calls);
        Assert.Equal(0, exits);
    }

    [Fact]
    public async Task InstallSavesTheWorkFirstThenHandsOffThenClosesTheApp()
    {
        Publish();
        (UpdateViewModel model, UpdateService service) = Create();
        await service.CheckAsync(false, TestContext.Current.CancellationToken);
        await model.DownloadAsync();

        await model.InstallAsync();

        Assert.Equal(1, installer.Calls);
        Assert.Equal(1, exits);
        Assert.Equal(UpdateState.Installing, service.Status.State);
    }

    [Fact]
    public async Task IfWorkCannotBeSavedTheAppStaysOpenAndTheHelperIsNotStarted()
    {
        Publish();
        (UpdateViewModel model, UpdateService service) = Create();
        await service.CheckAsync(false, TestContext.Current.CancellationToken);
        await model.DownloadAsync();
        canExit = false;

        await model.InstallAsync();

        Assert.Equal(0, installer.Calls);
        Assert.Equal(0, exits);
        Assert.True(model.IsProblem);
        Assert.Contains("did not close", model.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedHandOffStaysOpenShowsAReadableProblemAndOffersARetryWithoutDownloadingAgain()
    {
        Publish();
        (UpdateViewModel model, UpdateService service) = Create();
        await service.CheckAsync(false, TestContext.Current.CancellationToken);
        await model.DownloadAsync();
        installer.Failure = new UpdateInstallException("helper_failed");

        await model.InstallAsync();

        Assert.Equal(0, exits);
        Assert.True(model.IsProblem);
        Assert.Equal("CodePad could not start the update. Nothing was changed.", model.Title);
        Assert.True(model.CanRetry);

        installer.Failure = null;
        await model.RetryAsync();
        Assert.Equal(1, exits);
    }

    [Theory]
    [InlineData("digest_mismatch", "did not match its checksum")]
    [InlineData("size_mismatch", "wrong size")]
    [InlineData("untrusted_redirect", "not an official CodePad address")]
    [InlineData("network", "could not reach GitHub")]
    [InlineData("rate_limited", "asked CodePad to wait")]
    public async Task EachDownloadFailureHasAPlainSentenceAndNeverShowsTheCode(string code, string fragment)
    {
        Publish();
        (UpdateViewModel model, UpdateService service) = Create();
        await service.CheckAsync(false, TestContext.Current.CancellationToken);
        downloader.Failure = new UpdateDownloadException(code);

        await model.DownloadAsync();

        Assert.True(model.IsProblem);
        Assert.Contains(fragment, model.Title, StringComparison.Ordinal);
        Assert.DoesNotContain(code, model.Title + model.Detail, StringComparison.Ordinal);
        Assert.Contains("unchanged", model.Detail, StringComparison.Ordinal);
        Assert.True(model.CanRetry);
        Assert.True(model.CanDismiss);
    }

    [Fact]
    public async Task AnInvalidSignatureShowsThatNothingWasInstalledAndOffersNoDownload()
    {
        Publish();
        verifier.Valid = false;
        (UpdateViewModel model, UpdateService service) = Create();

        await model.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.True(model.IsProblem);
        Assert.Contains("could not be verified", model.Title, StringComparison.Ordinal);
        Assert.False(model.CanDownload);
        Assert.False(model.CanRetry);
        Assert.Equal(UpdateState.Failed, service.Status.State);
    }

    [Fact]
    public async Task AnInstallationThatCannotBeUpdatedByTheAppExplainsHowToUpdateAndOffersNoButton()
    {
        Publish();
        installer.Unsupported = "CodePad is installed in a folder that you cannot change. Update it the same way you installed it.";
        (UpdateViewModel model, UpdateService service) = Create();

        await service.CheckAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(installer.Unsupported, model.Detail);
        Assert.False(model.CanDownload);
        Assert.NotNull(model.ReleaseUrl);
        Assert.True(model.CanLater);
    }

    [Fact]
    public async Task LaterHidesTheOffer()
    {
        Publish();
        (UpdateViewModel model, UpdateService service) = Create();
        await service.CheckAsync(false, TestContext.Current.CancellationToken);

        model.Later();

        Assert.False(model.IsVisible);
        Assert.False(model.CanDownload);
    }

    [Fact]
    public async Task TheFirstRunExplanationOffersTwoChoicesAndSavesTheAnswer()
    {
        store.PromptShown = false;
        (UpdateViewModel model, _) = Create();

        await model.ShowFirstRunPromptIfNeededAsync();

        Assert.True(model.IsVisible);
        Assert.True(model.ShowPrompt);
        Assert.Contains("never downloads or installs without your click", model.Detail, StringComparison.Ordinal);
        Assert.False(model.CanDownload);

        await model.AnswerPromptAsync(keepChecking: false);

        Assert.Equal([false], checksChoice);
        Assert.False(model.IsVisible);
        Assert.True(store.PromptShown);
    }

    [Fact]
    public async Task TheExplanationShowsOnlyOnce()
    {
        (UpdateViewModel model, _) = Create();

        await model.ShowFirstRunPromptIfNeededAsync();

        Assert.False(model.ShowPrompt);
    }

    [Theory]
    [InlineData("updated", "was updated to 0.2.0", false)]
    [InlineData("rolled_back", "went back to 0.1.0", true)]
    [InlineData("not_applied", "still 0.1.0", true)]
    [InlineData("rollback_failed", "Download CodePad again", true)]
    public void TheResultOfThePreviousUpdateShowsInWords(string result, string fragment, bool problem)
    {
        (UpdateViewModel model, _) = Create();

        model.ShowPreviousResult("0.1.0", "0.2.0", result);

        Assert.True(model.IsVisible);
        Assert.Contains(fragment, model.Title, StringComparison.Ordinal);
        Assert.Equal(problem, model.IsProblem);
        Assert.True(model.CanDismiss);
    }

    [Fact]
    public async Task EveryStateHasAnAccessibleNameWithTheStateInWords()
    {
        Publish();
        (UpdateViewModel model, UpdateService service) = Create();
        await service.CheckAsync(false, TestContext.Current.CancellationToken);
        Assert.StartsWith("Update: ", model.AccessibleName, StringComparison.Ordinal);

        downloader.Failure = new UpdateDownloadException("network");
        await model.DownloadAsync();
        Assert.StartsWith("Update problem: ", model.AccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureTextsNeverHoldAnAddressOrAPath()
    {
        foreach (string code in new[] { "signature_invalid", "no_signing_key", "digest_mismatch", "size_mismatch", "untrusted_redirect", "network", "timeout", "rate_limited", "no_asset_for_platform", "manifest_invalid", "backup_failed", "not_writable", "archive_changed", "helper_failed", "something_new" })
        {
            string text = UpdateViewModel.FailureText(code, hasOffer: true);

            Assert.DoesNotContain("http", text, StringComparison.Ordinal);
            Assert.DoesNotContain("/", text, StringComparison.Ordinal);
            Assert.DoesNotContain(code.Replace('_', ' '), text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
