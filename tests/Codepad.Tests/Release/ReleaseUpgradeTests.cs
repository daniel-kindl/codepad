using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Codepad.Application.Updates;
using Codepad.Infrastructure.Updates;
using Codepad.ReleaseTool;
using Codepad.Tests.Updates;

namespace Codepad.Tests.Release;

/// <summary>
/// The clean-install upgrade of the release contract: archives and a signed manifest made by the release tool are installed once, then the
/// app finds the next version through its own update chain (source, signature, download, installer, helper) and swaps to it. User data
/// stays. It runs on Linux and Windows with small scripts as the apps, so it tests the chain and not the size of a real package.
/// </summary>
public sealed class ReleaseUpgradeTests : IDisposable
{
    private static readonly DateTimeOffset s_time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string root = Directory.CreateTempSubdirectory("codepad-upgrade-").FullName;
    private readonly FakeHttp http = new FakeHttp();

    public void Dispose()
    {
        http.Dispose();
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // A helper process can hold a folder for a moment. The folder is temporary.
        }
    }

    private static string Executable => OperatingSystem.IsWindows() ? "Codepad.Desktop.cmd" : "Codepad.Desktop";

    private static string Platform => OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";

    private string WriteApp(string folder, string label)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Executable);
        string marker = Path.Combine(root, "started-" + label + ".txt");
        string health = "CODEPAD_UPDATE_HEALTH_FILE";
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(path, $"@echo off\r\necho started> \"{marker}\"\r\nif defined {health} echo ok> \"%{health}%\"\r\nping -n 6 127.0.0.1 > NUL\r\n");
        }
        else
        {
            File.WriteAllText(path, $"#!/bin/sh\necho started > '{marker}'\nif [ -n \"${health}\" ]; then echo ok > \"${health}\"; fi\nsleep 5\n");
            File.SetUnixFileMode(path, (UnixFileMode)0b111_101_101);
        }

        File.WriteAllText(Path.Combine(folder, "codepad"), "cli " + label);
        File.WriteAllText(Path.Combine(folder, "version.txt"), label);
        return folder;
    }

    private string Archive(string version, string platform)
    {
        string folder = WriteApp(Path.Combine(root, $"payload-{version}-{platform}"), version);
        string archive = Path.Combine(root, Packager.ArchiveName(version, platform));
        if (platform.StartsWith("win", StringComparison.Ordinal))
        {
            DeterministicArchive.WriteZip(folder, archive, s_time);
        }
        else
        {
            DeterministicArchive.WriteTarGz(folder, archive, s_time, relative => relative == Executable);
        }

        return archive;
    }

    private static Process StartStandIn()
    {
        ProcessStartInfo info = OperatingSystem.IsWindows() ? new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 > NUL") : new ProcessStartInfo("sleep", "2");
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        return Process.Start(info)!;
    }

    [Fact]
    public async Task AnInstalledVersionFindsTheSignedNextReleaseDownloadsItAndUpgradesWithTheDataKept()
    {
        // Release 0.1.0 and 0.2.0, both platforms, made by the release tool.
        string linux1 = Archive("0.1.0", "linux-x64");
        string windows1 = Archive("0.1.0", "win-x64");
        string linux2 = Archive("0.2.0", "linux-x64");
        string windows2 = Archive("0.2.0", "win-x64");

        // A clean install of 0.1.0 into a fresh per-user folder, the way the package scripts do it. The data is outside.
        string installDir = Path.Combine(root, "home", "CodePad", "app");
        string data = Path.Combine(root, "home", "data");
        Directory.CreateDirectory(data);
        string database = Path.Combine(data, "codepad.db");
        await File.WriteAllTextAsync(database, "my experiments", TestContext.Current.CancellationToken);
        await ArchiveExtractor.ExtractAsync(Platform == "win-x64" ? windows1 : linux1, installDir, TestContext.Current.CancellationToken);
        Assert.Equal("0.1.0", await File.ReadAllTextAsync(Path.Combine(installDir, "version.txt"), TestContext.Current.CancellationToken));

        // The owner builds the manifest for 0.2.0 and signs it with the owner's key. The app has the public key.
        (string privatePem, string publicPem) = ReleaseSigning.NewKeyPair();
        byte[] manifest = ReleaseManifestBuilder.Build("0.2.0", "The second release.", s_time, [new ReleaseArchive("linux-x64", linux2), new ReleaseArchive("win-x64", windows2)]);
        byte[] signature = ReleaseSigning.Sign(privatePem, manifest);
        string baseUrl = "https://github.com/daniel-kindl/codepad/releases/download/v0.2.0/";
        JsonObject release = new JsonObject
        {
            ["tag_name"] = "v0.2.0",
            ["draft"] = false,
            ["prerelease"] = false,
            ["assets"] = new JsonArray
            {
                new JsonObject { ["name"] = "manifest.json", ["browser_download_url"] = baseUrl + "manifest.json" },
                new JsonObject { ["name"] = "manifest.json.sig", ["browser_download_url"] = baseUrl + "manifest.json.sig" },
            },
        };
        http.Add("https://api.github.com/repos/daniel-kindl/codepad/releases/latest", release.ToJsonString());
        http.Add(baseUrl + "manifest.json", HttpStatusCode.OK, manifest);
        http.Add(baseUrl + "manifest.json.sig", HttpStatusCode.OK, signature);
        string archiveName = Packager.ArchiveName("0.2.0", Platform);
        http.Add(baseUrl + archiveName, HttpStatusCode.OK, await File.ReadAllBytesAsync(Path.Combine(root, archiveName), TestContext.Current.CancellationToken));

        using Process standIn = StartStandIn();
        string updates = Path.Combine(data, "updates");
        FileUpdateInstaller installer = new FileUpdateInstaller(new FileUpdateInstallerOptions(
            installDir,
            updates,
            Executable,
            AppContext.BaseDirectory,
            "0.1.0",
            database,
            backup => { File.Copy(database, backup, overwrite: true); return Task.CompletedTask; },
            standIn.Id,
            ["--post-update"],
            TimeSpan.FromSeconds(10)));
        HttpClient client = new HttpClient(http);
        UpdateService service = new UpdateService(
            new GitHubUpdateSource(client),
            new EcdsaManifestVerifier(publicPem),
            new HttpUpdateDownloader(client, Path.Combine(updates, "staging")),
            installer,
            new NoStore(),
            TimeProvider.System,
            SemanticVersion.TryParse("0.1.0", out SemanticVersion? installed) ? installed! : throw new InvalidOperationException(),
            Platform);

        await service.CheckAsync(true, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Available, service.Status.State);
        await service.DownloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.ReadyToInstall, service.Status.State);
        await service.InstallAsync(TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Installing, service.Status.State);

        string result = await WaitForResultAsync(updates);

        Assert.Contains("\"updated\"", result, StringComparison.Ordinal);
        Assert.Equal("0.2.0", await File.ReadAllTextAsync(Path.Combine(installDir, "version.txt"), TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(root, "started-0.2.0.txt")), "The new version did not start.");
        Assert.Equal("my experiments", await File.ReadAllTextAsync(database, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(installDir + ".old"));
    }

    [Fact]
    public async Task ATamperedArchiveOnTheServerIsRejectedAndTheInstalledVersionKeepsRunning()
    {
        string linux2 = Archive("0.2.0", "linux-x64");
        string windows2 = Archive("0.2.0", "win-x64");
        string installDir = Path.Combine(root, "home", "CodePad", "app");
        WriteApp(installDir, "0.1.0");
        (string privatePem, string publicPem) = ReleaseSigning.NewKeyPair();
        byte[] manifest = ReleaseManifestBuilder.Build("0.2.0", "x", s_time, [new ReleaseArchive("linux-x64", linux2), new ReleaseArchive("win-x64", windows2)]);
        string baseUrl = "https://github.com/daniel-kindl/codepad/releases/download/v0.2.0/";
        http.Add("https://api.github.com/repos/daniel-kindl/codepad/releases/latest", new JsonObject
        {
            ["tag_name"] = "v0.2.0",
            ["draft"] = false,
            ["prerelease"] = false,
            ["assets"] = new JsonArray
            {
                new JsonObject { ["name"] = "manifest.json", ["browser_download_url"] = baseUrl + "manifest.json" },
                new JsonObject { ["name"] = "manifest.json.sig", ["browser_download_url"] = baseUrl + "manifest.json.sig" },
            },
        }.ToJsonString());
        http.Add(baseUrl + "manifest.json", HttpStatusCode.OK, manifest);
        http.Add(baseUrl + "manifest.json.sig", HttpStatusCode.OK, ReleaseSigning.Sign(privatePem, manifest));
        string archiveName = Packager.ArchiveName("0.2.0", Platform);
        byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(root, archiveName), TestContext.Current.CancellationToken);
        bytes[bytes.Length / 2] ^= 0xFF;
        http.Add(baseUrl + archiveName, HttpStatusCode.OK, bytes);
        HttpClient client = new HttpClient(http);
        UpdateService service = new UpdateService(new GitHubUpdateSource(client), new EcdsaManifestVerifier(publicPem), new HttpUpdateDownloader(client, Path.Combine(root, "staging")), new NoInstaller(), new NoStore(), TimeProvider.System, SemanticVersion.TryParse("0.1.0", out SemanticVersion? version) ? version! : throw new InvalidOperationException(), Platform);

        await service.CheckAsync(true, TestContext.Current.CancellationToken);
        await service.DownloadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.Failed, service.Status.State);
        Assert.Equal("digest_mismatch", service.Status.ErrorCode);
        Assert.Equal("0.1.0", await File.ReadAllTextAsync(Path.Combine(installDir, "version.txt"), TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "staging"), "*", SearchOption.AllDirectories));
    }

    private static async Task<string> WaitForResultAsync(string updates)
    {
        string file = Path.Combine(updates, "result.json");
        DateTime deadline = DateTime.UtcNow.AddSeconds(60);
        while (!File.Exists(file) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(file), "The helper wrote no result.");
        await Task.Delay(300, TestContext.Current.CancellationToken);
        return await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
    }

    private sealed class NoInstaller : IUpdateInstaller
    {
        public string? CheckSupported() => null;

        public Task HandOffAsync(StagedUpdate staged, CancellationToken cancellationToken) => throw new InvalidOperationException("Nothing may be installed.");
    }

    private sealed class NoStore : IUpdateStore
    {
        public Task<DateTimeOffset?> GetLastCheckAsync() => Task.FromResult<DateTimeOffset?>(null);

        public Task SetLastCheckAsync(DateTimeOffset time) => Task.CompletedTask;

        public Task<bool> GetPromptShownAsync() => Task.FromResult(true);

        public Task SetPromptShownAsync() => Task.CompletedTask;
    }
}
