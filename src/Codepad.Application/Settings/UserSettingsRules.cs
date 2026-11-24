namespace Codepad.Application.Settings;

/// <summary>A setting that has a value that the rules do not allow.</summary>
/// <param name="Field">The name of the setting.</param>
/// <param name="Message">A sentence that says what is allowed.</param>
public sealed record SettingsProblem(string Field, string Message);

/// <summary>The allowed range of a number setting. Both ends are allowed.</summary>
/// <param name="Min">The smallest value.</param>
/// <param name="Max">The largest value.</param>
public sealed record SettingRange(int Min, int Max);

/// <summary>The ranges of the settings. One place holds them: the form, the file reader and the tests use it.</summary>
public static class UserSettingsRules
{
    /// <summary>Gets the allowed editor font sizes.</summary>
    public static SettingRange FontSize { get; } = new SettingRange(8, 48);

    /// <summary>Gets the allowed indentation widths.</summary>
    public static SettingRange Indentation { get; } = new SettingRange(1, 8);

    /// <summary>Gets the allowed run timeouts in seconds.</summary>
    public static SettingRange Timeout { get; } = new SettingRange(1, 600);

    /// <summary>Gets the allowed history retention counts.</summary>
    public static SettingRange Retention { get; } = new SettingRange(1, 1_000);

    /// <summary>Checks all settings.</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>One problem for each setting that is not allowed. The list is empty when all settings are valid.</returns>
    public static IReadOnlyList<SettingsProblem> Validate(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        List<SettingsProblem> problems = [];
        if (!Enum.IsDefined(settings.Theme))
        {
            problems.Add(new SettingsProblem(nameof(UserSettings.Theme), "Choose System, Light or Dark."));
        }

        Check(problems, nameof(UserSettings.EditorFontSize), settings.EditorFontSize, FontSize, "The font size must be from {0} to {1}.");
        Check(problems, nameof(UserSettings.IndentationWidth), settings.IndentationWidth, Indentation, "The indentation width must be from {0} to {1} spaces.");
        Check(problems, nameof(UserSettings.RunTimeoutSeconds), settings.RunTimeoutSeconds, Timeout, "The run timeout must be from {0} to {1} seconds.");
        Check(problems, nameof(UserSettings.HistoryRetention), settings.HistoryRetention, Retention, "The history retention must be from {0} to {1} runs.");
        return problems;
    }

    /// <summary>Replaces each setting that is not allowed with its default. The other settings stay.</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>Valid settings.</returns>
    public static UserSettings Repair(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        UserSettings defaults = UserSettings.Default;
        HashSet<string> bad = [.. Validate(settings).Select(problem => problem.Field)];
        return settings with
        {
            Theme = bad.Contains(nameof(UserSettings.Theme)) ? defaults.Theme : settings.Theme,
            EditorFontSize = bad.Contains(nameof(UserSettings.EditorFontSize)) ? defaults.EditorFontSize : settings.EditorFontSize,
            IndentationWidth = bad.Contains(nameof(UserSettings.IndentationWidth)) ? defaults.IndentationWidth : settings.IndentationWidth,
            RunTimeoutSeconds = bad.Contains(nameof(UserSettings.RunTimeoutSeconds)) ? defaults.RunTimeoutSeconds : settings.RunTimeoutSeconds,
            HistoryRetention = bad.Contains(nameof(UserSettings.HistoryRetention)) ? defaults.HistoryRetention : settings.HistoryRetention,
        };
    }

    private static void Check(List<SettingsProblem> problems, string field, int value, SettingRange range, string message)
    {
        if (value < range.Min || value > range.Max)
        {
            problems.Add(new SettingsProblem(field, string.Format(System.Globalization.CultureInfo.InvariantCulture, message, range.Min, range.Max)));
        }
    }
}
