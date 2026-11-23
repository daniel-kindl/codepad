using Codepad.Application.Startup;

namespace Codepad.Infrastructure.Startup;

/// <summary>A data folder on the local file system.</summary>
/// <param name="path">The full path of the folder.</param>
public sealed class FileSystemDataFolder(string path) : IDataFolder
{
    /// <inheritdoc />
    public DataFolderStatus Prepare()
    {
        try
        {
            Directory.CreateDirectory(path);

            // A folder can exist and still refuse writes. Write and delete a probe file to check.
            string probe = Path.Combine(path, $".write-probe-{Guid.NewGuid():N}");
            using (File.Create(probe, bufferSize: 1, FileOptions.DeleteOnClose))
            {
            }

            return DataFolderStatus.Ready;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The exception type is the reason. The message can hold the path, which must not reach a log.
            return DataFolderStatus.Unavailable(exception.GetType().Name);
        }
    }
}
