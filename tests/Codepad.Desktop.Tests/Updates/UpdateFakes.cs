using System.Text;
using System.Text.Json.Nodes;
using Codepad.Application.Updates;

namespace Codepad.Desktop.Tests.Updates;

internal static class UpdateSamples
{
    public static SemanticVersion Version(string text) => SemanticVersion.TryParse(text, out SemanticVersion? version) ? version! : throw new InvalidOperationException(text);

    public static byte[] Manifest(string version, string notes = "Faster start.\nA new command.", long size = 12_900_000)
    {
        JsonObject json = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["protocol"] = 1,
            ["version"] = version,
            ["releaseUrl"] = "https://github.com/daniel-kindl/codepad/releases/tag/v" + version,
            ["releaseNotes"] = notes,
            ["assets"] = new JsonArray
            {
                Asset("linux-x64", $"codepad-{version}-linux-x64.tar.gz", size, version),
                Asset("win-x64", $"codepad-{version}-win-x64.zip", size, version),
            },
        };
        return Encoding.UTF8.GetBytes(json.ToJsonString());
    }

    private static JsonObject Asset(string platform, string name, long size, string version) => new JsonObject
    {
        ["platform"] = platform,
        ["name"] = name,
        ["size"] = size,
        ["sha256"] = new string('a', 64),
        ["url"] = $"https://github.com/daniel-kindl/codepad/releases/download/v{version}/{name}",
    };
}

internal sealed class FakeSource : IUpdateSource
{
    public SignedManifest? Next { get; set; }

    public Exception? Failure { get; set; }

    public Task<SignedManifest?> FetchLatestAsync(CancellationToken cancellationToken) => Failure is not null ? Task.FromException<SignedManifest?>(Failure) : Task.FromResult(Next);
}

internal sealed class FakeVerifier : IManifestVerifier
{
    public bool Valid { get; set; } = true;

    public bool HasKey => true;

    public bool Verify(ReadOnlySpan<byte> manifestBytes, ReadOnlySpan<byte> signature) => Valid;
}

internal sealed class FakeDownloader : IUpdateDownloader
{
    public UpdateDownloadException? Failure { get; set; }

    public TaskCompletionSource? Hold { get; set; }

    public IReadOnlyList<UpdateProgress> Steps { get; set; } = [];

    public async Task<StagedUpdate> DownloadAsync(UpdateOffer offer, IProgress<UpdateProgress>? progress, CancellationToken cancellationToken)
    {
        foreach (UpdateProgress step in Steps)
        {
            progress?.Report(step);
        }

        if (Hold is not null)
        {
            await Hold.Task.WaitAsync(cancellationToken);
        }

        return Failure is not null ? throw Failure : new StagedUpdate(offer, "/staging/archive");
    }
}

internal sealed class FakeInstaller : IUpdateInstaller
{
    public string? Unsupported { get; set; }

    public UpdateInstallException? Failure { get; set; }

    public int Calls { get; private set; }

    public string? CheckSupported() => Unsupported;

    public Task HandOffAsync(StagedUpdate staged, CancellationToken cancellationToken)
    {
        Calls++;
        return Failure is not null ? Task.FromException(Failure) : Task.CompletedTask;
    }
}

internal sealed class MemoryUpdateStore : IUpdateStore
{
    private DateTimeOffset? last;

    public bool PromptShown { get; set; }

    public Task<DateTimeOffset?> GetLastCheckAsync() => Task.FromResult(last);

    public Task SetLastCheckAsync(DateTimeOffset time)
    {
        last = time;
        return Task.CompletedTask;
    }

    public Task<bool> GetPromptShownAsync() => Task.FromResult(PromptShown);

    public Task SetPromptShownAsync()
    {
        PromptShown = true;
        return Task.CompletedTask;
    }
}
