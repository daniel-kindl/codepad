using System.Globalization;
using System.Text.RegularExpressions;
using Codepad.Application.Library;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;

namespace Codepad.Desktop.Library;

/// <summary>One row of the library list.</summary>
/// <param name="Id">The experiment.</param>
/// <param name="Title">The title.</param>
/// <param name="KindText">The kind as a word.</param>
/// <param name="ModifiedText">When the experiment was last edited, in local time.</param>
/// <param name="Preview">The start of the source on one line.</param>
public sealed partial record LibraryItem(ExperimentId Id, string Title, string KindText, string ModifiedText, string Preview)
{
    private const int MAX_PREVIEW = 120;

    /// <summary>Gets the text that a screen reader reads for the row.</summary>
    public string AccessibleName => Preview.Length > 0
        ? $"{Title}, {KindText}, edited {ModifiedText}. {Preview}"
        : $"{Title}, {KindText}, edited {ModifiedText}. The source is empty.";

    /// <summary>Makes a row from a search result.</summary>
    /// <param name="summary">The result.</param>
    /// <returns>The row.</returns>
    public static LibraryItem From(ExperimentSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        string oneLine = WhiteSpace().Replace(summary.Preview, " ").Trim();
        if (oneLine.Length > MAX_PREVIEW)
        {
            int cut = char.IsHighSurrogate(oneLine[MAX_PREVIEW - 1]) ? MAX_PREVIEW - 1 : MAX_PREVIEW;
            oneLine = oneLine[..cut].TrimEnd() + "…";
        }

        return new LibraryItem(
            summary.Id,
            summary.Title,
            summary.Kind == OperationKind.Json ? "JSON" : "C#",
            summary.ModifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            oneLine);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
}
