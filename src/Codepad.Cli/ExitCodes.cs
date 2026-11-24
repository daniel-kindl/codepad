namespace Codepad.Cli;

/// <summary>
/// The exit codes of the command line. They are part of the contract in docs/cli.md. A run gives one code for each outcome,
/// so a script does not need to parse the JSON to know what happened.
/// </summary>
internal static class ExitCodes
{
    public const int SUCCESS = 0;
    public const int INTERNAL_ERROR = 1;
    public const int USAGE = 2;
    public const int COMPILATION_FAILED = 10;
    public const int RUNTIME_FAILED = 11;
    public const int TIMED_OUT = 12;
    public const int CANCELED = 13;
    public const int WORKER_FAILED = 14;
    public const int INVALID_JSON = 20;
    public const int NOT_FOUND = 30;
    public const int CONFLICT = 31;
    public const int WRONG_KIND = 32;
    public const int STORAGE = 40;
    public const int INPUT = 50;
}
