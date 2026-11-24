using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Codepad.ReleaseTool;

/// <summary>One archive of a release.</summary>
/// <param name="Platform">The platform ID, <c>linux-x64</c> or <c>win-x64</c>.</param>
/// <param name="Path">The path of the archive. Its file name goes into the manifest.</param>
internal sealed record ReleaseArchive(string Platform, string Path);

/// <summary>
/// Writes <c>manifest.json</c> for a release, in the format of docs/updates.md. The size and the SHA-256 digest come from the
/// archive files themselves. The text is the same for the same input, so a second build can be compared byte for byte.
/// </summary>
internal static class ReleaseManifestBuilder
{
    private const string REPOSITORY = "https://github.com/daniel-kindl/codepad/releases/";

    /// <summary>Builds the manifest.</summary>
    /// <param name="version">The version, for example <c>0.2.0</c>. The tag is <c>v</c> and this.</param>
    /// <param name="releaseNotes">The notes for the user.</param>
    /// <param name="publishedAt">The time of the release, as text in UTC.</param>
    /// <param name="archives">The archives, one for each platform.</param>
    /// <returns>The UTF-8 bytes of the manifest. Sign exactly these bytes.</returns>
    public static byte[] Build(string version, string releaseNotes, DateTimeOffset publishedAt, IReadOnlyList<ReleaseArchive> archives)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(archives);
        using MemoryStream stream = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteNumber("protocol", 1);
            writer.WriteString("version", version);
            writer.WriteString("releaseUrl", REPOSITORY + "tag/v" + version);
            writer.WriteString("releaseNotes", releaseNotes);
            writer.WriteString("publishedAt", publishedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            writer.WriteStartArray("assets");
            foreach (ReleaseArchive archive in archives.OrderBy(item => item.Platform, StringComparer.Ordinal))
            {
                string name = System.IO.Path.GetFileName(archive.Path);
                byte[] bytes = File.ReadAllBytes(archive.Path);
                writer.WriteStartObject();
                writer.WriteString("platform", archive.Platform);
                writer.WriteString("name", name);
                writer.WriteNumber("size", bytes.Length);
                writer.WriteString("sha256", Convert.ToHexStringLower(SHA256.HashData(bytes)));
                writer.WriteString("url", REPOSITORY + "download/v" + version + "/" + name);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return [.. stream.ToArray(), .. Encoding.UTF8.GetBytes("\n")];
    }

    /// <summary>Writes the checksum file of a folder in the format of <c>sha256sum</c>: the digest, two spaces and the name.</summary>
    /// <param name="files">The files.</param>
    /// <returns>The text.</returns>
    public static string Checksums(IEnumerable<string> files)
    {
        StringBuilder builder = new StringBuilder();
        foreach (string file in files.OrderBy(path => System.IO.Path.GetFileName(path), StringComparer.Ordinal))
        {
            builder.Append(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))).Append("  ").Append(System.IO.Path.GetFileName(file)).Append('\n');
        }

        return builder.ToString();
    }
}
