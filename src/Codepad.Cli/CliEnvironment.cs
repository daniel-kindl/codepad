namespace Codepad.Cli;

/// <summary>
/// Everything that the command line takes from its surroundings: the three streams, the folders and the clock. The tests give
/// their own. The program gives the console and the folders of the user.
/// </summary>
/// <param name="Stdin">The standard input.</param>
/// <param name="StdinIsRedirected">True when the input comes from a pipe or a file. A terminal gives false, and the CLI never waits on it.</param>
/// <param name="Stdout">The standard output. It holds only the JSON result.</param>
/// <param name="Stderr">The standard error. It holds logs and short messages.</param>
/// <param name="DataFolder">The data folder, or null to use the folder of the user.</param>
/// <param name="ConfigFolder">The config folder, or null to use the folder of the user.</param>
/// <param name="Clock">The time source.</param>
/// <param name="WorkerFolder">The folder that holds the worker program, or null to use the folder of the CLI.</param>
internal sealed record CliEnvironment(
    TextReader Stdin,
    bool StdinIsRedirected,
    TextWriter Stdout,
    TextWriter Stderr,
    string? DataFolder,
    string? ConfigFolder,
    TimeProvider Clock,
    string? WorkerFolder = null);
