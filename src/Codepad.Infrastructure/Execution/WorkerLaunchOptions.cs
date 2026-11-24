using System.Runtime.InteropServices;
using Codepad.Contracts;

namespace Codepad.Infrastructure.Execution;

/// <summary>How to start the worker process. The options use no shell and no OS-specific path rule.</summary>
/// <param name="FileName">The program to start. It is the .NET host (<c>dotnet</c>) for a worker assembly.</param>
/// <param name="LeadingArguments">The arguments before the protocol starts. For a worker assembly, the path of the assembly.</param>
/// <param name="WorkingDirectory">The stable working directory of every run. The supervisor creates it when it is missing.</param>
public sealed record WorkerLaunchOptions(string FileName, IReadOnlyList<string> LeadingArguments, string WorkingDirectory)
{
    /// <summary>Gets the limit for the captured text of each of stdout and stderr.</summary>
    public int MaxOutputChars { get; init; } = WorkerProtocol.DefaultMaxOutputChars;

    /// <summary>Makes the options for a worker assembly. The .NET host that runs CodePad starts it.</summary>
    /// <param name="workerAssemblyPath">The full path of <c>Codepad.Worker.dll</c>.</param>
    /// <param name="workingDirectory">The stable working directory of every run.</param>
    /// <returns>The options.</returns>
    public static WorkerLaunchOptions ForWorkerAssembly(string workerAssemblyPath, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerAssemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        return new WorkerLaunchOptions(ResolveDotnetHost(), [workerAssemblyPath], workingDirectory);
    }

    /// <summary>Finds the worker assembly next to the app and makes the options for it.</summary>
    /// <param name="baseDirectory">The folder of the app. The worker assembly is in this folder.</param>
    /// <param name="workingDirectory">The stable working directory of every run.</param>
    /// <returns>The options, or null when the folder has no <c>Codepad.Worker.dll</c>.</returns>
    public static WorkerLaunchOptions? Locate(string baseDirectory, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        string assembly = Path.Combine(baseDirectory, "Codepad.Worker.dll");
        if (!File.Exists(assembly))
        {
            return null;
        }

        // A packaged app is self-contained: it has its own runtime and the machine may have no dotnet program. Then the worker starts as its own program.
        string apphost = Path.Combine(baseDirectory, OperatingSystem.IsWindows() ? "Codepad.Worker.exe" : "Codepad.Worker");
        return File.Exists(apphost) && IsSelfContained(Path.Combine(baseDirectory, "Codepad.Worker.runtimeconfig.json"))
            ? new WorkerLaunchOptions(apphost, [], workingDirectory)
            : ForWorkerAssembly(assembly, workingDirectory);
    }

    // A self-contained app lists "includedFrameworks" in its runtime configuration. A framework-dependent app lists "framework".
    private static bool IsSelfContained(string runtimeConfigPath)
    {
        try
        {
            return File.Exists(runtimeConfigPath) && File.ReadAllText(runtimeConfigPath).Contains("\"includedFrameworks\"", StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Finds the .NET host that matches the running runtime. The search order is: the current process when it is the host,
    /// the <c>DOTNET_HOST_PATH</c> variable, and the folder that holds the shared runtime. The last choice is the name
    /// <c>dotnet</c> on the search path.
    /// </summary>
    /// <returns>The path or the name of the host.</returns>
    internal static string ResolveDotnetHost()
    {
        string hostName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

        string? current = Environment.ProcessPath;
        if (current is not null && string.Equals(Path.GetFileName(current), hostName, StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        string? fromEnvironment = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(fromEnvironment))
        {
            return fromEnvironment;
        }

        // The runtime folder is <root>/shared/Microsoft.NETCore.App/<version>/. The host is in <root>.
        string runtimeFolder = RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? root = Directory.GetParent(runtimeFolder)?.Parent?.Parent?.FullName;
        if (root is not null)
        {
            string candidate = Path.Combine(root, hostName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "dotnet";
    }
}
