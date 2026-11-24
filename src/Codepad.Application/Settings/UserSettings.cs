using Codepad.Domain.Operations;

namespace Codepad.Application.Settings;

/// <summary>
/// The settings that the user can change in the app. The defaults are documented in ARCHITECTURE.md. A change to the
/// timeout or to the retention applies to new runs only: a request keeps the policy that it was made with.
/// </summary>
/// <param name="Theme">The theme.</param>
/// <param name="EditorFontSize">The font size of the editors, in points.</param>
/// <param name="IndentationWidth">The number of spaces for one indentation step in the C# editor.</param>
/// <param name="RunTimeoutSeconds">The time limit of one C# run, in seconds.</param>
/// <param name="HistoryRetention">The number of saved runs that the app keeps for each experiment.</param>
/// <param name="UpdateChecksEnabled">Whether the app may check for updates. A check never downloads or installs by itself.</param>
public sealed record UserSettings(ThemePreference Theme, int EditorFontSize, int IndentationWidth, int RunTimeoutSeconds, int HistoryRetention, bool UpdateChecksEnabled)
{
    /// <summary>Gets the default settings.</summary>
    public static UserSettings Default { get; } = new UserSettings(ThemePreference.System, 14, 4, 10, 100, true);

    /// <summary>Gets the time limit of one run.</summary>
    public TimeSpan RunTimeout => TimeSpan.FromSeconds(RunTimeoutSeconds);

    /// <summary>Gets the policy of a new C# run: explicit only, with the time limit of these settings.</summary>
    public OperationPolicy CSharpPolicy => new OperationPolicy(OperationKind.CSharp, 1, OperationTrigger.ExplicitOnly, RunTimeout, ResultPolicy.Default);

    /// <summary>Returns a copy with one number changed. The name is a property name of this type.</summary>
    /// <param name="field">The property name.</param>
    /// <param name="value">The new value.</param>
    /// <returns>The copy.</returns>
    public UserSettings With(string field, int value) => field switch
    {
        nameof(EditorFontSize) => this with { EditorFontSize = value },
        nameof(IndentationWidth) => this with { IndentationWidth = value },
        nameof(RunTimeoutSeconds) => this with { RunTimeoutSeconds = value },
        nameof(HistoryRetention) => this with { HistoryRetention = value },
        _ => throw new ArgumentException($"'{field}' is not a number setting.", nameof(field)),
    };
}
