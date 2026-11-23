using Codepad.Application.Startup;
using Microsoft.Extensions.Logging;

namespace Codepad.Tests.Startup;

public sealed class StartupServiceTests
{
    [Fact]
    public void PrepareWithNoSettingsFileAndReadyFolderUsesDefaultsWithNoNotices()
    {
        StartupService service = new StartupService(new StubSettingsStore(SettingsReadResult.Missing()), new StubDataFolder(DataFolderStatus.Ready), new StubDataStore(DataStoreStatus.Ready));

        StartupReport report = service.Prepare();

        Assert.Equal(AppSettings.Default, report.Settings);
        Assert.Empty(report.Notices);
    }

    [Fact]
    public void PrepareWithValidSettingsUsesThem()
    {
        AppSettings settings = new AppSettings(LogLevel.Debug);
        StartupService service = new StartupService(new StubSettingsStore(SettingsReadResult.Loaded(settings)), new StubDataFolder(DataFolderStatus.Ready), new StubDataStore(DataStoreStatus.Ready));

        StartupReport report = service.Prepare();

        Assert.Equal(settings, report.Settings);
        Assert.Empty(report.Notices);
    }

    [Fact]
    public void PrepareWithUnusableSettingsUsesDefaultsAndReportsTheReason()
    {
        StartupService service = new StartupService(new StubSettingsStore(SettingsReadResult.Unusable("invalid_json")), new StubDataFolder(DataFolderStatus.Ready), new StubDataStore(DataStoreStatus.Ready));

        StartupReport report = service.Prepare();

        Assert.Equal(AppSettings.Default, report.Settings);
        StartupNotice notice = Assert.Single(report.Notices);
        Assert.Equal(new StartupNotice(StartupNoticeKind.SettingsUnusable, "invalid_json"), notice);
    }

    [Fact]
    public void PrepareWithUnavailableDataFolderStillStartsAndReportsTheReason()
    {
        StartupService service = new StartupService(new StubSettingsStore(SettingsReadResult.Missing()), new StubDataFolder(DataFolderStatus.Unavailable("UnauthorizedAccessException")), new StubDataStore(DataStoreStatus.Ready));

        StartupReport report = service.Prepare();

        Assert.Equal(AppSettings.Default, report.Settings);
        StartupNotice notice = Assert.Single(report.Notices);
        Assert.Equal(new StartupNotice(StartupNoticeKind.DataFolderUnavailable, "UnauthorizedAccessException"), notice);
    }

    [Fact]
    public void PrepareWithTwoProblemsReportsBothInStartupOrder()
    {
        StartupService service = new StartupService(new StubSettingsStore(SettingsReadResult.Unusable("invalid_log_level")), new StubDataFolder(DataFolderStatus.Unavailable("IOException")), new StubDataStore(DataStoreStatus.Ready));

        StartupReport report = service.Prepare();

        Assert.Equal([StartupNoticeKind.SettingsUnusable, StartupNoticeKind.DataFolderUnavailable], report.Notices.Select(notice => notice.Kind));
    }

    [Fact]
    public void PrepareWithAnUnusableDatabaseStillStartsAndReportsTheReason()
    {
        StartupService service = new StartupService(new StubSettingsStore(SettingsReadResult.Missing()), new StubDataFolder(DataFolderStatus.Ready), new StubDataStore(DataStoreStatus.Unavailable("schema_too_new")));

        StartupReport report = service.Prepare();

        StartupNotice notice = Assert.Single(report.Notices);
        Assert.Equal(new StartupNotice(StartupNoticeKind.DataStoreUnavailable, "schema_too_new"), notice);
    }

    [Fact]
    public void PrepareDoesNotOpenTheDatabaseWhenTheDataFolderIsUnavailable()
    {
        StubDataStore store = new StubDataStore(DataStoreStatus.Ready);
        StartupService service = new StartupService(new StubSettingsStore(SettingsReadResult.Missing()), new StubDataFolder(DataFolderStatus.Unavailable("IOException")), store);

        StartupReport report = service.Prepare();

        Assert.Equal(0, store.Calls);
        Assert.Equal([StartupNoticeKind.DataFolderUnavailable], report.Notices.Select(notice => notice.Kind));
    }

    [Fact]
    public void PrepareInitializesTheDatabaseOnceWhenTheDataFolderIsReady()
    {
        StubDataStore store = new StubDataStore(DataStoreStatus.Ready);
        StartupService service = new StartupService(new StubSettingsStore(SettingsReadResult.Missing()), new StubDataFolder(DataFolderStatus.Ready), store);

        service.Prepare();

        Assert.Equal(1, store.Calls);
    }

    private sealed class StubDataStore(DataStoreStatus status) : IDataStoreInitializer
    {
        public int Calls { get; private set; }

        public DataStoreStatus Initialize()
        {
            Calls++;
            return status;
        }
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
