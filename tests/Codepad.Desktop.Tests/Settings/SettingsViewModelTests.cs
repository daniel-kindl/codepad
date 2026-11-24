using Codepad.Application.Experiments;
using Codepad.Application.Settings;
using Codepad.Desktop.Settings;
using Codepad.Desktop.Tests.History;

namespace Codepad.Desktop.Tests.Settings;

public sealed class SettingsViewModelTests
{
    private readonly MemoryStore store = new MemoryStore();
    private readonly MemoryHistory history = new MemoryHistory();
    private readonly List<string> failures = [];

    private SettingsViewModel Create(UserSettings? initial = null, bool withHistory = true) =>
        new SettingsViewModel(store, initial ?? UserSettings.Default, withHistory ? history : null, failures.Add);

    [Fact]
    public void ItStartsWithTheGivenSettingsAndEditorPreferences()
    {
        SettingsViewModel model = Create(new UserSettings(ThemePreference.Dark, 18, 2, 30, 250, false));

        Assert.Equal(ThemePreference.Dark, model.Theme);
        Assert.Equal(18, model.FontSize);
        Assert.Equal(2, model.IndentationWidth);
        Assert.Equal(30, model.RunTimeoutSeconds);
        Assert.Equal(250, model.HistoryRetention);
        Assert.False(model.UpdateChecksEnabled);
        Assert.Equal(18, model.Preferences.FontSize);
        Assert.Equal(2, model.Preferences.IndentationWidth);
    }

