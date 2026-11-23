using Codepad.Application.Experiments;
using Codepad.Application.Workspace;
using Codepad.Domain.Experiments;

namespace Codepad.Tests.Workspace;

public sealed class SessionStoreTests
{
    private readonly MemoryPreferences preferences = new MemoryPreferences();
    private readonly ExperimentId a = ExperimentId.New();
    private readonly ExperimentId b = ExperimentId.New();

    [Fact]
    public async Task NoStoredSessionGivesAnEmptySet()
    {
        TabSet loaded = await new SessionStore(preferences).LoadAsync(_ => true, TestContext.Current.CancellationToken);

        Assert.Empty(loaded.Open);
    }

    [Fact]
    public async Task SavedOrderAndActiveTabComeBack()
    {
        SessionStore store = new SessionStore(preferences);
        await store.SaveAsync(TabSet.Empty.OpenTab(a).OpenTab(b).Activate(a), TestContext.Current.CancellationToken);

        TabSet loaded = await new SessionStore(preferences).LoadAsync(_ => true, TestContext.Current.CancellationToken);

        Assert.Equal([a, b], loaded.Open);
        Assert.Equal(a, loaded.Active);
    }

    [Fact]
    public async Task AMissingExperimentIsOmittedAndTheActiveTabMovesOn()
    {
        await new SessionStore(preferences).SaveAsync(TabSet.Empty.OpenTab(a).OpenTab(b), TestContext.Current.CancellationToken);

        TabSet loaded = await new SessionStore(preferences).LoadAsync(id => id == a, TestContext.Current.CancellationToken);

        Assert.Equal([a], loaded.Open);
        Assert.Equal(a, loaded.Active);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"version\":99,\"open\":[\"x\"]}")]
    [InlineData("{\"version\":1,\"open\":[\"not-a-guid\",\"00000000-0000-0000-0000-000000000000\"],\"active\":5}")]
    [InlineData("[1,2]")]
    public async Task DamagedSessionDataGivesAnEmptySetNotACrash(string stored)
    {
        await preferences.SetAsync(SessionStore.KEY, stored, TestContext.Current.CancellationToken);

        TabSet loaded = await new SessionStore(preferences).LoadAsync(_ => true, TestContext.Current.CancellationToken);

        Assert.Empty(loaded.Open);
    }

    [Fact]
    public async Task TheStoredSessionHoldsOnlyIdsNoPathsAndNoSource()
    {
        await new SessionStore(preferences).SaveAsync(TabSet.Empty.OpenTab(a), TestContext.Current.CancellationToken);

        string text = preferences.Values[SessionStore.KEY];

        Assert.Contains(a.ToString(), text, StringComparison.Ordinal);
        Assert.DoesNotContain("/", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\\\\", text, StringComparison.Ordinal);
    }

    private sealed class MemoryPreferences : IPreferenceRepository
    {
        public Dictionary<string, string> Values { get; } = [];

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(Values.GetValueOrDefault(key));

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }
    }
}
