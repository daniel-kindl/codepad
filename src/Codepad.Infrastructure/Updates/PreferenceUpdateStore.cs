using System.Globalization;
using Codepad.Application.Experiments;
using Codepad.Application.Updates;

namespace Codepad.Infrastructure.Updates;

/// <summary>Keeps the time of the last update check and the first-run explanation in the preference table of the database. A failed read is the same as no value.</summary>
/// <param name="preferences">The preference store.</param>
public sealed class PreferenceUpdateStore(IPreferenceRepository preferences) : IUpdateStore
{
    private const string LAST_CHECK_KEY = "update.lastCheck";
    private const string PROMPT_KEY = "update.promptShown";

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetLastCheckAsync()
    {
        try
        {
            string? text = await preferences.GetAsync(LAST_CHECK_KEY).ConfigureAwait(false);
            return DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset value) ? value : null;
        }
        catch (ExperimentStorageException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetLastCheckAsync(DateTimeOffset time)
    {
        try
        {
            await preferences.SetAsync(LAST_CHECK_KEY, time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).ConfigureAwait(false);
        }
        catch (ExperimentStorageException)
        {
            // The check still ran. The next start may check again. The data store has its own warning.
        }
    }

    /// <inheritdoc />
    public async Task<bool> GetPromptShownAsync()
    {
        try
        {
            return await preferences.GetAsync(PROMPT_KEY).ConfigureAwait(false) == "1";
        }
        catch (ExperimentStorageException)
        {
            return true;
        }
    }

    /// <inheritdoc />
    public async Task SetPromptShownAsync()
    {
        try
        {
            await preferences.SetAsync(PROMPT_KEY, "1").ConfigureAwait(false);
        }
        catch (ExperimentStorageException)
        {
            // The explanation can show again at the next start. That is better than a lost choice.
        }
    }
}
