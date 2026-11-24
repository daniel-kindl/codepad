using System.Text.Json;
using Codepad.Application.Updates;

namespace Codepad.Infrastructure.Updates;

/// <summary>
/// Finds the latest stable release of the project on GitHub and downloads its signed manifest. It reads the release list from the
/// public API with no token. It ignores drafts and pre-releases. It trusts nothing that it reads: the app checks the signature
/// of the manifest and then the manifest. A release without the two manifest files is not an update.
/// </summary>
/// <param name="client">An HTTP client from <see cref="GuardedHttp.CreateClient"/>.</param>
public sealed class GitHubUpdateSource(HttpClient client) : IUpdateSource
{
    /// <summary>The name of the manifest file in a release.</summary>
    public static readonly string ManifestName = "manifest.json";

    /// <summary>The name of the signature file in a release.</summary>
    public static readonly string SignatureName = "manifest.json.sig";

    private const string LATEST_URL = "https://api.github.com/repos/daniel-kindl/codepad/releases/latest";
    private const string DOWNLOAD_PREFIX = "/daniel-kindl/codepad/releases/download/";
    private const int MAX_RELEASE_BYTES = 1024 * 1024;
    private const int MAX_SIGNATURE_BYTES = 1024;

    private readonly GuardedHttp http = new GuardedHttp(client);

    /// <inheritdoc />
    public async Task<SignedManifest?> FetchLatestAsync(CancellationToken cancellationToken)
    {
        try
        {
            byte[] releaseJson;
            using (HttpResponseMessage response = await http.GetAsync(new Uri(LATEST_URL), "application/vnd.github+json", cancellationToken).ConfigureAwait(false))
            {
                releaseJson = await GuardedHttp.ReadBoundedAsync(response, MAX_RELEASE_BYTES, cancellationToken).ConfigureAwait(false);
            }

            (string tag, Uri manifestUrl, Uri signatureUrl)? release = ParseRelease(releaseJson);
            if (release is null)
            {
                return null;
            }

            byte[] manifest = await DownloadAsync(release.Value.manifestUrl, UpdateManifestParser.MaxBytes, cancellationToken).ConfigureAwait(false);
            byte[] signature = await DownloadAsync(release.Value.signatureUrl, MAX_SIGNATURE_BYTES, cancellationToken).ConfigureAwait(false);
            return new SignedManifest(manifest, signature, release.Value.tag);
        }
        catch (GuardedHttpException exception)
        {
            throw new UpdateSourceException(exception.Code, exception);
        }
    }

    private static (string Tag, Uri ManifestUrl, Uri SignatureUrl)? ParseRelease(byte[] json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new UpdateSourceException("release_invalid");
            }

            // The list endpoint skips drafts and pre-releases already. The flags are checked again, because the app must not depend on it.
            if (Flag(root, "draft") || Flag(root, "prerelease"))
            {
                return null;
            }

            string tag = root.TryGetProperty("tag_name", out JsonElement tagElement) && tagElement.ValueKind == JsonValueKind.String ? tagElement.GetString() ?? string.Empty : string.Empty;
            if (!SemanticVersion.TryParse(tag, out SemanticVersion? version) || !version!.IsStable)
            {
                return null;
            }

            Uri? manifest = null;
            Uri? signature = null;
            if (root.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assets.EnumerateArray().Take(64))
                {
                    string? name = asset.TryGetProperty("name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString() : null;
                    string? address = asset.TryGetProperty("browser_download_url", out JsonElement urlElement) && urlElement.ValueKind == JsonValueKind.String ? urlElement.GetString() : null;
                    if ((name == ManifestName || name == SignatureName) && Uri.TryCreate(address, UriKind.Absolute, out Uri? url))
                    {
                        if (!IsOfficialAsset(url, tag, name))
                        {
                            throw new UpdateSourceException("untrusted_url");
                        }

                        if (name == ManifestName)
                        {
                            manifest = url;
                        }
                        else
                        {
                            signature = url;
                        }
                    }
                }
            }

            return manifest is null || signature is null ? null : (tag, manifest, signature);
        }
        catch (JsonException)
        {
            throw new UpdateSourceException("release_invalid");
        }
    }

    private static bool IsOfficialAsset(Uri url, string tag, string name) =>
        GuardedHttp.IsAllowed(url)
        && url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(url.Query)
        && url.AbsolutePath == DOWNLOAD_PREFIX + tag + "/" + name;

    private static bool Flag(JsonElement root, string name) => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private async Task<byte[]> DownloadAsync(Uri url, int maxBytes, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(url, "application/octet-stream", cancellationToken).ConfigureAwait(false);
        return await GuardedHttp.ReadBoundedAsync(response, maxBytes, cancellationToken).ConfigureAwait(false);
    }
}
