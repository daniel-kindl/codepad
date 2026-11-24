using System.Security.Cryptography;
using Codepad.Application.Updates;

namespace Codepad.Infrastructure.Updates;

/// <summary>
/// Downloads an archive into a staging folder outside the installation. It follows only a few redirects to official hosts. It
/// stops when the body is longer than the signed size. At the end it checks the size and the SHA-256 digest against the signed
/// manifest, and only then gives the file its final name. A failed or canceled download leaves no file.
/// </summary>
/// <param name="client">An HTTP client from <see cref="GuardedHttp.CreateClient"/>.</param>
/// <param name="stagingRoot">The folder for staged updates. It is never inside the installation.</param>
public sealed class HttpUpdateDownloader(HttpClient client, string stagingRoot) : IUpdateDownloader
{
    private const int BUFFER_BYTES = 64 * 1024;

    private readonly GuardedHttp http = new GuardedHttp(client);

    /// <inheritdoc />
    public async Task<StagedUpdate> DownloadAsync(UpdateOffer offer, IProgress<UpdateProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        UpdateAsset asset = offer.Asset;
        string folder = Path.Combine(stagingRoot, offer.Version.ToString());
        string finalPath = Path.Combine(folder, asset.Name);
        string partialPath = finalPath + ".partial";
        try
        {
            Directory.CreateDirectory(folder);
            DeleteIfExists(partialPath);
            DeleteIfExists(finalPath);

            using HttpResponseMessage response = await http.GetAsync(asset.Url, "application/octet-stream", cancellationToken).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is long declared && declared != asset.Size)
            {
                throw new UpdateDownloadException("size_mismatch");
            }

            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            await using (Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (FileStream file = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BUFFER_BYTES, useAsync: true))
            {
                byte[] buffer = new byte[BUFFER_BYTES];
                int read;
                while ((read = await GuardedHttp.ReadWithStallLimitAsync(body, buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    received += read;
                    if (received > asset.Size)
                    {
                        // More bytes than the signed manifest says. Stop at once. Do not fill the disk.
                        throw new UpdateDownloadException("size_mismatch");
                    }

                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    progress?.Report(new UpdateProgress(received, asset.Size));
                }
            }

            if (received != asset.Size)
            {
                throw new UpdateDownloadException("size_mismatch");
            }

            string actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(actual), System.Text.Encoding.ASCII.GetBytes(asset.Sha256)))
            {
                throw new UpdateDownloadException("digest_mismatch");
            }

            File.Move(partialPath, finalPath);
            return new StagedUpdate(offer, finalPath);
        }
        catch (GuardedHttpException exception)
        {
            DeleteQuietly(partialPath);
            throw new UpdateDownloadException(exception.Code, exception);
        }
        catch (HttpRequestException exception)
        {
            DeleteQuietly(partialPath);
            throw new UpdateDownloadException("network", exception);
        }
        catch (IOException exception)
        {
            DeleteQuietly(partialPath);
            throw new UpdateDownloadException("io_error", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            DeleteQuietly(partialPath);
            throw new UpdateDownloadException("io_error", exception);
        }
        catch
        {
            // A cancel, a size or digest failure, or a bug. The partial file never stays.
            DeleteQuietly(partialPath);
            throw;
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            DeleteIfExists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The folder is a staging folder. A file that stays is cleaned at the next update.
        }
    }
}
