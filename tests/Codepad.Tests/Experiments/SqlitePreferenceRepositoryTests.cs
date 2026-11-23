using Codepad.Infrastructure.Storage;

namespace Codepad.Tests.Experiments;

public sealed class SqlitePreferenceRepositoryTests : IDisposable
{
    private readonly TempDatabase temp = new TempDatabase();

    public void Dispose() => temp.Dispose();

    [Fact]
    public async Task UnsetPreferenceReadsAsNull()
    {
        SqlitePreferenceRepository repository = new SqlitePreferenceRepository(temp.Open());

        Assert.Null(await repository.GetAsync("theme", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetPreferenceSurvivesARestartAndASecondSetReplacesIt()
    {
        await new SqlitePreferenceRepository(temp.Open()).SetAsync("theme", "dark", TestContext.Current.CancellationToken);
        await new SqlitePreferenceRepository(temp.Open()).SetAsync("theme", "light", TestContext.Current.CancellationToken);

        Assert.Equal("light", await new SqlitePreferenceRepository(temp.Open()).GetAsync("theme", TestContext.Current.CancellationToken));
    }
}
