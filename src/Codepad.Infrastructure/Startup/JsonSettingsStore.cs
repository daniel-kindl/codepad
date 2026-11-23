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
            // Accept only a level name. A number or an unknown name is a user error, not a level.
            if (logLevelElement.ValueKind != JsonValueKind.String
                || !Enum.TryParse(logLevelElement.GetString(), ignoreCase: true, out logLevel)
                || !Enum.IsDefined(logLevel)
                || int.TryParse(logLevelElement.GetString(), out _))
            {
                return SettingsReadResult.Unusable("invalid_log_level");
            }
        }

        return SettingsReadResult.Loaded(new AppSettings(logLevel));
    }
}
