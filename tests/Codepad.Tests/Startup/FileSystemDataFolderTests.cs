using Codepad.Application.Startup;
using Codepad.Infrastructure.Startup;

namespace Codepad.Tests.Startup;

public sealed class FileSystemDataFolderTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("codepad-tests-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void PrepareWithMissingFolderCreatesItAndReturnsReady()
    {
        string path = Path.Combine(root, "missing", "codepad");

        DataFolderStatus status = new FileSystemDataFolder(path).Prepare();

        Assert.Equal(DataFolderStatus.Ready, status);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void PrepareWithExistingFolderReturnsReadyAndLeavesNoProbeFile()
    {
        DataFolderStatus status = new FileSystemDataFolder(root).Prepare();

        Assert.Equal(DataFolderStatus.Ready, status);
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
    }

    [Fact]
    public void PrepareWhenAFileHasTheFolderPathReturnsUnavailableWithoutThePath()
    {
        string path = Path.Combine(root, "codepad");
        File.WriteAllText(path, "not a folder");

        DataFolderStatus status = new FileSystemDataFolder(path).Prepare();

        Assert.False(status.IsReady);
        Assert.Equal("IOException", status.Reason);
    }
}
