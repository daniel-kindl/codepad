using Codepad.Application.Startup;
using Codepad.Desktop.Shell;

namespace Codepad.Desktop.Tests.Shell;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void NormalStartShowsReadyWithNoWarning()
    {
        MainWindowViewModel viewModel = new MainWindowViewModel([]);

        Assert.Equal("CodePad", viewModel.Title);
        Assert.Equal("Ready", viewModel.StatusText);
        Assert.False(viewModel.HasWarning);
    }

    [Fact]
    public void UnusableSettingsShowsThatDefaultsAreInUse()
    {
        MainWindowViewModel viewModel = new MainWindowViewModel([new StartupNotice(StartupNoticeKind.SettingsUnusable, "invalid_json")]);

        Assert.Equal(MainWindowViewModel.SETTINGS_UNUSABLE_TEXT, viewModel.StatusText);
        Assert.True(viewModel.HasWarning);
    }

    [Fact]
    public void UnavailableDataFolderShowsThatWorkIsNotSaved()
    {
        MainWindowViewModel viewModel = new MainWindowViewModel([new StartupNotice(StartupNoticeKind.DataFolderUnavailable, "IOException")]);

        Assert.Equal(MainWindowViewModel.DATA_FOLDER_UNAVAILABLE_TEXT, viewModel.StatusText);
        Assert.True(viewModel.HasWarning);
    }

    [Fact]
    public void UnusableDatabaseShowsThatWorkIsNotSaved()
    {
        MainWindowViewModel viewModel = new MainWindowViewModel([new StartupNotice(StartupNoticeKind.DataStoreUnavailable, "schema_too_new")]);

        Assert.Equal(MainWindowViewModel.DATA_STORE_UNAVAILABLE_TEXT, viewModel.StatusText);
        Assert.True(viewModel.HasWarning);
    }

    [Fact]
    public void RecoveredPreferencesShowAWarningWithoutTheFieldNames()
    {
        MainWindowViewModel viewModel = new MainWindowViewModel([new StartupNotice(StartupNoticeKind.PreferencesRecovered, "editorFontSize,theme")]);

        Assert.Equal(MainWindowViewModel.PREFERENCES_RECOVERED_TEXT, viewModel.StatusText);
        Assert.True(viewModel.HasWarning);
        Assert.DoesNotContain("editorFontSize", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoProblemsShowBothMessagesInOrder()
    {
        MainWindowViewModel viewModel = new MainWindowViewModel(
        [
            new StartupNotice(StartupNoticeKind.SettingsUnusable, "invalid_json"),
            new StartupNotice(StartupNoticeKind.DataFolderUnavailable, "IOException"),
        ]);

        Assert.Equal($"{MainWindowViewModel.SETTINGS_UNUSABLE_TEXT} {MainWindowViewModel.DATA_FOLDER_UNAVAILABLE_TEXT}", viewModel.StatusText);
    }

    [Fact]
    public void StatusTextNeverShowsTheTechnicalReason()
    {
        MainWindowViewModel viewModel = new MainWindowViewModel([new StartupNotice(StartupNoticeKind.SettingsUnusable, "marker-reason-7f3a")]);

        Assert.DoesNotContain("marker-reason-7f3a", viewModel.StatusText, StringComparison.Ordinal);
    }
}