    [Fact]
    public void AValidChangeAppliesAtOnceSavesAndRaisesChanged()
    {
        SettingsViewModel model = Create();
        List<UserSettings> changes = [];
        model.Changed += changes.Add;

        model.FontSize = 20;

        Assert.Equal(20, model.Current.EditorFontSize);
        Assert.Equal(20, model.Preferences.FontSize);
        Assert.Equal(20, store.Saved.Single().EditorFontSize);
        Assert.Equal(20, changes.Single().EditorFontSize);
        Assert.Equal(SettingsViewModel.SAVED_TEXT, model.SaveStatus);
        Assert.Equal(string.Empty, model.FontSizeError);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(49)]
    [InlineData(-1)]
    [InlineData(1000)]
    public void AnInvalidFontSizeIsRejectedWithAMessageAndNothingChanges(int value)
    {
        SettingsViewModel model = Create();
        int changes = 0;
        model.Changed += _ => changes++;

        model.FontSize = value;

        Assert.Equal(14, model.Current.EditorFontSize);
        Assert.Equal(14, model.Preferences.FontSize);
        Assert.Empty(store.Saved);
        Assert.Equal(0, changes);
        Assert.Contains("from 8 to 48", model.FontSizeError, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonNumberOrAFractionIsRejected()
    {
        SettingsViewModel model = Create();

        model.FontSizeInput = null;
        Assert.Contains("whole number", model.FontSizeError, StringComparison.Ordinal);

        model.FontSizeInput = 12.5m;
        Assert.Contains("whole number", model.FontSizeError, StringComparison.Ordinal);
        Assert.Equal(14, model.Current.EditorFontSize);

        model.FontSizeInput = 12m;
        Assert.Equal(12, model.Current.EditorFontSize);
        Assert.Equal(string.Empty, model.FontSizeError);
    }

    [Fact]
    public void EachNumberSettingIsValidatedAndAppliedOnItsOwn()
    {
        SettingsViewModel model = Create();

        model.IndentationWidth = 2;
        model.RunTimeoutSeconds = 45;
        model.HistoryRetention = 10;
        model.RunTimeoutSeconds = 0;
        model.HistoryRetention = 5_000;
        model.IndentationWidth = 9;

        Assert.Equal(2, model.Current.IndentationWidth);
        Assert.Equal(45, model.Current.RunTimeoutSeconds);
        Assert.Equal(10, model.Current.HistoryRetention);
        Assert.NotEqual(string.Empty, model.RunTimeoutError);
        Assert.NotEqual(string.Empty, model.HistoryRetentionError);
        Assert.NotEqual(string.Empty, model.IndentationWidthError);
        Assert.Equal(2, model.Preferences.IndentationWidth);
    }

    [Fact]
    public void ThemeAndTheUpdatePreferenceApplyAndSave()
    {
        SettingsViewModel model = Create();

        model.Theme = ThemePreference.Dark;
        model.UpdateChecksEnabled = false;

        Assert.Equal(ThemePreference.Dark, model.Current.Theme);
        Assert.False(model.Current.UpdateChecksEnabled);
        Assert.Equal(2, store.Saved.Count);
    }

    [Fact]
    public void ASaveFailureKeepsTheSettingsForThisSessionAndSaysSo()
    {
        store.Failure = new UserSettingsStorageException("read_only");
        SettingsViewModel model = Create();

        model.FontSize = 22;

        Assert.Equal(22, model.Current.EditorFontSize);
        Assert.Equal(22, model.Preferences.FontSize);
        Assert.Equal(SettingsViewModel.SAVE_FAILED_TEXT, model.SaveStatus);
        Assert.Equal(["read_only"], failures);
    }

    [Fact]
    public void ResetRestoresTheDefaultsAndClearsTheErrors()
    {
        SettingsViewModel model = Create(new UserSettings(ThemePreference.Dark, 30, 8, 100, 500, false));
        model.FontSize = 1;

        model.ResetToDefaults();

        Assert.Equal(UserSettings.Default, model.Current);
        Assert.Equal(14, model.FontSize);
        Assert.Equal(string.Empty, model.FontSizeError);
        Assert.Equal(UserSettings.Default, store.Saved.Last());
    }

    [Fact]
    public async Task ClearHistoryDeletesRunsAndSaysThatExperimentsStay()
    {
        SettingsViewModel model = Create();
        Codepad.Domain.Experiments.Experiment experiment = Codepad.Domain.Experiments.Experiment.Create(Codepad.Domain.Experiments.ExperimentId.New(), Codepad.Domain.Operations.OperationKind.CSharp, "t", "1", MemoryHistory.Start);
        history.Seed(experiment, Codepad.Domain.Executions.ExecutionOutcome.Success, 1);
        history.Seed(experiment, Codepad.Domain.Executions.ExecutionOutcome.Success, 2);
        int cleared = 0;
        model.HistoryCleared += () => cleared++;

        await model.ClearHistoryAsync(TestContext.Current.CancellationToken);

        Assert.Empty(history.Records);
        Assert.Equal("Deleted 2 saved runs. Your experiments and their source are unchanged.", model.HistoryStatus);
        Assert.Equal(1, cleared);
    }

    [Fact]
    public async Task ClearHistoryFailureShowsAFriendlySentenceAndReportsOnlyTheReason()
    {
        SettingsViewModel model = Create();
        history.ClearFailure = "busy";

        await model.ClearHistoryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SettingsViewModel.CLEAR_FAILED_TEXT, model.HistoryStatus);
        Assert.Equal(["busy"], failures);
    }

    [Fact]
    public async Task WithoutHistoryClearIsUnavailable()
    {
        SettingsViewModel model = Create(withHistory: false);

        Assert.False(model.CanClearHistory);
        await model.ClearHistoryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(string.Empty, model.HistoryStatus);
    }

    [Fact]
    public void TheUpdateCheckIsNotAvailableYetAndExplainsIt()
    {
        SettingsViewModel model = Create();

        Assert.False(model.CanCheckForUpdates);
        Assert.Contains("later version", model.UpdateStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithAnUpdaterTheCheckRunsOnRequestAndShowsItsSentenceAndFailureReportsOnlyTheType()
    {
        FakeUpdates updates = new FakeUpdates();
        SettingsViewModel model = new SettingsViewModel(store, UserSettings.Default, null, failures.Add, updates);
        Assert.True(model.CanCheckForUpdates);

        await model.CheckForUpdatesAsync(TestContext.Current.CancellationToken);
        Assert.Equal("You have the newest version.", model.UpdateStatus);

        updates.Failure = new InvalidOperationException("secret url");
        await model.CheckForUpdatesAsync(TestContext.Current.CancellationToken);
        Assert.Equal("CodePad couldn't check for updates.", model.UpdateStatus);
        Assert.Equal([nameof(InvalidOperationException)], failures);
    }

    [Fact]
    public void TheUpdatePreferenceIsKeptWhetherOrNotAnUpdaterExists()
    {
        SettingsViewModel model = Create();

        model.UpdateChecksEnabled = false;

        Assert.False(store.Saved.Single().UpdateChecksEnabled);
    }

    [Fact]
    public void TheShortcutListHasTheDocumentedKeys()
    {
        string[] keys = [.. KeyContract.All.Select(item => item.Keys)];

        Assert.Contains("Ctrl+N", keys);
        Assert.Contains("Ctrl+Enter", keys);
        Assert.Contains("Ctrl+K", keys);
        Assert.Contains("Ctrl+P", keys);
        Assert.Contains("Ctrl+Tab", keys);
        Assert.Contains("Ctrl+Shift+Tab", keys);
        Assert.Contains("Ctrl+W", keys);
        Assert.Contains("Escape", keys);
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    private sealed class FakeUpdates : IUpdateCheckHandoff
    {
        public Exception? Failure { get; set; }

        public Task<string> CheckAsync(CancellationToken cancellationToken = default) =>
            Failure is null ? Task.FromResult("You have the newest version.") : Task.FromException<string>(Failure);
    }

    private sealed class MemoryStore : IUserSettingsStore
    {
        public List<UserSettings> Saved { get; } = [];

        public UserSettingsStorageException? Failure { get; set; }

        public UserSettingsReadResult Read() => new(UserSettings.Default, []);

        public void Save(UserSettings settings)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Saved.Add(settings);
        }
    }
}
