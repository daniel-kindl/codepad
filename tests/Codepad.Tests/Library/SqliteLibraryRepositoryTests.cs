using System.Diagnostics;
using Codepad.Application.Library;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Infrastructure.Storage;
using Codepad.Tests.Experiments;

namespace Codepad.Tests.Library;

public sealed class SqliteLibraryRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset _start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TempDatabase temp = new TempDatabase();
    private readonly SqliteExperimentRepository experiments;
    private readonly SqliteLibraryRepository library;

    public SqliteLibraryRepositoryTests()
    {
        SqliteDatabase database = temp.Open();
        experiments = new SqliteExperimentRepository(database);
        library = new SqliteLibraryRepository(database);
    }

    public void Dispose() => temp.Dispose();

    private async Task<Experiment> AddAsync(string title, string source, OperationKind kind = OperationKind.CSharp, int minutes = 0)
    {
        Experiment experiment = Experiment.Create(ExperimentId.New(), kind, title, source, _start.AddMinutes(minutes));
        await experiments.SaveAsync(experiment, TestContext.Current.CancellationToken);
        return experiment;
    }

    private async Task<string[]> TitlesAsync(string text, OperationKind? kind = null)
    {
        LibraryPage page = await library.SearchAsync(new LibraryQuery(text, kind), TestContext.Current.CancellationToken);
        return [.. page.Items.Select(item => item.Title)];
    }

    [Fact]
    public async Task AnEmptyQueryListsTheNewestEditFirstWithMetadata()
    {
        await AddAsync("old", "a", minutes: 1);
        Experiment newest = await AddAsync("new", "b", OperationKind.Json, minutes: 5);

        LibraryPage page = await library.SearchAsync(new LibraryQuery(), TestContext.Current.CancellationToken);

        Assert.Equal(["new", "old"], page.Items.Select(item => item.Title));
        ExperimentSummary first = page.Items[0];
        Assert.Equal(newest.Id, first.Id);
        Assert.Equal(OperationKind.Json, first.Kind);
        Assert.Equal("b", first.Preview);
        Assert.Equal(newest.ModifiedAt, first.ModifiedAt);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task FindsByTitleBySourceFragmentAndByKind()
    {
        await AddAsync("Invoice parser", "var x = 1;", minutes: 1);
        await AddAsync("Payload", "{ \"total\": 5 }", OperationKind.Json, minutes: 2);
        await AddAsync("Other", "Console.WriteLine(\"hello\");", minutes: 3);

        Assert.Equal(["Invoice parser"], await TitlesAsync("invoice"));
        Assert.Equal(["Payload"], await TitlesAsync("TOTAL"));
        Assert.Equal(["Other"], await TitlesAsync("writeline"));
        Assert.Equal(["Payload"], await TitlesAsync("json"));
        Assert.Equal(["Other", "Invoice parser"], await TitlesAsync("c#"));
        Assert.Equal(["Other", "Invoice parser"], await TitlesAsync("CSharp"));
        Assert.Equal(["Payload"], await TitlesAsync(string.Empty, OperationKind.Json));
        Assert.Empty(await TitlesAsync("total", OperationKind.CSharp));
    }

    [Theory]
    [InlineData("100%", "Discount 100% off")]
    [InlineData("a_b", "name a_b here")]
    [InlineData("back\\slash", "path back\\slash end")]
    [InlineData("it's", "say it's fine")]
    [InlineData("\"quoted\"", "say \"quoted\" text")]
    [InlineData("x; DROP TABLE experiments;--", "x; DROP TABLE experiments;--")]
    public async Task SpecialCharactersMatchLiterally(string needle, string source)
    {
        await AddAsync("hit", source, minutes: 1);
        await AddAsync("decoy", "100 percent a b backslash its quoted", minutes: 2);

        Assert.Equal(["hit"], await TitlesAsync(needle));
        Assert.Single(await experiments.ListAsync(TestContext.Current.CancellationToken), item => item.Title == "decoy");
    }

    [Fact]
    public async Task PercentAndUnderscoreAreNotWildcards()
    {
        await AddAsync("one", "abc", minutes: 1);

        Assert.Empty(await TitlesAsync("%"));
        Assert.Empty(await TitlesAsync("a_c"));
    }

    [Fact]
    public async Task IgnoresCaseAlsoForNonAsciiLetters()
    {
        await AddAsync("Žluťoučký kůň", "ÁBC", minutes: 1);

        Assert.Equal(["Žluťoučký kůň"], await TitlesAsync("žLUŤ"));
        Assert.Equal(["Žluťoučký kůň"], await TitlesAsync("ábc"));
    }

    [Fact]
    public async Task NoMatchGivesAnEmptyPage()
    {
        await AddAsync("one", "abc");

        LibraryPage page = await library.SearchAsync(new LibraryQuery("zzz"), TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task APageIsBoundedAndHasMoreSaysSo()
    {
        for (int index = 0; index < 7; index++)
        {
            await AddAsync($"item {index}", "x", minutes: index);
        }

        LibraryPage first = await library.SearchAsync(new LibraryQuery(limit: 3), TestContext.Current.CancellationToken);
        LibraryPage second = await library.SearchAsync(new LibraryQuery(limit: 3, offset: 3), TestContext.Current.CancellationToken);
        LibraryPage last = await library.SearchAsync(new LibraryQuery(limit: 3, offset: 6), TestContext.Current.CancellationToken);

        Assert.Equal(["item 6", "item 5", "item 4"], first.Items.Select(item => item.Title));
        Assert.True(first.HasMore);
        Assert.Equal(["item 3", "item 2", "item 1"], second.Items.Select(item => item.Title));
        Assert.True(second.HasMore);
        Assert.Equal(["item 0"], last.Items.Select(item => item.Title));
        Assert.False(last.HasMore);
    }

    [Fact]
    public async Task ThePreviewIsBoundedAndTheSearchStillSeesTheWholeSource()
    {
        string source = new string('a', 5_000) + "NEEDLE";
        await AddAsync("long", source);

        LibraryPage page = await library.SearchAsync(new LibraryQuery("needle"), TestContext.Current.CancellationToken);

        Assert.Equal(LibraryQuery.PreviewLength, Assert.Single(page.Items).Preview.Length);
    }

    [Fact]
    public async Task ThePreviewDoesNotCutASurrogatePair()
    {
        await AddAsync("emoji", new string('a', LibraryQuery.PreviewLength - 1) + "\U0001F600tail");

        LibraryPage page = await library.SearchAsync(new LibraryQuery(), TestContext.Current.CancellationToken);

        string preview = Assert.Single(page.Items).Preview;
        Assert.False(char.IsHighSurrogate(preview[^1]));
    }

    [Fact]
    public async Task RenamesAndEditsShowAfterTheyAreSaved()
    {
        Experiment first = await AddAsync("Before", "old text", minutes: 1);
        Assert.Equal(["Before"], await TitlesAsync("old text"));

        await experiments.SaveAsync(first.WithTitle("After", _start.AddMinutes(2)).WithSource("new text", _start.AddMinutes(3)), TestContext.Current.CancellationToken);

        Assert.Empty(await TitlesAsync("old text"));
        Assert.Equal(["After"], await TitlesAsync("new text"));
        Assert.Equal(["After"], await TitlesAsync("after"));
        Assert.Empty(await TitlesAsync("before"));
    }

    [Fact]
    public async Task ASearchNeverChangesAnExperiment()
    {
        Experiment one = await AddAsync("keep", "same", minutes: 1);

        await TitlesAsync("same");
        await TitlesAsync(string.Empty);

        Assert.Equal(one, await experiments.GetAsync(one.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADamagedRowDoesNotHideTheOthers()
    {
        await AddAsync("good", "text", minutes: 1);
        using (Microsoft.Data.Sqlite.SqliteConnection connection = temp.RawConnection())
        {
            using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO experiments (id, kind, title, source, input, created_at, modified_at, revision) VALUES ('not-a-guid', 'CSharp', 'bad', 'text', '', 'x', 'x', 1);";
            command.ExecuteNonQuery();
        }

        Assert.Equal(["good"], await TitlesAsync("text"));
    }

    [Fact]
    public async Task ThousandsOfExperimentsGiveABoundedPageQuickly()
    {
        using (Microsoft.Data.Sqlite.SqliteConnection connection = temp.RawConnection())
        {
            using Microsoft.Data.Sqlite.SqliteTransaction transaction = connection.BeginTransaction();
            using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO experiments (id, kind, title, source, input, created_at, modified_at, revision) VALUES ($id, 'CSharp', $title, $source, '', $time, $time, 1);";
            Microsoft.Data.Sqlite.SqliteParameter id = command.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Text);
            Microsoft.Data.Sqlite.SqliteParameter title = command.Parameters.Add("$title", Microsoft.Data.Sqlite.SqliteType.Text);
            Microsoft.Data.Sqlite.SqliteParameter source = command.Parameters.Add("$source", Microsoft.Data.Sqlite.SqliteType.Text);
            Microsoft.Data.Sqlite.SqliteParameter time = command.Parameters.Add("$time", Microsoft.Data.Sqlite.SqliteType.Text);
            string body = new string('x', 1_000);
            for (int index = 0; index < 5_000; index++)
            {
                id.Value = ExperimentId.New().ToString();
                title.Value = $"Experiment {index}";
                source.Value = index == 4_321 ? body + "rare-token" : body;
                time.Value = _start.AddSeconds(index).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        Stopwatch clock = Stopwatch.StartNew();
        LibraryPage recent = await library.SearchAsync(new LibraryQuery(), TestContext.Current.CancellationToken);
        LibraryPage rare = await library.SearchAsync(new LibraryQuery("RARE-token"), TestContext.Current.CancellationToken);
        LibraryPage unicode = await library.SearchAsync(new LibraryQuery("ŽŽŽ-none"), TestContext.Current.CancellationToken);
        clock.Stop();

        Assert.Equal(LibraryQuery.DefaultLimit, recent.Items.Count);
        Assert.True(recent.HasMore);
        Assert.Equal("Experiment 4321", Assert.Single(rare.Items).Title);
        Assert.Empty(unicode.Items);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Three searches of 5,000 experiments took {clock.Elapsed}.");
    }

    [Fact]
    public async Task ACancelledSearchStops()
    {
        await AddAsync("one", "abc");
        using CancellationTokenSource cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.SearchAsync(new LibraryQuery("abc"), cancel.Token));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(201, 0)]
    [InlineData(10, -1)]
    public void ALibraryQueryRefusesBadPaging(int limit, int offset)
    {
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new LibraryQuery(limit: limit, offset: offset));
    }

    [Fact]
    public async Task WalkingThePagesWithADamagedRowRepeatsAndSkipsNothing()
    {
        for (int index = 0; index < 10; index++)
        {
            await AddAsync($"t{index}", "x", minutes: index);
        }

        using (Microsoft.Data.Sqlite.SqliteConnection connection = temp.RawConnection())
        {
            using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO experiments (id, kind, title, source, input, created_at, modified_at, revision) VALUES ('not-a-guid', 'CSharp', 'bad', 'x', '', 'x', '2026-01-01T00:07:30.0000000+00:00', 1);";
            command.ExecuteNonQuery();
        }

        List<string> seen = [];
        int offset = 0;
        for (int guard = 0; guard < 10; guard++)
        {
            LibraryPage page = await library.SearchAsync(new LibraryQuery(limit: 5, offset: offset), TestContext.Current.CancellationToken);
            seen.AddRange(page.Items.Select(item => item.Title));
            offset = page.NextOffset;
            if (!page.HasMore)
            {
                break;
            }
        }

        Assert.Equal(["t9", "t8", "t7", "t6", "t5", "t4", "t3", "t2", "t1", "t0"], seen);
    }

    [Fact]
    public async Task AVeryLongQueryIsCutAndStillSearches()
    {
        await AddAsync("one", "abc");

        LibraryPage page = await library.SearchAsync(new LibraryQuery(new string('a', 100_000)), TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
        Assert.Equal(LibraryQuery.MaxTextLength, new LibraryQuery(new string('a', 100_000)).Text.Length);
    }

    [Fact]
    public async Task ANulCharacterInTheQueryDoesNotBreakTheSearch()
    {
        await AddAsync("one", "abc");

        Assert.Empty(await TitlesAsync("a\0b"));
    }
}
