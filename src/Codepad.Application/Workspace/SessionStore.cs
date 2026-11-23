using System.Text.Json;
using Codepad.Application.Experiments;
using Codepad.Domain.Experiments;

namespace Codepad.Application.Workspace;

/// <summary>
/// Stores the open tabs and the active tab in the preference table, apart from the experiment data. The stored text
/// has only experiment IDs. It has no path and no source, so it works the same on Linux and Windows. Damaged or
/// unknown data gives an empty set. It never throws for bad data.
/// </summary>
/// <param name="preferences">The preference store.</param>
public sealed class SessionStore(IPreferenceRepository preferences)
{
    /// <summary>The preference key of the session.</summary>
    public const string KEY = "session.tabs";

    private const int VERSION = 1;

    /// <summary>Saves the tab set.</summary>
    /// <param name="tabs">The tabs.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that ends when the write ends.</returns>
    public Task SaveAsync(TabSet tabs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        string text = JsonSerializer.Serialize(new Stored(VERSION, [.. tabs.Open.Select(id => id.ToString())], tabs.Active?.ToString()));
        return preferences.SetAsync(KEY, text, cancellationToken);
    }

    /// <summary>Loads the tab set. An ID that <paramref name="exists"/> rejects is omitted.</summary>
    /// <param name="exists">Tells whether an experiment still exists.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The set. It is empty when nothing is stored or the data is damaged.</returns>
    public async Task<TabSet> LoadAsync(Func<ExperimentId, bool> exists, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exists);
        string? text = await preferences.GetAsync(KEY, cancellationToken).ConfigureAwait(false);
        if (text is null)
        {
            return TabSet.Empty;
        }

        try
        {
            Stored? stored = JsonSerializer.Deserialize<Stored>(text);
            if (stored is null || stored.Version != VERSION || stored.Open is null)
            {
                return TabSet.Empty;
            }

            return TabSet.Restore(stored.Open.Select(Parse).OfType<ExperimentId>(), Parse(stored.Active), exists);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            return TabSet.Empty;
        }
    }

    private static ExperimentId? Parse(string? text) => Guid.TryParse(text, out Guid value) && value != Guid.Empty ? new ExperimentId(value) : null;

    private sealed record Stored(int Version, List<string>? Open, string? Active);
}
