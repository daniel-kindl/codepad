using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Codepad.Application.Updates;

namespace Codepad.Desktop.Updates;

/// <summary>
/// The state of the update notice at the top of the window. It is quiet until there is something to say: a newer release, a
/// result of a check that the user asked for, a failure, or the one-time explanation of the automatic checks. Every
/// step that changes the installation needs a click: download, install and restart. Nothing runs by itself.
/// </summary>
public sealed class UpdateViewModel : INotifyPropertyChanged
{
    private readonly UpdateService service;
    private readonly IUpdateStore store;
    private readonly SemanticVersion installed;
    private readonly Func<Task<bool>> prepareToExit;
    private readonly Action requestExit;
    private readonly Action<bool> setChecksEnabled;
    private readonly Action<Action> post;
    private CancellationTokenSource? download;
    private UpdateStatus status = UpdateStatus.Idle;
    private bool manualCheck;
    private bool promptVisible;
    private string notice = string.Empty;
    private bool noticeIsProblem;

    /// <summary>Initializes a new instance of the <see cref="UpdateViewModel"/> class.</summary>
    /// <param name="service">The update service.</param>
    /// <param name="store">Keeps the first-run explanation.</param>
    /// <param name="installed">The installed version.</param>
    /// <param name="setChecksEnabled">Saves the choice about automatic checks.</param>
    /// <param name="prepareToExit">Saves every open experiment. It returns false when something could not be saved and the app must stay open.</param>
    /// <param name="requestExit">Closes the app. The helper takes over after the exit.</param>
    /// <param name="post">Runs an action on the UI thread. Null uses the Avalonia UI thread.</param>
    public UpdateViewModel(UpdateService service, IUpdateStore store, SemanticVersion installed, Action<bool> setChecksEnabled, Func<Task<bool>> prepareToExit, Action requestExit, Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(setChecksEnabled);
        ArgumentNullException.ThrowIfNull(prepareToExit);
        ArgumentNullException.ThrowIfNull(requestExit);
        this.service = service;
        this.store = store;
        this.installed = installed;
        this.setChecksEnabled = setChecksEnabled;
        this.prepareToExit = prepareToExit;
        this.requestExit = requestExit;
        this.post = post ?? (action => Dispatcher.UIThread.Post(action));
        service.StatusChanged += OnStatusChanged;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets a value indicating whether the notice shows.</summary>
    public bool IsVisible => promptVisible || notice.Length > 0 || ShownState(status.State) || ShowsChecking;

    /// <summary>Gets the one-line headline.</summary>
    public string Title => promptVisible ? "Check for updates?" : notice.Length > 0 ? notice : status.State switch
    {
        UpdateState.Checking => "Checking for updates…",
        UpdateState.UpToDate => string.Create(CultureInfo.InvariantCulture, $"CodePad {installed} is the newest version."),
        UpdateState.Available => string.Create(CultureInfo.InvariantCulture, $"CodePad {status.Offer?.Version} is available. You have {installed}."),
        UpdateState.Downloading => string.Create(CultureInfo.InvariantCulture, $"Downloading CodePad {status.Offer?.Version}… {Percent}%"),
        UpdateState.Verifying => "Checking the download…",
        UpdateState.ReadyToInstall => string.Create(CultureInfo.InvariantCulture, $"CodePad {status.Offer?.Version} is ready to install."),
        UpdateState.Installing => "Installing the update. CodePad closes now.",
        UpdateState.Failed => FailureText(status.ErrorCode, status.Offer is not null),
        _ => string.Empty,
    };

    /// <summary>Gets the second line: what the choice means, the size, or what happens next.</summary>
    public string Detail => promptVisible
        ? "CodePad can look for a newer stable version once a day. It never downloads or installs without your click. You can change this in the settings."
        : notice.Length > 0 ? string.Empty : status.State switch
        {
            UpdateState.Available when status.ManualInstallReason is string reason => reason,
            UpdateState.Available => SizeText.Length > 0 ? $"Download size: {SizeText}. Nothing changes until you choose Download and install." : "Nothing changes until you choose Download and install.",
            UpdateState.ReadyToInstall => "CodePad saves your work and closes. Then it installs the update and starts again. If the new version does not start, CodePad goes back to this one.",
            UpdateState.Downloading => "You can cancel the download. CodePad stays as it is.",
            UpdateState.Failed => "CodePad is unchanged. You can try again later.",
            _ => string.Empty,
        };

    /// <summary>Gets the release notes of the offer, or an empty string.</summary>
    public string ReleaseNotes => status.State is UpdateState.Available or UpdateState.ReadyToInstall && notice.Length == 0 ? status.Offer?.ReleaseNotes ?? string.Empty : string.Empty;

    /// <summary>Gets the size of the download as text, or an empty string when unknown.</summary>
    public string SizeText => status.Offer?.Asset.Size is long size and > 0 ? FormatSize(size) : string.Empty;

    /// <summary>Gets the progress from 0 to 100 while downloading.</summary>
    public double Percent => Math.Round((status.Progress?.Fraction ?? 0) * 100);

    /// <summary>Gets a value indicating whether the progress bar shows.</summary>
    public bool ShowProgress => notice.Length == 0 && !promptVisible && status.State is UpdateState.Downloading or UpdateState.Verifying;

    /// <summary>Gets a value indicating whether the progress is not known. The bar then shows activity only.</summary>
    public bool IsIndeterminate => status.State is UpdateState.Verifying or UpdateState.Checking;

    /// <summary>Gets a value indicating whether the notice is a problem. A problem has the word "problem" in its name for a screen reader, and a marker in the text.</summary>
    public bool IsProblem => status.State == UpdateState.Failed || noticeIsProblem;

    /// <summary>Gets the accessible name of the notice. It has the state in words.</summary>
    public string AccessibleName => (IsProblem ? "Update problem: " : "Update: ") + Title + " " + Detail;

    /// <summary>Gets a value indicating whether the user can start the download.</summary>
    public bool CanDownload => notice.Length == 0 && !promptVisible && status.State == UpdateState.Available && status.ManualInstallReason is null;

    /// <summary>Gets a value indicating whether the user can install the staged update.</summary>
    public bool CanInstall => notice.Length == 0 && !promptVisible && status.State == UpdateState.ReadyToInstall;

    /// <summary>Gets a value indicating whether the user can cancel the download.</summary>
    public bool CanCancel => notice.Length == 0 && !promptVisible && status.State == UpdateState.Downloading;

    /// <summary>Gets a value indicating whether the user can try the failed step again.</summary>
    public bool CanRetry => notice.Length == 0 && !promptVisible && status.State == UpdateState.Failed && status.Offer is not null && status.ErrorCode != "no_asset_for_platform";

    /// <summary>Gets a value indicating whether the user can hide the offer for now.</summary>
    public bool CanLater => notice.Length == 0 && !promptVisible && status.State is UpdateState.Available or UpdateState.ReadyToInstall;

    /// <summary>Gets a value indicating whether the user can close the notice.</summary>
    public bool CanDismiss => promptVisible is false && (notice.Length > 0 || status.State is UpdateState.UpToDate or UpdateState.Failed);

    /// <summary>Gets a value indicating whether the first-run explanation shows its two buttons.</summary>
    public bool ShowPrompt => promptVisible;

    /// <summary>Gets the page of the release, or null. A user who must install by hand can read it there.</summary>
    public Uri? ReleaseUrl => status.Offer?.ReleaseUrl;

    /// <summary>Shows the one-time explanation of the automatic checks, if the user has not seen it.</summary>
    /// <returns>A task that ends when the notice is ready.</returns>
    public async Task ShowFirstRunPromptIfNeededAsync()
    {
        if (!await store.GetPromptShownAsync().ConfigureAwait(true))
        {
            promptVisible = true;
            RaiseAll();
        }
    }

    /// <summary>Records the choice from the first-run explanation. Both buttons close it, and the choice is saved.</summary>
    /// <param name="keepChecking">True to keep the daily check. False to turn it off.</param>
    /// <returns>A task that ends when the choice is saved.</returns>
    public async Task AnswerPromptAsync(bool keepChecking)
    {
        setChecksEnabled(keepChecking);
        promptVisible = false;
        RaiseAll();
        await store.SetPromptShownAsync().ConfigureAwait(true);
    }

    /// <summary>Shows what the helper reported after the last update: the new version, or a rollback.</summary>
    /// <param name="fromVersion">The old version.</param>
    /// <param name="toVersion">The new version.</param>
    /// <param name="status">The helper status: <c>updated</c>, <c>rolled_back</c>, <c>not_applied</c> or <c>rollback_failed</c>.</param>
    public void ShowPreviousResult(string fromVersion, string toVersion, string status)
    {
        (notice, noticeIsProblem) = status switch
        {
            "updated" => ($"CodePad was updated to {toVersion}.", false),
            "rolled_back" => ($"The update to {toVersion} did not start, so CodePad went back to {fromVersion}. Your experiments are safe. You can try the update again later.", true),
            "not_applied" => ($"The update to {toVersion} was not applied and CodePad is still {fromVersion}. You can try again later.", true),
            "rollback_failed" => ($"The update to {toVersion} failed and CodePad could not go back by itself. Download CodePad again from the release page. Your experiments are safe.", true),
            _ => (string.Empty, false),
        };
        RaiseAll();
    }

    /// <summary>
    /// The work at startup, after the window is open: the one-time explanation, then a silent check when one is due. It waits a few
    /// seconds first, so the check does not compete with the start. A failure never reaches the user and never stops the app.
    /// </summary>
    /// <param name="checksEnabled">Tells whether the user allows automatic checks. It is read when the check is due.</param>
    /// <param name="delay">How long to wait before the check.</param>
    /// <param name="cancellationToken">Stops the wait and the check.</param>
    /// <returns>A task that ends when the work ends.</returns>
    public async Task RunStartupAsync(Func<bool> checksEnabled, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checksEnabled);
        try
        {
            await ShowFirstRunPromptIfNeededAsync().ConfigureAwait(true);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(true);
            await service.CheckIfDueAsync(checksEnabled(), cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // The app is closing.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The service already catches its own failures. This is a bug. The app goes on without updates.
            _ = exception;
        }
    }

    /// <summary>Checks now because the user asked. The result always shows.</summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A task that ends when the status shows the result.</returns>
    public async Task CheckNowAsync(CancellationToken cancellationToken = default)
    {
        notice = string.Empty;
        manualCheck = true;
        await service.CheckAsync(userRequested: true, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Downloads and checks the offered update.</summary>
    /// <returns>A task that ends when the download ends.</returns>
    public async Task DownloadAsync()
    {
        using CancellationTokenSource source = new CancellationTokenSource();
        download = source;
        try
        {
            await service.DownloadAsync(source.Token).ConfigureAwait(true);
        }
        finally
        {
            download = null;
        }
    }

    /// <summary>Tries the failed step again: the download, or the hand-off when the file is already staged.</summary>
    /// <returns>A task that ends when the step ends.</returns>
    public async Task RetryAsync()
    {
        if (!CanRetry)
        {
            return;
        }

        if (status.Staged is not null)
        {
            await InstallRetryAsync().ConfigureAwait(true);
        }
        else
        {
            await DownloadAsync().ConfigureAwait(true);
        }
    }

    private async Task InstallRetryAsync()
    {
        if (!await prepareToExit().ConfigureAwait(true))
        {
            return;
        }

        await service.InstallAsync().ConfigureAwait(true);
        if (service.Status.State == UpdateState.Installing)
        {
            requestExit();
        }
    }

    /// <summary>Cancels the download. CodePad stays as it is.</summary>
    public void CancelDownload() => download?.Cancel();

    /// <summary>
    /// Saves all work, hands the verified update to the helper and closes the app. When work cannot be saved, the update
    /// does not go on and the app stays open.
    /// </summary>
    /// <returns>A task that ends when the app is asked to close, or when the install stopped.</returns>
    public async Task InstallAsync()
    {
        if (!CanInstall)
        {
            return;
        }

        // The session and every edit are saved first. The helper restarts the app, and the session restores the tabs.
        if (!await prepareToExit().ConfigureAwait(true))
        {
            notice = "Some work could not be saved, so CodePad did not close. Fix the save problem on the tab, then try again.";
            noticeIsProblem = true;
            RaiseAll();
            return;
        }

        await service.InstallAsync().ConfigureAwait(true);
        if (service.Status.State == UpdateState.Installing)
        {
            requestExit();
        }
    }

    /// <summary>Hides the offer until the next check.</summary>
    public void Later() => service.Postpone();

    /// <summary>Closes the notice.</summary>
    public void Dismiss()
    {
        notice = string.Empty;
        noticeIsProblem = false;
        manualCheck = false;
        if (status.State is UpdateState.UpToDate or UpdateState.Failed)
        {
            service.Postpone();
        }

        RaiseAll();
    }

    private static bool ShownState(UpdateState state) => state is not (UpdateState.Idle or UpdateState.Checking);

    private bool ShowsChecking => manualCheck && status.State == UpdateState.Checking;

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d / 1024d:0.#} MB"),
        >= 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes"),
    };

    /// <summary>Gets the sentence for a failure code. The codes never reach the user as they are.</summary>
    /// <param name="code">The code.</param>
    /// <param name="hasOffer">True when an offer is known, so a retry makes sense.</param>
    /// <returns>The sentence.</returns>
    internal static string FailureText(string? code, bool hasOffer) => code switch
    {
        "signature_invalid" => "The update information could not be verified, so CodePad ignored it. Nothing was installed.",
        "no_signing_key" => "This build of CodePad cannot verify updates, so it does not install them.",
        "digest_mismatch" => "The download did not match its checksum, so CodePad deleted it. Nothing was installed.",
        "size_mismatch" => "The download had the wrong size, so CodePad deleted it. Nothing was installed.",
        "untrusted_redirect" or "untrusted_url" or "manifest_untrusted_url" => "The download address is not an official CodePad address, so CodePad did not use it.",
        "network" or "timeout" => "CodePad could not reach GitHub. Check your connection and try again.",
        "rate_limited" => "GitHub asked CodePad to wait. Try again in a while.",
        "no_asset_for_platform" => "This release has no download for your system.",
        "manifest_invalid" or "manifest_unsupported" or "manifest_too_large" or "release_invalid" or "manifest_version_mismatch" => "The update information is not valid, so CodePad ignored it.",
        "backup_failed" => "CodePad could not make a safety copy of your data, so it did not install the update.",
        "not_writable" => "CodePad cannot change its own folder, so it cannot install the update.",
        "archive_changed" or "archive_invalid" or "archive_incomplete" or "archive_entry_not_allowed" or "archive_too_large" or "archive_too_many_entries" => "The downloaded files are not valid, so CodePad did not install them.",
        "helper_failed" or "io_error" => "CodePad could not start the update. Nothing was changed.",
        _ => hasOffer ? "The update did not work. Nothing was changed." : "CodePad could not check for updates.",
    };

    private void OnStatusChanged(UpdateStatus next) => post(() =>
    {
        status = next;
        if (next.State is UpdateState.Idle or UpdateState.UpToDate or UpdateState.Available or UpdateState.Failed)
        {
            manualCheck = next.State != UpdateState.Idle && manualCheck;
        }

        RaiseAll();
    });

    private void RaiseAll()
    {
        foreach (string name in new[]
        {
            nameof(IsVisible), nameof(Title), nameof(Detail), nameof(ReleaseNotes), nameof(SizeText), nameof(Percent), nameof(ShowProgress), nameof(IsIndeterminate),
            nameof(IsProblem), nameof(AccessibleName), nameof(CanDownload), nameof(CanInstall), nameof(CanCancel), nameof(CanRetry), nameof(CanLater), nameof(CanDismiss), nameof(ShowPrompt), nameof(ReleaseUrl),
        })
        {
            Raise(name);
        }
    }

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
