using System.Text.Json;
using System.Text.Json.Nodes;
using Codepad.Application.Settings;

namespace Codepad.Infrastructure.Startup;

/// <summary>
/// Stores the user settings in the section <c>preferences</c> of <c>settings.json</c> in the config folder, next to the
/// hand-written <c>logLevel</c>. A write replaces the file in one step and keeps all other content. A bad value
/// falls back to the default for that value, with a warning. A file from a newer version is not used and not changed until the
/// user changes a setting. The file has no path and no user text.
/// </summary>
/// <param name="filePath">The full path of the settings file.</param>
public sealed class JsonUserSettingsStore(string filePath) : IUserSettingsStore
{
    private const string SECTION = "preferences";
    private const int VERSION = 1;

    /// <summary>Creates the store for the settings file in a config folder.</summary>
    /// <param name="configFolder">The config folder.</param>
    /// <returns>The store.</returns>
    public static JsonUserSettingsStore InFolder(string configFolder) => new(Path.Combine(configFolder, JsonSettingsStore.FILE_NAME));

    /// <inheritdoc />
    public UserSettingsReadResult Read()
    {
        string json;
        try
        {
            if (!File.Exists(filePath))
            {
                return Defaults();
            }

            json = File.ReadAllText(filePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Defaults("unreadable");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Defaults("invalid_json");
            }

            return document.RootElement.TryGetProperty(SECTION, out JsonElement section) ? ReadSection(section) : Defaults();
        }
        catch (JsonException)
        {
            return Defaults("invalid_json");
        }
    }

    /// <inheritdoc />
    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (UserSettingsRules.Validate(settings).Count > 0)
        {
            throw new ArgumentException("The settings are not valid.", nameof(settings));
        }

        try
        {
            JsonObject root = ReadRootForWrite();
            root[SECTION] = new JsonObject
            {
                ["version"] = VERSION,
                ["theme"] = settings.Theme.ToString().ToLowerInvariant(),
                ["editorFontSize"] = settings.EditorFontSize,
                ["indentationWidth"] = settings.IndentationWidth,
                ["runTimeoutSeconds"] = settings.RunTimeoutSeconds,
                ["historyRetention"] = settings.HistoryRetention,
                ["updateChecksEnabled"] = settings.UpdateChecksEnabled,
            };

            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write a new file, then replace the old one. A crash in the middle leaves the old file whole.
            string temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The message of an IO exception holds the path. Only a reason code goes out.
            throw new UserSettingsStorageException(exception is UnauthorizedAccessException ? "read_only" : "io_error", exception);
        }
    }

    private static UserSettingsReadResult Defaults(params string[] warnings) => new(UserSettings.Default, warnings);

    private static UserSettingsReadResult ReadSection(JsonElement section)
    {
        if (section.ValueKind != JsonValueKind.Object)
        {
            return Defaults("invalid_preferences");
        }

        if (!section.TryGetProperty("version", out JsonElement version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number) || number < 1)
        {
            return Defaults("invalid_version");
        }

        if (number > VERSION)
        {
            return Defaults("future_version");
        }

        UserSettings defaults = UserSettings.Default;
        List<string> warnings = [];
        ThemePreference theme = defaults.Theme;
        if (section.TryGetProperty("theme", out JsonElement themeElement))
        {
            // Accept one exact name. Enum.TryParse would also accept numbers and lists.
            string? name = themeElement.ValueKind == JsonValueKind.String ? themeElement.GetString() : null;
            string? match = Enum.GetNames<ThemePreference>().FirstOrDefault(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                warnings.Add("theme");
            }
            else
            {
                theme = Enum.Parse<ThemePreference>(match);
            }
        }

        UserSettings read = new UserSettings(
            theme,
            ReadNumber(section, "editorFontSize", defaults.EditorFontSize, UserSettingsRules.FontSize, warnings),
            ReadNumber(section, "indentationWidth", defaults.IndentationWidth, UserSettingsRules.Indentation, warnings),
            ReadNumber(section, "runTimeoutSeconds", defaults.RunTimeoutSeconds, UserSettingsRules.Timeout, warnings),
            ReadNumber(section, "historyRetention", defaults.HistoryRetention, UserSettingsRules.Retention, warnings),
            ReadFlag(section, "updateChecksEnabled", defaults.UpdateChecksEnabled, warnings));
        return new UserSettingsReadResult(read, warnings);
    }

    private static int ReadNumber(JsonElement section, string name, int fallback, SettingRange range, List<string> warnings)
    {
        if (!section.TryGetProperty(name, out JsonElement element))
        {
            return fallback;
        }

        // A number that is not a whole number in the range is not used. A string that looks like a number is not a number.
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int value) && value >= range.Min && value <= range.Max)
        {
            return value;
        }

        warnings.Add(name);
        return fallback;
    }

    private static bool ReadFlag(JsonElement section, string name, bool fallback, List<string> warnings)
    {
        if (!section.TryGetProperty(name, out JsonElement element))
        {
            return fallback;
        }

        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return element.GetBoolean();
        }

        warnings.Add(name);
        return fallback;
    }

    private JsonObject ReadRootForWrite()
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        string text = File.ReadAllText(filePath);
        try
        {
            if (JsonNode.Parse(text) is JsonObject root)
            {
                return root;
            }
        }
        catch (JsonException)
        {
            // Fall through to the copy below.
        }

        // The file is damaged. A copy keeps what the user wrote, and the new file is valid.
        File.WriteAllText(filePath + ".invalid", text);
        return [];
    }
}
