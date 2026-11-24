using System.Formats.Tar;
using System.IO.Compression;

namespace Codepad.ReleaseTool;

/// <summary>
/// Writes an archive of a folder so that the same files always give the same bytes: sorted names, one fixed time, no owner, and fixed file modes.
/// The archive root is the folder itself, as the updater expects. A tar keeps the execute bit of a file. A zip has no modes.
/// </summary>
internal static class DeterministicArchive
{
    private const UnixFileMode FILE = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
    private const UnixFileMode EXECUTABLE = FILE | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    /// <summary>Writes a tar.gz.</summary>
    /// <param name="sourceFolder">The folder.</param>
    /// <param name="archivePath">The new file.</param>
    /// <param name="timestamp">The time for every entry.</param>
    /// <param name="isExecutable">Tells which files get the execute bit. The path is relative with forward slashes.</param>
    public static void WriteTarGz(string sourceFolder, string archivePath, DateTimeOffset timestamp, Func<string, bool> isExecutable)
    {
        using FileStream file = File.Create(archivePath);

        // The gzip header can hold a time and a name. The .NET stream writes neither.
        using GZipStream gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        using TarWriter writer = new TarWriter(gzip, TarEntryFormat.Ustar, leaveOpen: false);
        foreach (string relative in Files(sourceFolder))
        {
            // The ustar format has no extended attributes, so no time of the file system gets into the bytes.
            UstarTarEntry entry = new UstarTarEntry(TarEntryType.RegularFile, relative)
            {
                Mode = isExecutable(relative) ? EXECUTABLE : FILE,
                ModificationTime = timestamp,
                Uid = 0,
                Gid = 0,
                UserName = string.Empty,
                GroupName = string.Empty,
            };

            using FileStream data = File.OpenRead(Path.Combine(sourceFolder, relative.Replace('/', Path.DirectorySeparatorChar)));
            entry.DataStream = data;
            writer.WriteEntry(entry);
        }
    }

    /// <summary>Writes a zip.</summary>
    /// <param name="sourceFolder">The folder.</param>
    /// <param name="archivePath">The new file.</param>
    /// <param name="timestamp">The time for every entry. A zip cannot hold a year before 1980.</param>
    public static void WriteZip(string sourceFolder, string archivePath, DateTimeOffset timestamp)
    {
        using FileStream file = File.Create(archivePath);
        using ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (string relative in Files(sourceFolder))
        {
            ZipArchiveEntry entry = archive.CreateEntry(relative, CompressionLevel.SmallestSize);
            entry.LastWriteTime = timestamp;
            using Stream output = entry.Open();
            using FileStream input = File.OpenRead(Path.Combine(sourceFolder, relative.Replace('/', Path.DirectorySeparatorChar)));
            input.CopyTo(output);
        }
    }

    private static IEnumerable<string> Files(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal);
}
