namespace Codepad.Application.Updates;

/// <summary>The signed manifest as the source gives it: the bytes that were signed, and the signature.</summary>
/// <param name="ManifestBytes">The exact bytes of the manifest file.</param>
/// <param name="Signature">The signature of those bytes.</param>
/// <param name="ReleaseTag">The tag of the GitHub release that held the files, or null. After the signature check, the version in the manifest must match the tag.</param>
public sealed record SignedManifest(byte[] ManifestBytes, byte[] Signature, string? ReleaseTag = null);

/// <summary>An update source failed. The code is safe to log, for example <c>network</c>, <c>rate_limited</c> or <c>untrusted_redirect</c>.</summary>
public sealed class UpdateSourceException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="UpdateSourceException"/> class.</summary>
    /// <param name="code">The reason code.</param>
    /// <param name="innerException">The cause.</param>
    public UpdateSourceException(string code, Exception? innerException = null)
        : base($"The update source failed: {code}.", innerException)
    {
        Code = code;
    }

    /// <summary>Gets the reason code.</summary>
    public string Code { get; }
}

/// <summary>Finds the latest stable release of the project. The source never installs anything.</summary>
public interface IUpdateSource
{
    /// <summary>Gets the signed manifest of the latest stable release.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The signed manifest, or null when the project has no stable release.</returns>
    /// <exception cref="UpdateSourceException">The source cannot be reached or gave a bad answer.</exception>
    Task<SignedManifest?> FetchLatestAsync(CancellationToken cancellationToken);
}

/// <summary>Checks that a manifest comes from the publisher. The public key comes with the app.</summary>
public interface IManifestVerifier
{
    /// <summary>Gets a value indicating whether the app has a public key. Without a key no update can be trusted.</summary>
    bool HasKey { get; }

    /// <summary>Checks a signature.</summary>
    /// <param name="manifestBytes">The exact bytes of the manifest.</param>
    /// <param name="signature">The signature.</param>
    /// <returns>True only for a valid signature of the publisher.</returns>
    bool Verify(ReadOnlySpan<byte> manifestBytes, ReadOnlySpan<byte> signature);
}

/// <summary>An update that was downloaded and checked, in a folder outside the installation.</summary>
/// <param name="Offer">The offer that this download belongs to.</param>
/// <param name="FilePath">The full path of the verified archive.</param>
public sealed record StagedUpdate(UpdateOffer Offer, string FilePath);

/// <summary>The progress of a download.</summary>
/// <param name="BytesReceived">The bytes that arrived.</param>
/// <param name="TotalBytes">The size from the signed manifest.</param>
public readonly record struct UpdateProgress(long BytesReceived, long TotalBytes)
{
    /// <summary>Gets the part that arrived, from 0 to 1.</summary>
    public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)BytesReceived / TotalBytes, 0, 1);
}

/// <summary>A download failed or did not pass a check. The code is safe to log.</summary>
public sealed class UpdateDownloadException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="UpdateDownloadException"/> class.</summary>
    /// <param name="code">The reason code, for example <c>digest_mismatch</c>, <c>size_mismatch</c>, <c>network</c> or <c>untrusted_redirect</c>.</param>
    /// <param name="innerException">The cause.</param>
    public UpdateDownloadException(string code, Exception? innerException = null)
        : base($"The update download failed: {code}.", innerException)
    {
        Code = code;
    }

    /// <summary>Gets the reason code.</summary>
    public string Code { get; }
}

/// <summary>Downloads an archive into a staging folder and checks its size and its SHA-256 digest against the signed manifest.</summary>
public interface IUpdateDownloader
{
    /// <summary>Downloads and checks an archive.</summary>
    /// <param name="offer">The offer with the asset from the signed manifest.</param>
    /// <param name="progress">Receives the progress. Null ignores it.</param>
    /// <param name="cancellationToken">Cancels the download. The partial file is deleted.</param>
    /// <returns>The staged update. The file passed all checks.</returns>
    /// <exception cref="UpdateDownloadException">The download failed or a check failed.</exception>
    Task<StagedUpdate> DownloadAsync(UpdateOffer offer, IProgress<UpdateProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>The handoff to the platform could not start. The code is safe to log.</summary>
public sealed class UpdateInstallException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="UpdateInstallException"/> class.</summary>
    /// <param name="code">The reason code, for example <c>not_writable</c>, <c>archive_invalid</c> or <c>helper_failed</c>.</param>
    /// <param name="innerException">The cause.</param>
    public UpdateInstallException(string code, Exception? innerException = null)
        : base($"The update could not be installed: {code}.", innerException)
    {
        Code = code;
    }

    /// <summary>Gets the reason code.</summary>
    public string Code { get; }
}

/// <summary>
/// The platform part of an update. It prepares the new files next to the installation and hands the swap to a helper
/// that runs after CodePad exits. The helper restores the old version when the new one does not start.
/// </summary>
public interface IUpdateInstaller
{
    /// <summary>Checks that the app can update itself here. An installation that the user cannot write needs an update by hand.</summary>
    /// <returns>Null when the update can go on, or a sentence that says why not.</returns>
    string? CheckSupported();

    /// <summary>Prepares the files and starts the helper. The caller must exit the app after this method returns.</summary>
    /// <param name="staged">The verified archive.</param>
    /// <param name="cancellationToken">Cancels the preparation. After the helper started, it cannot be canceled.</param>
    /// <returns>A task that ends when the helper waits for the app to exit.</returns>
    /// <exception cref="UpdateInstallException">The preparation failed. The installation is unchanged.</exception>
    Task HandOffAsync(StagedUpdate staged, CancellationToken cancellationToken);
}

/// <summary>Keeps the small facts of the updater between runs of the app.</summary>
public interface IUpdateStore
{
    /// <summary>Gets the time of the last check, or null.</summary>
    /// <returns>The time.</returns>
    Task<DateTimeOffset?> GetLastCheckAsync();

    /// <summary>Saves the time of a check.</summary>
    /// <param name="time">The time.</param>
    /// <returns>A task.</returns>
    Task SetLastCheckAsync(DateTimeOffset time);

    /// <summary>Tells whether the app already explained the update checks to the user.</summary>
    /// <returns>True after the explanation.</returns>
    Task<bool> GetPromptShownAsync();

    /// <summary>Remembers that the app explained the update checks.</summary>
    /// <returns>A task.</returns>
    Task SetPromptShownAsync();
}
