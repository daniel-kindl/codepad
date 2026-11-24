using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Codepad.Application.Experiments;
using Codepad.Application.History;
using Codepad.Application.Settings;

namespace Codepad.Desktop.Settings;

/// <summary>
/// The live settings. A valid change applies at once to the views and is saved. An invalid change is refused with a message
/// and changes nothing. A failed save keeps the change for this session and says so. A change of the timeout or the
/// retention applies to new runs only, because the run policy comes from <see cref="Current"/> when a run starts.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    /// <summary>The status after a successful save.</summary>
    internal const string SAVED_TEXT = "Saved";

    /// <summary>The status after a failed save.</summary>
    internal const string SAVE_FAILED_TEXT = "CodePad couldn't save your settings. They stay in use until you close CodePad.";

    /// <summary>The status when clearing the history failed.</summary>
    internal const string CLEAR_FAILED_TEXT = "CodePad couldn't clear the history.";

    private const string WHOLE_NUMBER_TEXT = "Enter a whole number.";

    private readonly IUserSettingsStore store;
    private readonly IRunHistoryRepository? history;
    private readonly IUpdateCheckHandoff? updates;
    private string updateStatus;
    private readonly Action<string> reportFailure;
    private readonly Dictionary<string, string> errors = [];
    private UserSettings current;
    private string saveStatus = string.Empty;
    private string historyStatus = string.Empty;

    /// <summary>Initializes a new instance of the <see cref="SettingsViewModel"/> class.</summary>
    /// <param name="store">Saves the settings.</param>
    /// <param name="initial">The valid settings from the last start.</param>
    /// <param name="history">The saved runs, for the clear command. Null when run history is not available.</param>
    /// <param name="reportFailure">Receives a reason code when a save or a clear fails. It never holds a path or a value. Null ignores it.</param>
    /// <param name="updates">The manual update check. Null when the app has no updater yet.</param>
    public SettingsViewModel(IUserSettingsStore store, UserSettings initial, IRunHistoryRepository? history = null, Action<string>? reportFailure = null, IUpdateCheckHandoff? updates = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(initial);
        if (UserSettingsRules.Validate(initial).Count > 0)
        {
            throw new ArgumentException("The initial settings are not valid.", nameof(initial));
        }

        this.store = store;
        this.history = history;
        this.updates = updates;
        this.reportFailure = reportFailure ?? (_ => { });
        current = initial;
        updateStatus = updates is null ? "Checking for updates arrives in a later version. Your choice is saved for then." : string.Empty;
        Preferences.Update(initial.EditorFontSize, initial.IndentationWidth);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised on the UI thread after a valid change. The app applies the theme from it.</summary>
    public event Action<UserSettings>? Changed;

    /// <summary>Raised after the saved runs were deleted. The history panel reloads.</summary>
    public event Action? HistoryCleared;

    /// <summary>Gets the editor settings that the views apply.</summary>
    public EditorPreferences Preferences { get; } = new EditorPreferences();

    /// <summary>Gets the settings in use. A run reads its policy from here when it starts. Readers on other threads see one whole value.</summary>
    public UserSettings Current => Volatile.Read(ref current);

    /// <summary>Gets or sets the theme.</summary>
    public ThemePreference Theme
    {
        get => Current.Theme;
        set => Apply(Current with { Theme = value });
    }

    /// <summary>Gets or sets the index of the theme in the list System, Light, Dark. A list control binds to it.</summary>
    public int ThemeIndex
    {
        get => (int)Current.Theme;
        set
        {
            if (Enum.IsDefined((ThemePreference)value))
            {
                Theme = (ThemePreference)value;
            }
        }
    }

    /// <summary>Gets or sets the editor font size. A value outside the range is refused.</summary>
    public int FontSize
    {
        get => Current.EditorFontSize;
        set => SetNumber(nameof(UserSettings.EditorFontSize), value);
    }

    /// <summary>Gets or sets the font size as a number box gives it. A value that is not a whole number is refused.</summary>
    public decimal? FontSizeInput
    {
        get => Current.EditorFontSize;
        set => SetInput(nameof(UserSettings.EditorFontSize), value);
    }

    /// <summary>Gets or sets the indentation width. A value outside the range is refused.</summary>
    public int IndentationWidth
    {
        get => Current.IndentationWidth;
        set => SetNumber(nameof(UserSettings.IndentationWidth), value);
    }

    /// <summary>Gets or sets the indentation width as a number box gives it.</summary>
    public decimal? IndentationWidthInput
    {
        get => Current.IndentationWidth;
        set => SetInput(nameof(UserSettings.IndentationWidth), value);
    }

    /// <summary>Gets or sets the run timeout in seconds. A value outside the range is refused. A change applies to new runs only.</summary>
    public int RunTimeoutSeconds
    {
        get => Current.RunTimeoutSeconds;
        set => SetNumber(nameof(UserSettings.RunTimeoutSeconds), value);
    }

    /// <summary>Gets or sets the run timeout as a number box gives it.</summary>
    public decimal? RunTimeoutInput
    {
        get => Current.RunTimeoutSeconds;
        set => SetInput(nameof(UserSettings.RunTimeoutSeconds), value);
    }

    /// <summary>Gets or sets the number of saved runs for each experiment. A value outside the range is refused. A lower number trims at the next run.</summary>
    public int HistoryRetention
    {
        get => Current.HistoryRetention;
        set => SetNumber(nameof(UserSettings.HistoryRetention), value);
    }

    /// <summary>Gets or sets the history retention as a number box gives it.</summary>
    public decimal? HistoryRetentionInput
    {
        get => Current.HistoryRetention;
        set => SetInput(nameof(UserSettings.HistoryRetention), value);
    }

    /// <summary>Gets or sets a value indicating whether the app may check for updates.</summary>
    public bool UpdateChecksEnabled
    {
        get => Current.UpdateChecksEnabled;
        set => Apply(Current with { UpdateChecksEnabled = value });
    }

    /// <summary>Gets the message for the font size, or an empty string.</summary>
    public string FontSizeError => ErrorFor(nameof(UserSettings.EditorFontSize));

    /// <summary>Gets the message for the indentation width, or an empty string.</summary>
    public string IndentationWidthError => ErrorFor(nameof(UserSettings.IndentationWidth));

    /// <summary>Gets the message for the run timeout, or an empty string.</summary>
    public string RunTimeoutError => ErrorFor(nameof(UserSettings.RunTimeoutSeconds));

    /// <summary>Gets the message for the history retention, or an empty string.</summary>
    public string HistoryRetentionError => ErrorFor(nameof(UserSettings.HistoryRetention));

    /// <summary>Gets the status of the last save for a live region: "Saved", a failure sentence, or an empty string.</summary>
    public string SaveStatus
    {
        get => saveStatus;
        private set => Set(ref saveStatus, value);
    }

    /// <summary>Gets the result of the last clear of the history, or an empty string.</summary>
    public string HistoryStatus
    {
        get => historyStatus;
        private set => Set(ref historyStatus, value);
    }

    /// <summary>Gets a value indicating whether the app can clear the history.</summary>
    public bool CanClearHistory => history is not null;

    /// <summary>Gets a value indicating whether the app can check for updates now. It can when the updater exists and the user allows checks.</summary>
    public bool CanCheckForUpdates => updates is not null;

    /// <summary>Gets the sentence next to the update check.</summary>
    public string UpdateStatus
    {
        get => updateStatus;
        private set => Set(ref updateStatus, value);
    }

    /// <summary>Gets the shortcuts of the keyboard contract.</summary>
    public IReadOnlyList<KeyBinding> Shortcuts { get; } = KeyContract.All;

    /// <summary>Checks for an update because the user asked. It does nothing without an updater.</summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A task that ends when the status shows.</returns>
    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (updates is null)
        {
            return;
        }

        try
        {
            UpdateStatus = await updates.CheckAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException)
        {
            // The updater owns its own logging. The window only says that the check did not work.
            reportFailure(exception.GetType().Name);
            UpdateStatus = "CodePad couldn't check for updates.";
        }
    }

    /// <summary>Restores all settings to the defaults.</summary>
    public void ResetToDefaults()
    {
        errors.Clear();
        Apply(UserSettings.Default);
        RaiseAll();
    }

    /// <summary>
    /// Deletes all saved runs. It never deletes an experiment or its source. The caller asks the user to confirm first.
    /// </summary>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>A task that ends when the status shows.</returns>
    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (history is null)
        {
            return;
        }

        try
        {
            int deleted = await history.ClearAsync(cancellationToken).ConfigureAwait(true);
            HistoryStatus = string.Create(CultureInfo.InvariantCulture, $"Deleted {deleted} saved {(deleted == 1 ? "run" : "runs")}. Your experiments and their source are unchanged.");
            HistoryCleared?.Invoke();
        }
        catch (ExperimentStorageException exception)
        {
            reportFailure(exception.Reason);
            HistoryStatus = CLEAR_FAILED_TEXT;
        }
    }

    private void SetInput(string field, decimal? value)
    {
        if (value is not decimal number || number != decimal.Truncate(number) || number < int.MinValue || number > int.MaxValue)
        {
            errors[field] = WHOLE_NUMBER_TEXT;
            RaiseErrors();
            return;
        }

        SetNumber(field, (int)number);
    }

    private void SetNumber(string field, int value)
    {
        UserSettings candidate = Current.With(field, value);
        SettingsProblem? problem = UserSettingsRules.Validate(candidate).FirstOrDefault(item => item.Field == field);
        if (problem is not null)
        {
            // The old value stays in use. The message says what is allowed.
            errors[field] = problem.Message;
            RaiseErrors();
            return;
        }

        errors.Remove(field);
        RaiseErrors();
        Apply(candidate);
    }

    private void Apply(UserSettings next)
    {
        if (next == Current)
        {
            return;
        }

        Volatile.Write(ref current, next);
        Preferences.Update(next.EditorFontSize, next.IndentationWidth);
        try
        {
            store.Save(next);
            SaveStatus = SAVED_TEXT;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The setting stays in use for this session. The user must know that it will not survive a restart.
            // A store can throw another kind of exception too. The window must not stop, and only a code goes out.
            reportFailure(exception is UserSettingsStorageException storage ? storage.Reason : exception.GetType().Name);
            SaveStatus = SAVE_FAILED_TEXT;
        }

        RaiseAll();
        Changed?.Invoke(next);
    }

    private string ErrorFor(string field) => errors.GetValueOrDefault(field, string.Empty);

    private void RaiseErrors()
    {
        Raise(nameof(FontSizeError));
        Raise(nameof(IndentationWidthError));
        Raise(nameof(RunTimeoutError));
        Raise(nameof(HistoryRetentionError));
    }

    private void RaiseAll()
    {
        foreach (string name in new[]
        {
            nameof(Theme), nameof(ThemeIndex), nameof(FontSize), nameof(FontSizeInput), nameof(IndentationWidth), nameof(IndentationWidthInput),
            nameof(RunTimeoutSeconds), nameof(RunTimeoutInput), nameof(HistoryRetention), nameof(HistoryRetentionInput), nameof(UpdateChecksEnabled),
        })
        {
            Raise(name);
        }

        RaiseErrors();
    }

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field != value)
        {
            field = value;
            Raise(name);
        }
    }

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
