namespace Codepad.Application.Commands;

/// <summary>
/// Finds and ranks commands for a text. The text is never a pattern: each character matches itself. An exact label ranks
/// first, then a label that starts with the text, then a word that starts with it, then a label that contains it, then a
/// label that has its characters in order. A keyword ranks below the same match on the label. Equal scores keep
/// the registration order. A disabled command is listed, with its reason.
/// </summary>
public static class CommandSearch
{
    private const int EXACT = 1_000;
    private const int PREFIX = 800;
    private const int WORD_START = 600;
    private const int CONTAINS = 400;
    private const int SUBSEQUENCE = 200;
    private const int KEYWORD_PENALTY = 50;

    /// <summary>Ranks the commands that match a text.</summary>
    /// <param name="commands">The commands in registration order.</param>
    /// <param name="query">The text. An empty text matches all commands.</param>
    /// <returns>The matches, best first.</returns>
    public static IReadOnlyList<CommandMatch> Rank(IEnumerable<AppCommand> commands, string query)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(query);
        string text = query.Trim();
        List<(CommandMatch Match, int Order)> found = [];
        int order = 0;
        foreach (AppCommand command in commands)
        {
            int? score = text.Length == 0 ? 0 : Score(command, text);
            if (score is int value)
            {
                found.Add((new CommandMatch(command, command.Availability(), value), order));
            }

            order++;
        }

        return [.. found.OrderByDescending(item => item.Match.Score).ThenBy(item => item.Order).Select(item => item.Match)];
    }

    private static int? Score(AppCommand command, string text)
    {
        int? label = ScoreText(command.Label, text);
        int? keyword = command.Keywords is null ? null : ScoreText(command.Keywords, text) - KEYWORD_PENALTY;
        return label is null ? keyword : keyword is null ? label : Math.Max(label.Value, keyword.Value);
    }

    private static int? ScoreText(string candidate, string text)
    {
        if (candidate.Equals(text, StringComparison.OrdinalIgnoreCase))
        {
            return EXACT;
        }

        if (candidate.StartsWith(text, StringComparison.OrdinalIgnoreCase))
        {
            return PREFIX - Math.Min(candidate.Length - text.Length, 99);
        }

        int index = candidate.IndexOf(text, StringComparison.OrdinalIgnoreCase);
        if (index > 0 && !char.IsLetterOrDigit(candidate[index - 1]))
        {
            return WORD_START - Math.Min(index, 99);
        }

        if (index >= 0)
        {
            return CONTAINS - Math.Min(index, 99);
        }

        return SubsequenceGaps(candidate, text) is int gaps ? SUBSEQUENCE - Math.Min(gaps, 99) : null;
    }

    // Counts the characters that the match skips. A tight match has a higher score.
    private static int? SubsequenceGaps(string candidate, string text)
    {
        int position = 0;
        int gaps = 0;
        foreach (char wanted in text)
        {
            int found = candidate.IndexOf(wanted, position, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return null;
            }

            gaps += found - position;
            position = found + 1;
        }

        return gaps;
    }
}
