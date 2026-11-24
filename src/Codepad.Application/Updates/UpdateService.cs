namespace Codepad.Application.Updates;

/// <summary>Where the updater is in its work.</summary>
public enum UpdateState
{
    /// <summary>Nothing to say. This is also the state after a silent check that found nothing.</summary>
    Idle,

    /// <summary>The app asks GitHub for the latest release.</summary>
    Checking,

    /// <summary>The user asked for a check and the installed version is the newest.</summary>
    UpToDate,

    /// <summary>A newer stable release is available. The user decides.</summary>
    Available,

    /// <summary>The archive is downloading.</summary>
    Downloading,

    /// <summary>The archive arrived and the app checks its size and its digest.</summary>
    Verifying,

    /// <summary>The verified archive is in the staging folder. The user can restart to install it.</summary>
    ReadyToInstall,

    /// <summary>The helper has the update. The app must exit.</summary>
    Installing,

    /// <summary>A step failed. The code says which. The installed version is untouched.</summary>
    Failed,
}

/// <summary>The status of the updater for the UI.</summary>
/// <param name="State">The state.</param>
/// <param name="Offer">The release on offer, or null.</param>
/// <param name="Progress">The progress while downloading, or null.</param>
/// <param name="ErrorCode">The code of the failure, or null.</param>
/// <param name="ManualInstallReason">Why the user must install by hand, or null when the app can install.</param>
/// <param name="Staged">The verified archive when the state allows an install, or null.</param>
public sealed record UpdateStatus(UpdateState State, UpdateOffer? Offer, UpdateProgress? Progress, string? ErrorCode, string? ManualInstallReason, StagedUpdate? Staged)
{
    /// <summary>Gets the status before any check.</summary>
    public static UpdateStatus Idle { get; } = new UpdateStatus(UpdateState.Idle, null, null, null, null, null);
}

/// <summary>A fact for the log. It has a name, a version and a code. It never has a URL, a path or a token.</summary>
/// <param name="Name">The event name, for example <c>update.available</c>.</param>
/// <param name="Version">The release version, or null.</param>
/// <param name="Code">A reason code, or null.</param>
public sealed record UpdateEvent(string Name, string? Version, string? Code);

/// <summary>
/// Coordinates the update: check, verify the signature of the manifest, choose, download, verify the archive and hand off to
/// the installer. It never installs without the explicit call from the user. One operation runs at a time. An automatic
/// check is silent: it shows something only when a newer release exists. A failure never stops the app.
/// </summary>
public sealed class UpdateService
{
    private static readonly TimeSpan s_interval = TimeSpan.FromHours(24);

    private readonly IUpdateSource source;
    private readonly IManifestVerifier verifier;
    private readonly IUpdateDownloader downloader;
    private readonly IUpdateInstaller installer;
    private readonly IUpdateStore store;
    private readonly TimeProvider clock;
    private readonly SemanticVersion installed;
    private readonly string platform;
    private readonly Action<UpdateEvent> report;
    private readonly object sync = new object();
    private UpdateStatus status = UpdateStatus.Idle;
    private bool busy;

    /// <summary>Initializes a new instance of the <see cref="UpdateService"/> class.</summary>
    /// <param name="source">Finds the latest release.</param>
    /// <param name="verifier">Checks the signature of the manifest.</param>
    /// <param name="downloader">Downloads and checks the archive.</param>
    /// <param name="installer">Hands the update to the platform.</param>
    /// <param name="store">Keeps the time of the last check.</param>
    /// <param name="clock">The time source.</param>
    /// <param name="installed">The installed version.</param>
    /// <param name="platform">The platform ID, for example <c>linux-x64</c>.</param>
    /// <param name="report">Receives the facts for the log. Null ignores them.</param>
    public UpdateService(IUpdateSource source, IManifestVerifier verifier, IUpdateDownloader downloader, IUpdateInstaller installer, IUpdateStore store, TimeProvider clock, SemanticVersion installed, string platform, Action<UpdateEvent>? report = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        this.source = source;
        this.verifier = verifier;
        this.downloader = downloader;
        this.installer = installer;
        this.store = store;
        this.clock = clock;
        this.installed = installed;
        this.platform = platform;
        this.report = report ?? (_ => { });
    }

