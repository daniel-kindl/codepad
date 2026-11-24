namespace Codepad.Application.Updates;

/// <summary>One file of a release: the archive for one platform.</summary>
/// <param name="Platform">The platform ID, <c>linux-x64</c> or <c>win-x64</c>.</param>
/// <param name="Name">The file name. It is a plain name, never a path.</param>
/// <param name="Size">The size in bytes.</param>
/// <param name="Sha256">The SHA-256 digest as 64 lower-case hex digits.</param>
/// <param name="Url">The download address. It is on the official release downloads of the project.</param>
public sealed record UpdateAsset(string Platform, string Name, long Size, string Sha256, Uri Url);

/// <summary>The signed description of one release. The signature is checked before anyone reads this model.</summary>
/// <param name="Version">The version of the release.</param>
/// <param name="ReleaseUrl">The page of the release on GitHub.</param>
/// <param name="ReleaseNotes">The notes for the user, cut at the limit.</param>
/// <param name="PublishedAt">When the release was published, as the manifest says. It is information only and never orders versions.</param>
/// <param name="Assets">The assets for the platforms that this app knows.</param>
public sealed record UpdateManifest(SemanticVersion Version, Uri ReleaseUrl, string ReleaseNotes, DateTimeOffset? PublishedAt, IReadOnlyList<UpdateAsset> Assets);

/// <summary>The manifest is not acceptable. The code is safe to log. The message never holds the content of the manifest.</summary>
public sealed class UpdateManifestException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="UpdateManifestException"/> class.</summary>
    /// <param name="code">The reason code, for example <c>manifest_invalid</c>.</param>
    public UpdateManifestException(string code)
        : base($"The update manifest is not acceptable: {code}.")
    {
        Code = code;
    }

    /// <summary>Gets the reason code.</summary>
    public string Code { get; }
}
