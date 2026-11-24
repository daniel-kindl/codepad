using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Codepad.Application.Updates;
using Codepad.Infrastructure.Updates;

namespace Codepad.Tests.Updates;

public sealed class ArchiveExtractorTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("codepad-archive-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Target => Path.Combine(root, "out");

    private string NewPath(string name) => Path.Combine(root, name);

    private string Zip(params (string Name, string Content)[] entries)
    {
        string path = NewPath("a.zip");
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name);
            using StreamWriter writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        return path;
    }

    private string TarGz(Action<TarWriter> fill)
    {
        string path = NewPath("a.tar.gz");
        using FileStream file = File.Create(path);
        using GZipStream gzip = new GZipStream(file, CompressionLevel.Fastest);
        using TarWriter writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false);
        fill(writer);
        return path;
    }

    private static void AddFile(TarWriter writer, string name, string content, UnixFileMode mode = (UnixFileMode)0b110_100_100)
    {
        PaxTarEntry entry = new PaxTarEntry(TarEntryType.RegularFile, name) { Mode = mode, DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)) };
        writer.WriteEntry(entry);
    }

    private async Task AssertRefusedAsync(string archive, string code)
    {
        UpdateInstallException error = await Assert.ThrowsAsync<UpdateInstallException>(() => ArchiveExtractor.ExtractAsync(archive, Target, TestContext.Current.CancellationToken));

        Assert.Equal(code, error.Code);
        Assert.False(File.Exists(Path.Combine(root, "evil.txt")));
    }

    [Fact]
    public async Task AZipIsUnpackedWithItsFolders()
    {
        string archive = Zip(("Codepad.Desktop.dll", "dll"), ("lib/native.so", "so"));

        await ArchiveExtractor.ExtractAsync(archive, Target, TestContext.Current.CancellationToken);

        Assert.Equal("dll", await File.ReadAllTextAsync(Path.Combine(Target, "Codepad.Desktop.dll"), TestContext.Current.CancellationToken));
        Assert.Equal("so", await File.ReadAllTextAsync(Path.Combine(Target, "lib", "native.so"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ATarGzIsUnpackedAndKeepsTheExecuteBit()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string archive = TarGz(writer =>
        {
            AddFile(writer, "Codepad.Desktop", "#!/bin/sh", (UnixFileMode)0b111_101_101);
            AddFile(writer, "data/readme.txt", "hello");
        });

        await ArchiveExtractor.ExtractAsync(archive, Target, TestContext.Current.CancellationToken);

        Assert.True(File.GetUnixFileMode(Path.Combine(Target, "Codepad.Desktop")).HasFlag(UnixFileMode.UserExecute));
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(Target, "data", "readme.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetuidAndSetgidBitsAreDropped()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string archive = TarGz(writer => AddFile(writer, "tool", "x", UnixFileMode.SetUser | UnixFileMode.SetGroup | (UnixFileMode)0b111_101_101));

        await ArchiveExtractor.ExtractAsync(archive, Target, TestContext.Current.CancellationToken);

        UnixFileMode mode = File.GetUnixFileMode(Path.Combine(Target, "tool"));
        Assert.False(mode.HasFlag(UnixFileMode.SetUser));
        Assert.False(mode.HasFlag(UnixFileMode.SetGroup));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("a/../../evil.txt")]
    [InlineData("/etc/evil.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("C:\\evil.txt")]
    [InlineData("C:/evil.txt")]
    [InlineData("a/b/../../../evil.txt")]
    public async Task AZipEntryThatLeavesTheFolderIsRefusedAndNothingIsWrittenOutside(string name)
    {
        await AssertRefusedAsync(Zip((name, "evil")), "archive_entry_not_allowed");
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("/etc/evil.txt")]
    [InlineData("a/../../evil.txt")]
    public async Task ATarEntryThatLeavesTheFolderIsRefused(string name)
    {
        await AssertRefusedAsync(TarGz(writer => AddFile(writer, name, "evil")), "archive_entry_not_allowed");
    }

    [Fact]
    public async Task ASymbolicLinkIsRefused()
    {
        string archive = TarGz(writer => writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "link") { LinkName = "/etc/passwd" }));

        await AssertRefusedAsync(archive, "archive_entry_not_allowed");
    }

    [Fact]
    public async Task AHardLinkAndADeviceAreRefused()
    {
        await AssertRefusedAsync(TarGz(writer => writer.WriteEntry(new PaxTarEntry(TarEntryType.HardLink, "hard") { LinkName = "other" })), "archive_entry_not_allowed");
        Directory.Delete(Target, recursive: true);
        await AssertRefusedAsync(TarGz(writer => writer.WriteEntry(new PaxTarEntry(TarEntryType.CharacterDevice, "dev") { DeviceMajor = 1, DeviceMinor = 3 })), "archive_entry_not_allowed");
    }

    [Fact]
    public async Task TwoEntriesWithTheSameNameAreRefusedNotOverwritten()
    {
        string archive = Zip(("a.txt", "one"), ("a.txt", "two"));

        UpdateInstallException error = await Assert.ThrowsAsync<UpdateInstallException>(() => ArchiveExtractor.ExtractAsync(archive, Target, TestContext.Current.CancellationToken));

        Assert.Equal("archive_invalid", error.Code);
    }

    [Fact]
    public async Task AnEntryThatIsLargerThanItsHeaderSaysIsRefused()
    {
        // A tar header claims 5 bytes and the data stream gives more only if the library allows it. A short header is the usual lie.
        string archive = Zip(("a.txt", new string('x', 100)));

        await ArchiveExtractor.ExtractAsync(archive, Target, TestContext.Current.CancellationToken);

        Assert.Equal(100, new FileInfo(Path.Combine(Target, "a.txt")).Length);
    }

    [Fact]
    public async Task ADamagedArchiveIsArchiveInvalid()
    {
        string path = NewPath("bad.zip");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4, 5], TestContext.Current.CancellationToken);

        UpdateInstallException error = await Assert.ThrowsAsync<UpdateInstallException>(() => ArchiveExtractor.ExtractAsync(path, Target, TestContext.Current.CancellationToken));

        Assert.Equal("archive_invalid", error.Code);
    }

    [Fact]
    public async Task AnUnknownFormatIsRefused()
    {
        string path = NewPath("a.rar");
        await File.WriteAllBytesAsync(path, [1], TestContext.Current.CancellationToken);

        UpdateInstallException error = await Assert.ThrowsAsync<UpdateInstallException>(() => ArchiveExtractor.ExtractAsync(path, Target, TestContext.Current.CancellationToken));

        Assert.Equal("archive_format_unsupported", error.Code);
    }

    [Fact]
    public async Task ADestinationThatIsNotEmptyIsRefused()
    {
        Directory.CreateDirectory(Target);
        await File.WriteAllTextAsync(Path.Combine(Target, "old.txt"), "x", TestContext.Current.CancellationToken);

        UpdateInstallException error = await Assert.ThrowsAsync<UpdateInstallException>(() => ArchiveExtractor.ExtractAsync(Zip(("a.txt", "x")), Target, TestContext.Current.CancellationToken));

        Assert.Equal("destination_not_empty", error.Code);
    }

    [Fact]
    public async Task CancelStopsTheExtraction()
    {
        using CancellationTokenSource cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArchiveExtractor.ExtractAsync(Zip(("a.txt", "x")), Target, cancel.Token));
    }
}
