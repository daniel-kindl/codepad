using System.Runtime.InteropServices;

namespace Codepad.Application.Updates;

/// <summary>The platform IDs that a release has an archive for.</summary>
public static class UpdatePlatform
{
    /// <summary>Gets the ID of this machine, for example <c>linux-x64</c>, or an ID that no release has when the machine is not supported.</summary>
    /// <returns>The platform ID.</returns>
    public static string Current() => Describe(OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), RuntimeInformation.ProcessArchitecture);

    /// <summary>Gets the platform ID for a system and an architecture.</summary>
    /// <param name="windows">True on Windows.</param>
    /// <param name="linux">True on Linux.</param>
    /// <param name="architecture">The processor architecture.</param>
    /// <returns>The ID.</returns>
    public static string Describe(bool windows, bool linux, Architecture architecture)
    {
        string os = windows ? "win" : linux ? "linux" : "other";
        string cpu = architecture == Architecture.X64 ? "x64" : architecture.ToString().ToLowerInvariant();
        return os + "-" + cpu;
    }
}
