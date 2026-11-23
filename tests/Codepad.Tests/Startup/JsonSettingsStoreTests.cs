using Codepad.Application.Startup;
using Codepad.Infrastructure.Startup;
using Microsoft.Extensions.Logging;

namespace Codepad.Tests.Startup;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("codepad-tests-").FullName;

    private string FilePath => Path.Combine(folder, JsonSettingsStore.FILE_NAME);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void ReadWithNoFileReturnsMissing()
    {
        SettingsReadResult result = new JsonSettingsStore(FilePath).Read();

        Assert.Equal(SettingsReadResult.Missing(), result);
    }

    [Theory]
    [InlineData("""{ "logLevel": "Debug" }""", LogLevel.Debug)]
    [InlineData("""{ "logLevel": "warning" }""", LogLevel.Warning)]
    [InlineData("""{ }""", LogLevel.Information)]
    [InlineData("""{ "logLevel": "Error", "laterSetting": 42 }""", LogLevel.Error)]
    public void ReadWithValidSettingsReturnsThem(string json, LogLevel expected)
    {
        File.WriteAllText(FilePath, json);

        SettingsReadResult result = new JsonSettingsStore(FilePath).Read();

        Assert.Equal(SettingsReadResult.Loaded(new AppSettings(expected)), result);
    }

    [Theory]
    [InlineData("""{ "logLevel": """, "invalid_json")]
    [InlineData("""not json""", "invalid_json")]
    [InlineData("""[]""", "invalid_json")]
    [InlineData("""{ "logLevel": "Loud" }""", "invalid_log_level")]
    [InlineData("""{ "logLevel": 2 }""", "invalid_log_level")]
    [InlineData("""{ "logLevel": "3" }""", "invalid_log_level")]
    [InlineData("""{ "logLevel": null }""", "invalid_log_level")]
    [InlineData("""{ "logLevel": "Debug, Information" }""", "invalid_log_level")]
    [InlineData("""{ "logLevel": " Debug" }""", "invalid_log_level")]
    [InlineData("""{ "logLevel": "" }""", "invalid_log_level")]
    public void ReadWithInvalidContentReturnsUnusableWithAStableReason(string json, string expectedReason)
    {
        File.WriteAllText(FilePath, json);

        SettingsReadResult result = new JsonSettingsStore(FilePath).Read();

        Assert.Equal(SettingsReadResult.Unusable(expectedReason), result);
    }

    [Fact]
    public void ReadWhenThePathIsAFolderReturnsUnusable()
    {
        Directory.CreateDirectory(FilePath);

        SettingsReadResult result = new JsonSettingsStore(FilePath).Read();

        Assert.Equal(SettingsReadResult.Unusable("unreadable"), result);
    }
}
