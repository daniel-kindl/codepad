using Codepad.Application.Settings;

namespace Codepad.Tests.Settings;

public sealed class UserSettingsTests
{
    [Fact]
    public void TheDefaultsAreTheDocumentedValuesAndAreValid()
    {
        UserSettings defaults = UserSettings.Default;

        Assert.Equal(ThemePreference.System, defaults.Theme);
        Assert.Equal(14, defaults.EditorFontSize);
        Assert.Equal(4, defaults.IndentationWidth);
        Assert.Equal(10, defaults.RunTimeoutSeconds);
        Assert.Equal(100, defaults.HistoryRetention);
        Assert.True(defaults.UpdateChecksEnabled);
        Assert.Empty(UserSettingsRules.Validate(defaults));
    }

    [Theory]
    [InlineData(nameof(UserSettings.EditorFontSize), 7)]
    [InlineData(nameof(UserSettings.EditorFontSize), 49)]
    [InlineData(nameof(UserSettings.IndentationWidth), 0)]
    [InlineData(nameof(UserSettings.IndentationWidth), 9)]
    [InlineData(nameof(UserSettings.RunTimeoutSeconds), 0)]
    [InlineData(nameof(UserSettings.RunTimeoutSeconds), 601)]
    [InlineData(nameof(UserSettings.HistoryRetention), 0)]
    [InlineData(nameof(UserSettings.HistoryRetention), 1_001)]
    public void ANumberOutsideItsRangeIsAProblemForThatField(string field, int value)
    {
        UserSettings changed = UserSettings.Default.With(field, value);

        SettingsProblem problem = Assert.Single(UserSettingsRules.Validate(changed));

        Assert.Equal(field, problem.Field);
        Assert.False(string.IsNullOrWhiteSpace(problem.Message));
    }

    [Theory]
    [InlineData(nameof(UserSettings.EditorFontSize), 8)]
    [InlineData(nameof(UserSettings.EditorFontSize), 48)]
    [InlineData(nameof(UserSettings.IndentationWidth), 1)]
    [InlineData(nameof(UserSettings.IndentationWidth), 8)]
    [InlineData(nameof(UserSettings.RunTimeoutSeconds), 1)]
    [InlineData(nameof(UserSettings.RunTimeoutSeconds), 600)]
    [InlineData(nameof(UserSettings.HistoryRetention), 1)]
    [InlineData(nameof(UserSettings.HistoryRetention), 1_000)]
    public void TheEdgesOfARangeAreValid(string field, int value)
    {
        Assert.Empty(UserSettingsRules.Validate(UserSettings.Default.With(field, value)));
    }

    [Fact]
    public void AnUndefinedThemeIsAProblem()
    {
        UserSettings changed = UserSettings.Default with { Theme = (ThemePreference)99 };

        Assert.Equal(nameof(UserSettings.Theme), Assert.Single(UserSettingsRules.Validate(changed)).Field);
    }

    [Fact]
    public void SeveralBadFieldsGiveSeveralProblems()
    {
        UserSettings changed = UserSettings.Default with { EditorFontSize = 1, RunTimeoutSeconds = 0 };

        Assert.Equal(2, UserSettingsRules.Validate(changed).Count);
    }

    [Fact]
    public void RepairReplacesOnlyTheBadFieldsWithTheDefaults()
    {
        UserSettings broken = new UserSettings(ThemePreference.Dark, 500, 2, 0, 50, false);

        UserSettings repaired = UserSettingsRules.Repair(broken);

        Assert.Equal(ThemePreference.Dark, repaired.Theme);
        Assert.Equal(UserSettings.Default.EditorFontSize, repaired.EditorFontSize);
        Assert.Equal(2, repaired.IndentationWidth);
        Assert.Equal(UserSettings.Default.RunTimeoutSeconds, repaired.RunTimeoutSeconds);
        Assert.Equal(50, repaired.HistoryRetention);
        Assert.False(repaired.UpdateChecksEnabled);
        Assert.Empty(UserSettingsRules.Validate(repaired));
    }

    [Fact]
    public void TheTimeLimitIsTheRunTimeoutInSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), (UserSettings.Default with { RunTimeoutSeconds = 30 }).RunTimeout);
    }
}
