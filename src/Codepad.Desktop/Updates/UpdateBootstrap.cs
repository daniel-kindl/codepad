using System.Reflection;
using System.Text.Json;
using Codepad.Application.Settings;
using Codepad.Application.Updates;

namespace Codepad.Desktop.Updates;

/// <summary>The small parts of the update setup that touch the process: the version, the key, the health file and the result of the last update.</summary>
internal static class UpdateBootstrap
{
    /// <summary>The name of the environment variable that the update helper sets for the first start of a new version.</summary>
    internal const string HEALTH_VARIABLE = "CODEPAD_UPDATE_HEALTH_FILE";

    /// <summary>Reads the installed version from the assembly. The commit text after <c>+</c> is dropped.</summary>
    /// <returns>The version, or 0.0.0 when the assembly has none.</returns>
    public static SemanticVersion InstalledVersion()
    {
        string? text = typeof(UpdateBootstrap).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return SemanticVersion.TryParse(text, out SemanticVersion? version) ? version! : SemanticVersion.TryParse("0.0.0", out SemanticVersion? zero) ? zero! : throw new InvalidOperationException();
    }

    /// <summary>Reads the public key that comes with this build. A build without a key gives null, and then it installs no update.</summary>
    /// <returns>The PEM text, or null.</returns>
    public static string? PublicKeyPem()
    {
        using Stream? stream = typeof(UpdateBootstrap).Assembly.GetManifestResourceStream("Codepad.Desktop.Updates.UpdatePublicKey.pem");
        if (stream is null)
        {
            return null;
        }

        using StreamReader reader = new StreamReader(stream);
        string text = reader.ReadToEnd();
        return text.Contains("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal) ? text : null;
    }

    /// <summary>
    /// Tells the update helper that the new version started. The helper sets the variable only for the first start after an
    /// update. Without this file the helper puts the old version back.
    /// </summary>
    public static void WriteHealthFileIfRequested()
    {
        string? path = Environment.GetEnvironmentVariable(HEALTH_VARIABLE);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            File.WriteAllText(path, "ok");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Without the file the helper rolls back. The app has no better way to report it.
        }
    }

    /// <summary>Reads and deletes the result that the helper left after the last update.</summary>
    /// <param name="resultFile">The path of the result file.</param>
    /// <returns>The result, or null when there is none or it is not valid.</returns>
    public static (string From, string To, string Status)? TakePreviousResult(string resultFile)
    {
        try
        {
            if (!File.Exists(resultFile))
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(resultFile));
            JsonElement root = document.RootElement;
            string? status = root.TryGetProperty("status", out JsonElement statusElement) ? statusElement.GetString() : null;
            string? from = root.TryGetProperty("fromVersion", out JsonElement fromElement) ? fromElement.GetString() : null;
            string? to = root.TryGetProperty("toVersion", out JsonElement toElement) ? toElement.GetString() : null;
            File.Delete(resultFile);
            return status is not null && from is not null && to is not null && SemanticVersion.TryParse(from, out _) && SemanticVersion.TryParse(to, out _)
                ? (from, to, status)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}

/// <summary>Connects the Check now button of the settings to the update notice.</summary>
/// <param name="getUpdates">Gives the update notice. The settings are made before it, so the notice comes later.</param>
internal sealed class UpdateCheckHandoff(Func<UpdateViewModel?> getUpdates) : IUpdateCheckHandoff
{
    /// <inheritdoc />
    public async Task<string> CheckAsync(CancellationToken cancellationToken = default)
    {
        UpdateViewModel? updates = getUpdates();
        if (updates is null)
        {
            return "Updates are not available.";
        }

        await updates.CheckNowAsync(cancellationToken).ConfigureAwait(true);
        return updates.Title;
    }
}
