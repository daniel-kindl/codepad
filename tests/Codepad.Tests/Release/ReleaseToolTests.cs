using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Codepad.Application.Updates;
using Codepad.Infrastructure.Updates;
using Codepad.ReleaseTool;

namespace Codepad.Tests.Release;

public sealed class ReleaseToolTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("codepad-release-").FullName;
    private static readonly DateTimeOffset s_time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Folder(string name, params (string Path, string Content)[] files)
    {
        string folder = Path.Combine(root, name);
        foreach ((string path, string content) in files)
        {
            string full = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        return folder;
    }

    [Fact]
    public async Task TheSameFilesGiveTheSameTarGzBytesAndTheSameZipBytesAlsoWhenCreatedInAnotherOrderAndAtAnotherTime()
    {
        string first = Folder("one", ("b.txt", "bbb"), ("a/c.txt", "ccc"), ("app", "#!"));
        string second = Folder("two", ("app", "#!"), ("a/c.txt", "ccc"), ("b.txt", "bbb"));
        File.SetLastWriteTimeUtc(Path.Combine(second, "b.txt"), new DateTime(2020, 5, 5, 5, 5, 5, DateTimeKind.Utc));

        DeterministicArchive.WriteTarGz(first, Path.Combine(root, "1.tar.gz"), s_time, relative => relative == "app");
        DeterministicArchive.WriteTarGz(second, Path.Combine(root, "2.tar.gz"), s_time, relative => relative == "app");
        DeterministicArchive.WriteZip(first, Path.Combine(root, "1.zip"), s_time);
        DeterministicArchive.WriteZip(second, Path.Combine(root, "2.zip"), s_time);

        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(root, "1.tar.gz"), TestContext.Current.CancellationToken), await File.ReadAllBytesAsync(Path.Combine(root, "2.tar.gz"), TestContext.Current.CancellationToken));
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(root, "1.zip"), TestContext.Current.CancellationToken), await File.ReadAllBytesAsync(Path.Combine(root, "2.zip"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AChangedFileGivesAChangedArchive()
    {
        string folder = Folder("one", ("a.txt", "one"));
        DeterministicArchive.WriteTarGz(folder, Path.Combine(root, "1.tar.gz"), s_time, _ => false);
        await File.WriteAllTextAsync(Path.Combine(folder, "a.txt"), "two", TestContext.Current.CancellationToken);
        DeterministicArchive.WriteTarGz(folder, Path.Combine(root, "2.tar.gz"), s_time, _ => false);

        Assert.NotEqual(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(root, "1.tar.gz"), TestContext.Current.CancellationToken)), SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(root, "2.tar.gz"), TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task ATarGzHasTheArchiveRootAsTheFolderSortedNamesNoOwnerAndTheExecuteBitOnlyWhereAsked()
    {
        string folder = Folder("app", ("z.txt", "z"), ("Codepad.Desktop", "#!"), ("lib/native.so", "so"));
        string archive = Path.Combine(root, "a.tar.gz");

        DeterministicArchive.WriteTarGz(folder, archive, s_time, relative => relative == "Codepad.Desktop");

        await using FileStream file = File.OpenRead(archive);
        await using GZipStream gzip = new GZipStream(file, CompressionMode.Decompress);
        using TarReader reader = new TarReader(gzip);
        List<TarEntry> entries = [];
        while (await reader.GetNextEntryAsync(cancellationToken: TestContext.Current.CancellationToken) is TarEntry entry)
        {
            entries.Add(entry);
        }

        Assert.Equal(["Codepad.Desktop", "lib/native.so", "z.txt"], entries.Select(entry => entry.Name));
        Assert.All(entries, entry =>
        {
            Assert.Equal(0, entry.Uid);
            Assert.Equal(0, entry.Gid);
            Assert.Equal(s_time, entry.ModificationTime);
        });
        Assert.True(entries[0].Mode.HasFlag(UnixFileMode.UserExecute));
        Assert.False(entries[2].Mode.HasFlag(UnixFileMode.UserExecute));
        Assert.DoesNotContain(entries, entry => entry.Mode.HasFlag(UnixFileMode.SetUser));
    }

    [Fact]
    public async Task TheArchivesCanBeUnpackedByTheAppExtractor()
    {
        string folder = Folder("app", ("Codepad.Desktop", "#!"), ("data/a.txt", "a"));
        DeterministicArchive.WriteTarGz(folder, Path.Combine(root, "a.tar.gz"), s_time, relative => relative == "Codepad.Desktop");
        DeterministicArchive.WriteZip(folder, Path.Combine(root, "a.zip"), s_time);

        await ArchiveExtractor.ExtractAsync(Path.Combine(root, "a.tar.gz"), Path.Combine(root, "out1"), TestContext.Current.CancellationToken);
        await ArchiveExtractor.ExtractAsync(Path.Combine(root, "a.zip"), Path.Combine(root, "out2"), TestContext.Current.CancellationToken);

        Assert.Equal("a", await File.ReadAllTextAsync(Path.Combine(root, "out1", "data", "a.txt"), TestContext.Current.CancellationToken));
        Assert.Equal("a", await File.ReadAllTextAsync(Path.Combine(root, "out2", "data", "a.txt"), TestContext.Current.CancellationToken));
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(Path.Combine(root, "out1", "Codepad.Desktop")).HasFlag(UnixFileMode.UserExecute));
        }
    }

    private (string Linux, string Windows) Archives(string version)
    {
        string folder = Folder("pkg-" + version, ("Codepad.Desktop", "app " + version));
        string linux = Path.Combine(root, Packager.ArchiveName(version, "linux-x64"));
        string windows = Path.Combine(root, Packager.ArchiveName(version, "win-x64"));
        DeterministicArchive.WriteTarGz(folder, linux, s_time, relative => relative == "Codepad.Desktop");
        DeterministicArchive.WriteZip(folder, windows, s_time);
        return (linux, windows);
    }

    [Fact]
    public void TheArchiveNamesFollowTheContract()
    {
        Assert.Equal("codepad-0.2.0-linux-x64.tar.gz", Packager.ArchiveName("0.2.0", "linux-x64"));
        Assert.Equal("codepad-0.2.0-win-x64.zip", Packager.ArchiveName("0.2.0", "win-x64"));
    }

    [Fact]
    public void TheManifestParsesWithTheRulesOfTheAppAndHasTheRealSizesAndDigests()
    {
        (string linux, string windows) = Archives("0.2.0");

        byte[] manifestBytes = ReleaseManifestBuilder.Build("0.2.0", "Notes.", s_time, [new ReleaseArchive("win-x64", windows), new ReleaseArchive("linux-x64", linux)]);

        UpdateManifest manifest = UpdateManifestParser.Parse(manifestBytes);
        Assert.Equal("0.2.0", manifest.Version.ToString());
        UpdateAsset asset = manifest.Assets.Single(item => item.Platform == "linux-x64");
        Assert.Equal(new FileInfo(linux).Length, asset.Size);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(linux))), asset.Sha256);
        Assert.Equal("https://github.com/daniel-kindl/codepad/releases/download/v0.2.0/codepad-0.2.0-linux-x64.tar.gz", asset.Url.ToString());
    }

    [Fact]
    public void TheManifestIsTheSameForTheSameInputSoASecondBuildCanBeComparedByteForByte()
    {
        (string linux, string windows) = Archives("0.2.0");

        byte[] first = ReleaseManifestBuilder.Build("0.2.0", "Notes.", s_time, [new ReleaseArchive("linux-x64", linux), new ReleaseArchive("win-x64", windows)]);
        byte[] second = ReleaseManifestBuilder.Build("0.2.0", "Notes.", s_time, [new ReleaseArchive("win-x64", windows), new ReleaseArchive("linux-x64", linux)]);

        Assert.Equal(first, second);
    }

    [Fact]
    public void TheSignatureOfTheToolIsValidForTheAppVerifierAndFailsAfterAnyChange()
    {
        (string privatePem, string publicPem) = ReleaseSigning.NewKeyPair();
        byte[] manifest = Encoding.UTF8.GetBytes("{\"version\":\"0.2.0\"}\n");

        byte[] signature = ReleaseSigning.Sign(privatePem, manifest);

        EcdsaManifestVerifier verifier = new EcdsaManifestVerifier(publicPem);
        Assert.Equal(64, signature.Length);
        Assert.True(verifier.Verify(manifest, signature));
        manifest[3] ^= 1;
        Assert.False(verifier.Verify(manifest, signature));
    }

    [Fact]
    public void ASignatureNeedsAnEcdsaP256PrivateKey()
    {
        using RSA rsa = RSA.Create(2048);
        using ECDsa p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        Assert.Throws<InvalidOperationException>(() => ReleaseSigning.Sign(rsa.ExportPkcs8PrivateKeyPem(), [1]));
        Assert.Throws<InvalidOperationException>(() => ReleaseSigning.Sign(p384.ExportPkcs8PrivateKeyPem(), [1]));
        Assert.Throws<InvalidOperationException>(() => ReleaseSigning.Sign("not a key", [1]));
    }

    [Fact]
    public void TheCommandsMakeKeysManifestSignatureAndVerifyEndToEndAndRefuseToReplaceAKey()
    {
        (string linux, string windows) = Archives("0.2.0");
        string keys = Path.Combine(root, "keys");
        string notes = Path.Combine(root, "notes.txt");
        string manifest = Path.Combine(root, "manifest.json");
        File.WriteAllText(notes, "What is new.");

        Assert.Equal(0, Program.KeyGen(keys));
        Assert.Equal(1, Program.KeyGen(keys));
        Assert.Equal(0, Program.Manifest(Options(("--version", "0.2.0"), ("--notes", notes), ("--linux", linux), ("--windows", windows), ("--out", manifest))));
        Assert.Equal(0, Program.Sign(Path.Combine(keys, "release-private.pem"), manifest));
        Assert.Equal(0, Program.Verify(Path.Combine(keys, "release-public.pem"), manifest, [linux, windows]));

        File.AppendAllText(linux, "tampered");
        Assert.Equal(1, Program.Verify(Path.Combine(keys, "release-public.pem"), manifest, [linux]));
        (string otherPrivate, string otherPublic) = ReleaseSigning.NewKeyPair();
        File.WriteAllText(Path.Combine(root, "other.pem"), otherPublic);
        Assert.Equal(1, Program.Verify(Path.Combine(root, "other.pem"), manifest, []));
        Assert.NotEmpty(otherPrivate);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(keys, "release-private.pem")));
        }
    }

    [Fact]
    public void ThePackageCommandRefusesAPreReleaseVersionAndAnUnknownPlatform()
    {
        Assert.Equal(1, Program.Package(Options(("--platform", "linux-x64"), ("--version", "0.2.0-rc.1"), ("--out", root))));
        Assert.Equal(1, Program.Package(Options(("--platform", "macos-arm64"), ("--version", "0.2.0"), ("--out", root))));
    }

    [Fact]
    public void ChecksumsAreInTheFormatOfSha256sum()
    {
        string file = Path.Combine(root, "a.bin");
        File.WriteAllBytes(file, [1, 2, 3]);

        string text = ReleaseManifestBuilder.Checksums([file]);

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData([1, 2, 3])) + "  a.bin\n", text);
    }

    private static Dictionary<string, List<string>> Options(params (string Name, string Value)[] pairs)
    {
        Dictionary<string, List<string>> options = [];
        foreach ((string name, string value) in pairs)
        {
            options[name] = [value];
        }

        return options;
    }
}
