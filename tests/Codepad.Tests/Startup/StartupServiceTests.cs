using Codepad.Application.Startup;
using Microsoft.Extensions.Logging;

namespace Codepad.Tests.Startup;

public sealed class StartupServiceTests
{
    [Fact]
    public void PrepareWithNoSettingsFileAndReadyFolderUsesDefaultsWithNoNotices()
    {
        StartupService service = new StartupService(new StubSettingsStore(SettingsReadResult.Missing()), new StubDataFolder(DataFolderStatus.Ready));

        StartupReport report = service.Prepare();

        Assert.Equal(AppSettings.Default, report.Settings);
        Assert.Empty(report.Notices);
    }

    [Fact]
    public void PrepareWithValidSettingsUsesThem()
    {
        AppSettings settings = new AppSettings(LogLevel.Debug);
        StartupService service = new StartupService(new StubSettingsStore(SettingsReadResult.Loaded(settings)), new StubDataFolder(DataFolderStatus.Ready));

        StartupReport report = service.Prepare();

        Assert.Equal(settings, report.Settings);
        Assert.Empty(report.Notices);
    }

    [Fact]
    public void PrepareWithUnusableSettingsUsesDefaultsAndReportsTheReason()
    {
        StartupService service = new StartupService(new StubSettingsStore(SettingsReadResult.Unusable("invalid_json")), new StubDataFolder(DataFolderStatus.Ready));

        StartupReport report = service.Prepare();

        Assert.Equal(AppSettings.Default, report.Settings);
        StartupNotice notice = Assert.Single(report.Notices);
        Assert.Equal(new StartupNotice(StartupNoticeKind.SettingsUnusable, "invalid_json"), notice);
    }

    [Fact]
    public void PrepareWithUnavailableDataFolderStillStartsAndReportsTheReason()
    {
        StartupService service = new StartupService(new StubSettingsStore(SettingsReadResult.Missing()), new StubDataFolder(DataFolderStatus.Unavailable("UnauthorizedAccessException")));

        StartupReport report = service.Prepare();

        Assert.Equal(AppSettings.Default, report.Settings);
        StartupNotice notice = Assert.Single(report.Notices);
        Assert.Equal(new StartupNotice(StartupNoticeKind.DataFolderUnavailable, "UnauthorizedAccessException"), notice);
    }

    [Fact]
    public void PrepareWithTwoProblemsReportsBothInStartupOrder()
    {
        StartupService service = new StartupService(new StubSettingsStore(SettingsReadResult.Unusable("invalid_log_level")), new StubDataFolder(DataFolderStatus.Unavailable("IOException")));

        StartupReport report = service.Prepare();

        Assert.Equal([StartupNoticeKind.SettingsUnusable, StartupNoticeKind.DataFolderUnavailable], report.Notices.Select(notice => notice.Kind));
    }

    private sealed class StubSettingsStore(SettingsReadResult result) : ISettingsStore
    {
        public SettingsReadResult Read() => result;
    }

    private sealed class StubDataFolder(DataFolderStatus status) : IDataFolder
    {
        public DataFolderStatus Prepare() => status;
    }
}
