namespace Codepad.Application.Updates;

/// <summary>A release that the user can install.</summary>
/// <param name="Version">The new version.</param>
/// <param name="ReleaseUrl">The release page.</param>
/// <param name="ReleaseNotes">The notes for the user.</param>
/// <param name="Asset">The archive for this platform, from the signed manifest.</param>
public sealed record UpdateOffer(SemanticVersion Version, Uri ReleaseUrl, string ReleaseNotes, UpdateAsset Asset);

/// <summary>Why a release is not offered.</summary>
public enum UpdateRejection
{
    /// <summary>The release is a pre-release. v0.1 follows stable releases only.</summary>
    PreRelease,

    /// <summary>The release is the installed version or older. The app never downgrades.</summary>
    NotNewer,

    /// <summary>The manifest has no archive for this platform.</summary>
    NoAssetForPlatform,
}

/// <summary>The result of the selection: an offer, or the reason for none.</summary>
/// <param name="Offer">The offer, or null.</param>
/// <param name="Rejection">The reason when there is no offer.</param>
public sealed record UpdateSelection(UpdateOffer? Offer, UpdateRejection? Rejection);

/// <summary>Chooses whether a verified manifest is an update for this installation.</summary>
public static class UpdateSelector
{
    /// <summary>Applies the rules.</summary>
    /// <param name="installed">The installed version.</param>
    /// <param name="manifest">The verified manifest.</param>
    /// <param name="platform">The platform ID of this machine, for example <c>linux-x64</c>.</param>
    /// <returns>The selection.</returns>
    public static UpdateSelection Select(SemanticVersion installed, UpdateManifest manifest, string platform)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        if (!manifest.Version.IsStable)
        {
            return new UpdateSelection(null, UpdateRejection.PreRelease);
        }

        // The version decides. The date of the upload never does.
        if (manifest.Version <= installed)
        {
            return new UpdateSelection(null, UpdateRejection.NotNewer);
        }

        UpdateAsset? asset = manifest.Assets.FirstOrDefault(item => item.Platform == platform);
        return asset is null
            ? new UpdateSelection(null, UpdateRejection.NoAssetForPlatform)
            : new UpdateSelection(new UpdateOffer(manifest.Version, manifest.ReleaseUrl, manifest.ReleaseNotes, asset), null);
    }
}