    /// <summary>Raised after each change of the status. It can run on any thread.</summary>
    public event Action<UpdateStatus>? StatusChanged;

    /// <summary>Gets the current status.</summary>
    public UpdateStatus Status
    {
        get
        {
            lock (sync)
            {
                return status;
            }
        }
    }

    /// <summary>Checks only when the user allows it and the last check is at least 24 hours old. The check is silent.</summary>
    /// <param name="enabled">Whether the user allows automatic checks.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>True when a check ran.</returns>
    public async Task<bool> CheckIfDueAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!enabled)
        {
            return false;
        }

        DateTimeOffset? last = await store.GetLastCheckAsync().ConfigureAwait(false);
        if (last is DateTimeOffset previous && clock.GetUtcNow() - previous < s_interval && clock.GetUtcNow() >= previous)
        {
            return false;
        }

        await CheckAsync(userRequested: false, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Asks GitHub for the latest stable release and checks it.</summary>
    /// <param name="userRequested">True when the user asked. A manual check shows its result, also "up to date" and a failure.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A task that ends when the status shows the result.</returns>
    public async Task CheckAsync(bool userRequested, CancellationToken cancellationToken = default)
    {
        if (!TryBegin(UpdateState.Checking))
        {
            return;
        }

        try
        {
            // The attempt counts, also when it fails, so a broken network or a rate limit does not make the app call again at once.
            await store.SetLastCheckAsync(clock.GetUtcNow()).ConfigureAwait(false);
            if (!verifier.HasKey)
            {
                Fail("no_signing_key", userRequested, null);
                return;
            }

            SignedManifest? signed = await source.FetchLatestAsync(cancellationToken).ConfigureAwait(false);
            if (signed is null)
            {
                Finish(userRequested ? new UpdateStatus(UpdateState.UpToDate, null, null, null, null, null) : UpdateStatus.Idle);
                return;
            }

            // The signature comes first. Nothing in the manifest is read before the publisher is known.
            if (!verifier.Verify(signed.ManifestBytes, signed.Signature))
            {
                Fail("signature_invalid", userRequested: true, null);
                return;
            }

            UpdateManifest manifest = UpdateManifestParser.Parse(signed.ManifestBytes);

            // The tag of the release page is not signed. The manifest is. They must agree, so an old signed manifest cannot be shown as a new release.
            if (signed.ReleaseTag is string tag && !(SemanticVersion.TryParse(tag, out SemanticVersion? tagged) && tagged == manifest.Version))
            {
                Fail("manifest_version_mismatch", userRequested: true, null);
                return;
            }

            UpdateSelection selection = UpdateSelector.Select(installed, manifest, platform);
            if (selection.Offer is UpdateOffer offer)
            {
                report(new UpdateEvent("update.available", offer.Version.ToString(), null));
                Finish(new UpdateStatus(UpdateState.Available, offer, null, null, installer.CheckSupported(), null));
                return;
            }

            if (selection.Rejection == UpdateRejection.NoAssetForPlatform)
            {
                Fail("no_asset_for_platform", userRequested, manifest.Version.ToString());
                return;
            }

            report(new UpdateEvent("update.none", manifest.Version.ToString(), selection.Rejection?.ToString()));
            Finish(userRequested ? new UpdateStatus(UpdateState.UpToDate, null, null, null, null, null) : UpdateStatus.Idle);
        }
        catch (OperationCanceledException)
        {
            Finish(UpdateStatus.Idle);
        }
        catch (UpdateManifestException exception)
        {
            Fail(exception.Code, userRequested: true, null);
        }
        catch (UpdateSourceException exception)
        {
            Fail(exception.Code, userRequested, null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A bug or an unexpected network error. The message can hold an address or a token. Only the type goes out.
            Fail(exception.GetType().Name, userRequested, null);
        }
    }

    /// <summary>Downloads the offered archive, checks it and stages it. It can run again after a failure.</summary>
    /// <param name="cancellationToken">Cancels the download. The status then goes back to available.</param>
    /// <returns>A task that ends when the status shows the result.</returns>
    public async Task DownloadAsync(CancellationToken cancellationToken = default)
    {
        UpdateStatus before = Status;
        if (before.Offer is not UpdateOffer offer || before.State is not (UpdateState.Available or UpdateState.Failed) || before.ManualInstallReason is not null || before.Staged is not null)
        {
            return;
        }

        if (!TryBegin(UpdateState.Downloading, offer))
        {
            return;
        }

        string? manual = installer.CheckSupported();
        if (manual is not null)
        {
            Finish(new UpdateStatus(UpdateState.Available, offer, null, null, manual, null));
            return;
        }

        try
        {
            StagedUpdate staged = await downloader.DownloadAsync(offer, new SynchronousProgress(value => Publish(new UpdateStatus(UpdateState.Downloading, offer, value, null, null, null))), cancellationToken).ConfigureAwait(false);
            Publish(new UpdateStatus(UpdateState.Verifying, offer, null, null, null, null));
            report(new UpdateEvent("update.staged", offer.Version.ToString(), null));
            Finish(new UpdateStatus(UpdateState.ReadyToInstall, offer, null, null, null, staged));
        }
        catch (OperationCanceledException)
        {
            Finish(new UpdateStatus(UpdateState.Available, offer, null, null, null, null));
        }
        catch (UpdateDownloadException exception)
        {
            FailWithOffer(exception.Code, offer, null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            FailWithOffer(exception.GetType().Name, offer, null);
        }
    }

    /// <summary>Hands the verified update to the installer. The caller must close the app after this returns without a failure.</summary>
    /// <param name="cancellationToken">Cancels the preparation.</param>
    /// <returns>A task that ends when the helper has the update.</returns>
    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        UpdateStatus before = Status;
        StagedUpdate? staged = before.Staged;
        if (staged is null || before.State is not (UpdateState.ReadyToInstall or UpdateState.Failed))
        {
            return;
        }

        if (!TryBegin(UpdateState.Installing, staged.Offer))
        {
            return;
        }

        try
        {
            await installer.HandOffAsync(staged, cancellationToken).ConfigureAwait(false);
            report(new UpdateEvent("update.handed_off", staged.Offer.Version.ToString(), null));
            Finish(new UpdateStatus(UpdateState.Installing, staged.Offer, null, null, null, staged));
        }
        catch (OperationCanceledException)
        {
            Finish(new UpdateStatus(UpdateState.ReadyToInstall, staged.Offer, null, null, null, staged));
        }
        catch (UpdateInstallException exception)
        {
            FailWithOffer(exception.Code, staged.Offer, staged);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            FailWithOffer(exception.GetType().Name, staged.Offer, staged);
        }
    }

    /// <summary>Hides the offer. A later check can offer it again.</summary>
    public void Postpone()
    {
        lock (sync)
        {
            if (busy || status.State is UpdateState.Downloading or UpdateState.Verifying or UpdateState.Installing)
            {
                return;
            }
        }

        Publish(UpdateStatus.Idle);
    }

    private bool TryBegin(UpdateState state, UpdateOffer? offer = null)
    {
        lock (sync)
        {
            if (busy)
            {
                return false;
            }

            busy = true;
        }

        Publish(new UpdateStatus(state, offer, null, null, null, null));
        return true;
    }

    private void Finish(UpdateStatus result)
    {
        lock (sync)
        {
            busy = false;
        }

        Publish(result);
    }

    private void Fail(string code, bool userRequested, string? version)
    {
        report(new UpdateEvent("update.check_failed", version, code));
        Finish(userRequested ? new UpdateStatus(UpdateState.Failed, null, null, code, null, null) : UpdateStatus.Idle);
    }

    private void FailWithOffer(string code, UpdateOffer offer, StagedUpdate? staged)
    {
        report(new UpdateEvent("update.failed", offer.Version.ToString(), code));
        Finish(new UpdateStatus(UpdateState.Failed, offer, null, code, null, staged));
    }

    private void Publish(UpdateStatus next)
    {
        lock (sync)
        {
            status = next;
        }

        StatusChanged?.Invoke(next);
    }

    // Progress<T> posts to the thread pool and can deliver out of order. This one runs the action at once, in order.
    private sealed class SynchronousProgress(Action<UpdateProgress> action) : IProgress<UpdateProgress>
    {
        public void Report(UpdateProgress value) => action(value);
    }
}
