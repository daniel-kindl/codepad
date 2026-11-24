using Codepad.Application.Commands;

namespace Codepad.Tests.Commands;

public sealed class CommandSearchTests
{
    private static AppCommand Command(string id, string label, bool enabled = true, string? reason = null, string? keywords = null) =>
        new AppCommand(id, label, () => new CommandAvailability(enabled, reason), () => Task.CompletedTask, null, keywords);

    private static readonly AppCommand[] _all =
    [
        Command("new.csharp", "New C# experiment"),
        Command("new.json", "New JSON experiment"),
        Command("run", "Run C#", keywords: "execute"),
        Command("library", "Open library"),
        Command("next", "Next tab"),
        Command("settings", "Settings", enabled: false, reason: "Settings are not available yet."),
    ];

    private static string[] Ids(string query) => [.. CommandSearch.Rank(_all, query).Select(match => match.Command.Id)];

    [Fact]
    public void AnEmptyQueryListsEverythingInRegistrationOrder()
    {
        Assert.Equal(["new.csharp", "new.json", "run", "library", "next", "settings"], Ids(string.Empty));
        Assert.Equal(Ids(string.Empty), Ids("   "));
    }

    [Fact]
    public void ExactBeatsPrefixBeatsWordStartBeatsSubstringBeatsSubsequence()
    {
        AppCommand[] commands =
        [
            Command("subsequence", "Open Reference Notes"),
            Command("substring", "Preview runs"),
            Command("word", "Show run history"),
            Command("prefix", "Running tasks"),
            Command("exact", "run"),
        ];

        string[] ids = [.. CommandSearch.Rank(commands, "run").Select(match => match.Command.Id)];

        Assert.Equal(["exact", "prefix", "word", "substring"], ids);
    }

    [Fact]
    public void ASubsequenceMatchFindsAbbreviationsAndRanksLast()
    {
        Assert.Equal(["new.json"], Ids("njs"));
        Assert.Equal("new.csharp", Ids("nce")[0]);
    }

    [Fact]
    public void MatchingIgnoresCase()
    {
        Assert.Equal(["run"], Ids("RUN C"));
        Assert.Equal(["settings"], Ids("SETT"));
    }

    [Fact]
    public void KeywordsFindACommandButRankBelowALabelMatch()
    {
        Assert.Equal(["run"], Ids("execute"));
    }

    [Fact]
    public void ADisabledCommandIsListedWithItsReason()
    {
        CommandMatch match = Assert.Single(CommandSearch.Rank(_all, "settings"));

        Assert.False(match.Availability.Enabled);
        Assert.Equal("Settings are not available yet.", match.Availability.Reason);
    }

    [Fact]
    public void NothingMatchesGivesAnEmptyList()
    {
        Assert.Empty(Ids("zzzz"));
    }

    [Fact]
    public void SpecialCharactersMatchLiterally()
    {
        Assert.Equal(["new.csharp", "run"], Ids("c#"));
        Assert.Empty(Ids(".*"));
        Assert.Empty(Ids("[a-z]"));
    }

    [Fact]
    public void RankingIsStableForEqualScores()
    {
        AppCommand[] commands = [Command("b", "Alpha one"), Command("a", "Alpha two")];

        Assert.Equal(["b", "a"], CommandSearch.Rank(commands, "alpha").Select(match => match.Command.Id));
    }

    [Fact]
    public void TheAvailabilityIsReadAtSearchTime()
    {
        bool enabled = false;
        AppCommand command = new AppCommand("x", "Thing", () => new CommandAvailability(enabled, "No."), () => Task.CompletedTask);

        Assert.False(CommandSearch.Rank([command], "thing")[0].Availability.Enabled);
        enabled = true;
        Assert.True(CommandSearch.Rank([command], "thing")[0].Availability.Enabled);
    }

    [Fact]
    public void ACommandNeedsAnIdAndALabelAndADisabledOneNeedsAReason()
    {
        Assert.Throws<ArgumentException>(() => new AppCommand(" ", "L", () => CommandAvailability.Available, () => Task.CompletedTask));
        Assert.Throws<ArgumentException>(() => new AppCommand("i", "", () => CommandAvailability.Available, () => Task.CompletedTask));
        Assert.Throws<ArgumentException>(() => new CommandAvailability(false, null));
    }
}
