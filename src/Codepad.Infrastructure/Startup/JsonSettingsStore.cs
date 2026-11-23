using System.Text.Json;
using Codepad.Application.Startup;
using Microsoft.Extensions.Logging;

namespace Codepad.Infrastructure.Startup;

/// <summary>
/// Reads the settings from a JSON file. The file is an object. CodePad ignores the properties
/// that it does not know, so that an older version can read a newer file.
/// </summary>
/// <param name="filePath">The full path of the settings file.</param>
public sealed class JsonSettingsStore(string filePath) : ISettingsStore
{
    /// <summary>The name of the settings file in the config folder.</summary>
    internal const string FILE_NAME = "settings.json";

    private const string LOG_LEVEL_PROPERTY = "logLevel";

    /// <summary>Makes a store for the settings file in the given config folder.</summary>
    /// <param name="configFolder">The full path of the config folder.</param>
    /// <returns>A store that reads <c>settings.json</c> in the folder.</returns>
    public static JsonSettingsStore InFolder(string configFolder) => new(Path.Combine(configFolder, FILE_NAME));

    /// <inheritdoc />
    public SettingsReadResult Read()
    {
        string json;
        try
        {
            if (!File.Exists(filePath) && !Directory.Exists(filePath))
            {
                return SettingsReadResult.Missing();
            }

            json = File.ReadAllText(filePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return SettingsReadResult.Unusable("unreadable");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return Parse(document.RootElement);
        }
        catch (JsonException)
        {
            return SettingsReadResult.Unusable("invalid_json");
        }
    }

    private static SettingsReadResult Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return SettingsReadResult.Unusable("invalid_json");
        }

        LogLevel logLevel = AppSettings.Default.MinimumLogLevel;
        if (root.TryGetProperty(LOG_LEVEL_PROPERTY, out JsonElement logLevelElement))
        {
            // Accept only one exact level name, in any case. Enum.TryParse would also accept numbers,
            // padded names and comma lists such as "Debug, Information", which it combines into another level.
            string? name = logLevelElement.ValueKind == JsonValueKind.String ? logLevelElement.GetString() : null;
            string? match = Enum.GetNames<LogLevel>().FirstOrDefault(level => string.Equals(level, name, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return SettingsReadResult.Unusable("invalid_log_level");
            }

            logLevel = Enum.Parse<LogLevel>(match);
        }

        return SettingsReadResult.Loaded(new AppSettings(logLevel));
    }
}
