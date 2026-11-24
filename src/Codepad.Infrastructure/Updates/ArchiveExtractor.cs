using System.Formats.Tar;
using System.IO.Compression;
using Codepad.Application.Updates;

namespace Codepad.Infrastructure.Updates;

/// <summary>
/// Unpacks a staged update (zip or tar.gz) into a new folder. The archive is untrusted until each check passes: an entry
/// may not leave the folder (no absolute path, no drive, no <c>..</c>), links and devices are refused, and the number and the total
/// size of the entries are bounded. The file mode of a tar entry is kept, but never a setuid or setgid bit.
/// </summary>
public static class ArchiveExtractor
{
    /// <summary>The largest number of entries.</summary>
    public static readonly int MaxEntries = 20_000;

    /// <summary>The largest total size of the unpacked files in bytes (3 GiB).</summary>
    public static readonly long MaxTotalBytes = 3L * 1024 * 1024 * 1024;

    /// <summary>Unpacks an archive. The format comes from the file name.</summary>
    /// <param name="archivePath">The archive.</param>
    /// <param name="destination">The folder to fill. It must not exist or must be empty.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that ends when all files are written.</returns>
    /// <exception cref="UpdateInstallException">The archive is not valid or breaks a rule.</exception>
    public static async Task ExtractAsync(string archivePath, string destination, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            throw new UpdateInstallException("destination_not_empty");
        }

        string root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        try
        {
            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await ExtractZipAsync(archivePath, root, cancellationToken).ConfigureAwait(false);
            }
            else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                await ExtractTarGzAsync(archivePath, root, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new UpdateInstallException("archive_format_unsupported");
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or FormatException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new UpdateInstallException("archive_invalid", exception);
        }
    }

    private static async Task ExtractZipAsync(string archivePath, string root, CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaxEntries)
        {
            throw new UpdateInstallException("archive_too_many_entries");
        }

        long total = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = ResolveTarget(root, entry.FullName);
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            total += entry.Length;
            if (total > MaxTotalBytes)
            {
                throw new UpdateInstallException("archive_too_large");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using Stream input = entry.Open();
            await using FileStream output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await CopyBoundedAsync(input, output, entry.Length, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ExtractTarGzAsync(string archivePath, string root, CancellationToken cancellationToken)
    {
        await using FileStream file = File.OpenRead(archivePath);
        await using GZipStream gzip = new GZipStream(file, CompressionMode.Decompress);
        using TarReader reader = new TarReader(gzip);
        int count = 0;
        long total = 0;
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false) is TarEntry entry)
        {
            if (++count > MaxEntries)
            {
                throw new UpdateInstallException("archive_too_many_entries");
            }

            string target = ResolveTarget(root, entry.Name);
            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(target);
                    break;
                case TarEntryType.RegularFile or TarEntryType.V7RegularFile:
                    total += entry.Length;
                    if (total > MaxTotalBytes)
                    {
                        throw new UpdateInstallException("archive_too_large");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using (FileStream output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        if (entry.DataStream is Stream data)
                        {
                            await CopyBoundedAsync(data, output, entry.Length, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    if (!OperatingSystem.IsWindows())
                    {
                        // Keep the execute bit. Drop setuid, setgid and sticky bits: an update never needs them.
                        File.SetUnixFileMode(target, (UnixFileMode)((int)entry.Mode & 0b111_111_111));
                    }

                    break;
                default:
                    // A link, a device or a pipe could point out of the folder. An app update has none.
                    throw new UpdateInstallException("archive_entry_not_allowed");
            }
        }
    }

    private static string ResolveTarget(string root, string entryName)
    {
        if (string.IsNullOrEmpty(entryName) || entryName.Contains('\0', StringComparison.Ordinal) || entryName.Contains(':', StringComparison.Ordinal))
        {
            throw new UpdateInstallException("archive_entry_not_allowed");
        }

        string normalized = entryName.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Split('/').Any(segment => segment == ".."))
        {
            throw new UpdateInstallException("archive_entry_not_allowed");
        }

        string combined = Path.GetFullPath(Path.Combine(root, normalized));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(prefix, StringComparison.Ordinal) && combined != root)
        {
            throw new UpdateInstallException("archive_entry_not_allowed");
        }

        return combined;
    }

    // The size in the header can lie. Count the bytes that really come out.
    private static async Task CopyBoundedAsync(Stream input, Stream output, long declared, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81_920];
        long written = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            written += read;
            if (written > declared || written > MaxTotalBytes)
            {
                throw new UpdateInstallException("archive_invalid");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }
}
