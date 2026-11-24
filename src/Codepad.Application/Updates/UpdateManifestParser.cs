using System.Globalization;
using System.Text.Json;

namespace Codepad.Application.Updates;

/// <summary>
/// Reads the update manifest. The caller has already checked the signature. The parser still treats the content as
/// untrusted: it checks the sizes, the names, the digests and that each address is an official GitHub release download of this
/// project for this version. A manifest that breaks a rule is rejected as a whole.
/// </summary>
public static class UpdateManifestParser
{
    /// <summary>The newest manifest schema that this app reads.</summary>
    public static readonly int SchemaVersion = 1;

    /// <summary>The updater protocol that this app speaks. A manifest for another protocol needs another app.</summary>
    public static readonly int Protocol = 1;

    /// <summary>The largest manifest in bytes.</summary>
    public static readonly int MaxBytes = 256 * 1024;

    /// <summary>The largest asset in bytes (1 GiB).</summary>
    public static readonly long MaxAssetBytes = 1024L * 1024 * 1024;

    /// <summary>The largest number of assets.</summary>
    public static readonly int MaxAssets = 16;

    /// <summary>The longest release notes in characters.</summary>
    public static readonly int MaxNotesChars = 20_000;

    private const string REPOSITORY_PATH = "/daniel-kindl/codepad/";
    private const string DOWNLOAD_PATH = REPOSITORY_PATH + "releases/download/";
    private const string TAG_PATH = REPOSITORY_PATH + "releases/tag/";
    private const string OFFICIAL_HOST = "github.com";

    private static readonly string[] s_platforms = ["linux-x64", "win-x64"];

    private static readonly string[] s_reservedNames = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "LPT1", "LPT2", "LPT3"];

    /// <summary>Reads a manifest.</summary>
    /// <param name="bytes">The UTF-8 JSON.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="UpdateManifestException">The manifest breaks a rule.</exception>
    public static UpdateManifest Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxBytes)
        {
            throw new UpdateManifestException("manifest_too_large");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            return ParseRoot(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            throw new UpdateManifestException("manifest_invalid");
        }
    }

    private static UpdateManifest ParseRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new UpdateManifestException("manifest_invalid");
        }

        int schema = RequiredInt(root, "schemaVersion");
        int protocol = RequiredInt(root, "protocol");
        if (schema != SchemaVersion || protocol != Protocol)
        {
            throw new UpdateManifestException("manifest_unsupported");
        }

        if (!SemanticVersion.TryParse(RequiredString(root, "version"), out SemanticVersion? version))
        {
            throw new UpdateManifestException("manifest_invalid");
        }

        Uri releaseUrl = OfficialUrl(RequiredString(root, "releaseUrl"), TAG_PATH);
        string notes = root.TryGetProperty("releaseNotes", out JsonElement notesElement) && notesElement.ValueKind == JsonValueKind.String ? notesElement.GetString() ?? string.Empty : string.Empty;
        if (notes.Length > MaxNotesChars)
        {
            notes = notes[..MaxNotesChars];
        }

        DateTimeOffset? published = root.TryGetProperty("publishedAt", out JsonElement publishedElement)
            && publishedElement.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(publishedElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset when)
            ? when
            : null;

        if (!root.TryGetProperty("assets", out JsonElement assetsElement) || assetsElement.ValueKind != JsonValueKind.Array || assetsElement.GetArrayLength() > MaxAssets)
        {
            throw new UpdateManifestException("manifest_invalid");
        }

        List<UpdateAsset> assets = [];
        foreach (JsonElement element in assetsElement.EnumerateArray())
        {
            UpdateAsset asset = ParseAsset(element, version!);
            if (!s_platforms.Contains(asset.Platform, StringComparer.Ordinal))
            {
                // A platform that this app does not know is not an error. A newer app may know it.
                continue;
            }

            if (assets.Any(existing => existing.Platform == asset.Platform))
            {
                throw new UpdateManifestException("manifest_invalid");
            }

            assets.Add(asset);
        }

        return new UpdateManifest(version!, releaseUrl, notes, published, assets);
    }

    private static UpdateAsset ParseAsset(JsonElement element, SemanticVersion version)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new UpdateManifestException("manifest_invalid");
        }

        string platform = RequiredString(element, "platform");
        string name = RequiredString(element, "name");
        long size = RequiredLong(element, "size");
        string digest = RequiredString(element, "sha256");
        Uri url = OfficialUrl(RequiredString(element, "url"), DOWNLOAD_PATH);

        if (!IsPlainFileName(name) || size < 1 || size > MaxAssetBytes || !IsSha256(digest))
        {
            throw new UpdateManifestException("manifest_invalid");
        }

        // The address must be the download of this file, in the release of this version. A manifest cannot point to another release or file.
        string expectedPath = DOWNLOAD_PATH + "v" + version + "/" + name;
        if (!string.Equals(url.AbsolutePath, expectedPath, StringComparison.Ordinal))
        {
            throw new UpdateManifestException(url.AbsolutePath.EndsWith("/" + name, StringComparison.Ordinal) ? "manifest_untrusted_url" : "manifest_invalid");
        }

        return new UpdateAsset(platform, name, size, digest.ToLowerInvariant(), url);
    }

    private static Uri OfficialUrl(string text, string requiredPathPrefix)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? url)
            || url.Scheme != Uri.UriSchemeHttps
            || !string.Equals(url.Host, OFFICIAL_HOST, StringComparison.OrdinalIgnoreCase)
            || !url.IsDefaultPort
            || !string.IsNullOrEmpty(url.UserInfo)
            || !string.IsNullOrEmpty(url.Query)
            || !string.IsNullOrEmpty(url.Fragment)
            || !url.AbsolutePath.StartsWith(requiredPathPrefix, StringComparison.Ordinal)
            || url.AbsolutePath.Contains("..", StringComparison.Ordinal)
            || url.AbsolutePath.Contains("//", StringComparison.Ordinal)
            || url.AbsolutePath.Contains('%', StringComparison.Ordinal))
        {
            throw new UpdateManifestException("manifest_untrusted_url");
        }

        return url;
    }

    private static bool IsPlainFileName(string name) =>
        name.Length is > 0 and <= 128
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
        && !name.StartsWith('.')
        && !name.Contains("..", StringComparison.Ordinal)
        && !s_reservedNames.Contains(Path.GetFileNameWithoutExtension(name), StringComparer.OrdinalIgnoreCase);

    private static bool IsSha256(string text) => text.Length == 64 && text.All(char.IsAsciiHexDigit);

    private static string RequiredString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : throw new UpdateManifestException("manifest_invalid");

    private static int RequiredInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)
            ? number
            : throw new UpdateManifestException("manifest_invalid");

    private static long RequiredLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number)
            ? number
            : throw new UpdateManifestException("manifest_invalid");
}
