using Microsoft.Extensions.Logging;

namespace Codepad.Application.Startup;

/// <summary>The user settings that CodePad reads at startup.</summary>
/// <param name="MinimumLogLevel">The lowest level that the app writes to the log.</param>
public sealed record AppSettings(LogLevel MinimumLogLevel)
{
    /// <summary>Gets the settings that CodePad uses when the user has no usable settings file.</summary>
    public static AppSettings Default { get; } = new(LogLevel.Information);
}
