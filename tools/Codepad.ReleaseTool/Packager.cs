using System.Diagnostics;

namespace Codepad.ReleaseTool;

/// <summary>
/// Builds the full-install package of one platform: the desktop app, the command line, the worker, and the update helper, all self-contained so
/// that the machine needs no .NET. Run <c>dotnet restore</c> first: the publish does not restore, so the lock files stay in force. The layout is the contract of docs/updates.md: the archive root is the app folder, and the update helper is one
/// self-contained file in <c>updater/</c>.
/// </summary>
internal static class Packager
{
    /// <summary>Gets the file name of the archive of a platform.</summary>
    /// <param name="version">The version.</param>
    /// <param name="platform">The platform ID.</param>
    /// <returns>The file name.</returns>
    public static string ArchiveName(string version, string platform) => $"codepad-{version}-{platform}" + (platform.StartsWith("win", StringComparison.Ordinal) ? ".zip" : ".tar.gz");

    /// <summary>Publishes and packs.</summary>
    /// <param name="repositoryRoot">The root of the repository.</param>
    /// <param name="platform">The platform ID, which is also the runtime ID.</param>
    /// <param name="version">The version.</param>
    /// <param name="outputFolder">The folder for the archive.</param>
    /// <param name="timestamp">The time for the entries of the archive.</param>
    /// <returns>The path of the archive.</returns>
    public static string Package(string repositoryRoot, string platform, string version, string outputFolder, DateTimeOffset timestamp)
    {
        string work = Path.Combine(Path.GetTempPath(), "codepad-package-" + Guid.NewGuid().ToString("N"));
        string app = Path.Combine(work, "app");
        string helper = Path.Combine(work, "helper");
        try
        {
            Directory.CreateDirectory(app);
            string common = $"-c Release -r {platform} --self-contained --no-restore -p:Version={version} -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false";
            Dotnet(repositoryRoot, $"publish src/Codepad.Desktop {common} -o \"{app}\"");
            Dotnet(repositoryRoot, $"publish src/Codepad.Cli {common} -o \"{app}\"");
            Dotnet(repositoryRoot, $"publish src/Codepad.Updater {common} -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o \"{helper}\"");

            // The helper next to the app has no runtime beside it. The single-file helper has its own, so it can run from a copy in another folder.
            foreach (string file in Directory.GetFiles(app, "Codepad.Updater*"))
            {
                File.Delete(file);
            }

            Directory.CreateDirectory(Path.Combine(app, "updater"));
            string helperName = "Codepad.Updater" + (platform.StartsWith("win", StringComparison.Ordinal) ? ".exe" : string.Empty);
            File.Copy(Path.Combine(helper, helperName), Path.Combine(app, "updater", helperName));

            foreach (string file in Directory.GetFiles(app, "*.xml").Where(path => !path.EndsWith(".deps.json", StringComparison.Ordinal)))
            {
                File.Delete(file);
            }

            CopyDocs(repositoryRoot, Path.Combine(app, "docs"));
            Directory.CreateDirectory(outputFolder);
            string archive = Path.Combine(outputFolder, ArchiveName(version, platform));
            if (File.Exists(archive))
            {
                File.Delete(archive);
            }

            if (platform.StartsWith("win", StringComparison.Ordinal))
            {
                DeterministicArchive.WriteZip(app, archive, timestamp);
            }
            else
            {
                // The programs of the package are the files without an extension that have the execute bit after the publish, and the shared libraries.
                DeterministicArchive.WriteTarGz(app, archive, timestamp, relative => IsProgram(Path.Combine(app, relative.Replace('/', Path.DirectorySeparatorChar))));
            }

            return archive;
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A temporary folder. The system cleans it later. A failure here must not hide the real error.
            }
        }
    }

    internal static bool IsProgram(string path) => !OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;

    private static void CopyDocs(string root, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string name in new[] { "README.md", "CHANGELOG.md", "SECURITY.md" })
        {
            string source = Path.Combine(root, name);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(target, name));
            }
        }

        string docs = Path.Combine(root, "docs");
        if (Directory.Exists(docs))
        {
            foreach (string file in Directory.GetFiles(docs, "*.md"))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            }
        }
    }

    private static void Dotnet(string workingFolder, string arguments)
    {
        ProcessStartInfo info = new ProcessStartInfo("dotnet", arguments) { WorkingDirectory = workingFolder, UseShellExecute = false };
        using Process process = Process.Start(info) ?? throw new InvalidOperationException("dotnet did not start.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet {arguments.Split(' ')[0]} failed with exit code {process.ExitCode}.");
        }
    }
}
