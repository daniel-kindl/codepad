using Codepad.Application.Settings;
using Codepad.Infrastructure.Startup;

namespace Codepad.Tests.Settings;

public sealed class JsonUserSettingsStoreTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("codepad-settings-").FullName;

    private string FilePath => Path.Combine(folder, "settings.json");

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private JsonUserSettingsStore Store() => new JsonUserSettingsStore(FilePath);

    [Fact]
    public void AMissingFileGivesTheDefaultsWithoutWarnings()
    {
        UserSettingsReadResult result = Store().Read();

        Assert.Equal(UserSettings.Default, result.Settings);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void SavedSettingsComeBackAfterARestart()
    {
        UserSettings chosen = new UserSettings(ThemePreference.Dark, 18, 2, 30, 250, false);

        Store().Save(chosen);
        UserSettingsReadResult result = new JsonUserSettingsStore(FilePath).Read();

        Assert.Equal(chosen, result.Settings);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void SaveKeepsTheLogLevelAndOtherContent()
    {
        File.WriteAllText(FilePath, "{ \"logLevel\": \"Debug\", \"note\": [1, 2] }");

        Store().Save(UserSettings.Default with { Theme = ThemePreference.Light });

        string text = File.ReadAllText(FilePath);
        Assert.Contains("\"logLevel\"", text, StringComparison.Ordinal);
        Assert.Contains("\"Debug\"", text, StringComparison.Ordinal);
        Assert.Contains("\"note\"", text, StringComparison.Ordinal);
        Assert.Equal(ThemePreference.Light, Store().Read().Settings.Theme);
    }

    [Fact]
    public void TheExistingStartupReaderStillReadsTheLogLevelAfterASave()
    {
        File.WriteAllText(FilePath, "{ \"logLevel\": \"Warning\" }");
        Store().Save(UserSettings.Default);

        Codepad.Application.Startup.SettingsReadResult read = new JsonSettingsStore(FilePath).Read();

        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, read.Settings?.MinimumLogLevel);
    }

    [Theory]
    [InlineData("\"editorFontSize\": 500", "editorFontSize")]
    [InlineData("\"editorFontSize\": \"big\"", "editorFontSize")]
    [InlineData("\"editorFontSize\": 12.5", "editorFontSize")]
    [InlineData("\"indentationWidth\": 0", "indentationWidth")]
    [InlineData("\"runTimeoutSeconds\": -3", "runTimeoutSeconds")]
    [InlineData("\"historyRetention\": 99999", "historyRetention")]
    [InlineData("\"theme\": \"purple\"", "theme")]
    [InlineData("\"theme\": 3", "theme")]
    [InlineData("\"updateChecksEnabled\": \"yes\"", "updateChecksEnabled")]
    public void AnInvalidValueFallsBackToTheDefaultForThatFieldOnlyAndWarns(string property, string field)
    {
        File.WriteAllText(FilePath, "{ \"preferences\": { \"version\": 1, \"theme\": \"dark\", \"editorFontSize\": 20, " + property + " } }");

        UserSettingsReadResult result = Store().Read();

        Assert.Equal([field], result.Warnings);
        Assert.Empty(UserSettingsRules.Validate(result.Settings));
        UserSettings expected = field switch
        {
            "editorFontSize" => UserSettings.Default with { Theme = ThemePreference.Dark },
            "theme" => UserSettings.Default with { EditorFontSize = 20 },
            _ => UserSettings.Default with { Theme = ThemePreference.Dark, EditorFontSize = 20 },
        };
        Assert.Equal(expected, result.Settings);
    }

    [Fact]
    public void AFutureVersionGivesTheDefaultsAndAWarningAndKeepsTheFileUntilTheNextSave()
    {
        string content = "{ \"preferences\": { \"version\": 7, \"theme\": \"dark\", \"newThing\": true } }";
        File.WriteAllText(FilePath, content);

        UserSettingsReadResult result = Store().Read();

        Assert.Equal(UserSettings.Default, result.Settings);
        Assert.Equal(["future_version"], result.Warnings);
        Assert.Equal(content, File.ReadAllText(FilePath));
    }

    [Theory]
    [InlineData("not json", "invalid_json")]
    [InlineData("[1,2]", "invalid_json")]
    [InlineData("{ \"preferences\": 5 }", "invalid_preferences")]
    [InlineData("{ \"preferences\": { \"theme\": \"dark\" } }", "invalid_version")]
    [InlineData("{ \"preferences\": { \"version\": \"one\" } }", "invalid_version")]
    public void DamagedContentGivesTheDefaultsAndAWarning(string content, string warning)
    {
        File.WriteAllText(FilePath, content);

        UserSettingsReadResult result = Store().Read();

        Assert.Equal(UserSettings.Default, result.Settings);
        Assert.Equal([warning], result.Warnings);
    }

    [Fact]
    public void SaveOverADamagedFileKeepsACopyAndWritesAValidFile()
    {
        File.WriteAllText(FilePath, "this is not json");

        Store().Save(UserSettings.Default with { EditorFontSize = 20 });

        Assert.Equal(20, Store().Read().Settings.EditorFontSize);
        Assert.Equal("this is not json", File.ReadAllText(FilePath + ".invalid"));
    }

    [Fact]
    public void SaveRefusesInvalidSettings()
    {
        Assert.Throws<ArgumentException>(() => Store().Save(UserSettings.Default with { EditorFontSize = 1 }));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void SaveCreatesTheFolderAndLeavesNoTemporaryFile()
    {
        string nested = Path.Combine(folder, "a", "b", "settings.json");

        new JsonUserSettingsStore(nested).Save(UserSettings.Default);

        Assert.True(File.Exists(nested));
        Assert.Equal([nested], Directory.GetFiles(Path.GetDirectoryName(nested)!));
    }

    [Fact]
    public void AFailedWriteThrowsAStorageExceptionAndKeepsTheOldFile()
    {
        // A directory has the file name, so the write cannot replace it.
        Directory.CreateDirectory(FilePath);

        UserSettingsStorageException error = Assert.Throws<UserSettingsStorageException>(() => Store().Save(UserSettings.Default));

        Assert.DoesNotContain(folder, error.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(FilePath));
    }

    [Fact]
    public void TheFileHoldsNoPathAndNoUserText()
    {
        Store().Save(UserSettings.Default);

        string text = File.ReadAllText(FilePath);

        Assert.DoesNotContain(folder, text, StringComparison.Ordinal);
        Assert.Contains("\"version\"", text, StringComparison.Ordinal);
    }
}
