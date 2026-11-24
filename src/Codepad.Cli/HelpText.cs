namespace Codepad.Cli;

/// <summary>The help text. It states the trusted-code rule first. A user or an agent must read it before the first run.</summary>
internal static class HelpText
{
    public const string TRUST_WARNING =
        "WARNING: 'codepad run' executes C# code with the permissions of your user account. The worker process is a crash boundary,\n" +
        "not a security sandbox. Run only code that you trust. Reading, listing, searching and updating experiments never runs code.";

    public static string General() => $"""
        codepad - run C# snippets, format JSON and manage the experiments of CodePad from a shell.

        {TRUST_WARNING}

        Usage:
          codepad [global options] <command> [options]

        Commands:
          run      Run a C# snippet. Stores it as an experiment and saves a history record.
          json     Validate and format JSON.
          list     List the experiments, newest edit first.
          search   Search the title and the source: codepad search <text>
          get      Read one experiment: codepad get <id>
          create   Create an experiment.
          update   Change the title or the source of an experiment.
          history  Read the saved runs of an experiment: codepad history <id>

        Global options:
          --data-dir DIR     Use another data folder (default: the folder of CodePad).
          --config-dir DIR   Use another config folder (default: the folder of CodePad).
          --pretty           Indent the JSON output.
          --help, -h         Show help. Use 'codepad <command> --help' for a command.
          --version          Show the version.

        Output:
          The standard output holds one JSON document, with "schemaVersion": 1. Logs and messages go to the
          standard error. The exit code tells the outcome. See docs/cli.md for the schema and the exit codes.

        Exit codes:
          0 success            2 usage error          10 CompilationFailed   11 RuntimeFailed
          12 TimedOut          13 Canceled            14 WorkerFailed        20 invalid JSON
          30 not found         31 conflict            32 wrong kind          40 storage error
          50 input error       1 internal error
        """;

    public static string For(string command) => command switch
    {
        "run" => $"""
            Usage:
              codepad run [--file PATH|-] [--title TITLE] [--timeout SECONDS]
              codepad run --experiment ID [--file PATH|-] [--expected-revision N] [--timeout SECONDS]

            {TRUST_WARNING}

            Runs a C# snippet in the worker process. Without --experiment, the snippet comes from --file or from the standard
            input, and the command creates a new experiment (title: --title, or "CLI run" and the time). With --experiment, the
            command runs the stored source, or the source from --file after it stores it (--expected-revision stops a lost write).
            The run saves a history record that the desktop app shows. The time limit is --timeout, or the setting of CodePad (10 s).
            The exit code is 0 for Success, 10 CompilationFailed, 11 RuntimeFailed, 12 TimedOut, 13 Canceled, 14 WorkerFailed.
            """,
        "json" => """
            Usage:
              codepad json [--file PATH|-]

            Validates JSON and formats it with the same rules as the desktop tool: size limit 1,000,000 bytes, depth limit 64.
            Exit code 0 for valid JSON, 20 for invalid JSON. The output has the formatted text, the tree and, for an error, the line and the column.
            This command never runs code and does not touch the database.
            """,
        "list" => """
            Usage:
              codepad list [--kind csharp|json] [--limit N] [--offset N]

            Lists experiments, newest edit first. A page has at most 200 items. This command never runs code.
            """,
        "search" => """
            Usage:
              codepad search <text> [--kind csharp|json] [--limit N] [--offset N]

            Finds experiments by title and source, ignoring case. The text "json" or "c#" also matches the kind. This command never runs code.
            """,
        "get" => """
            Usage:
              codepad get <id>

            Reads one experiment with its full source. This command never runs code.
            """,
        "create" => """
            Usage:
              codepad create --kind csharp|json --title TITLE [--file PATH|-]

            Creates an experiment. The source comes from --file, or from the standard input when it is piped, or it is empty. This command never runs code.
            """,
        "update" => """
            Usage:
              codepad update <id> [--title TITLE] [--file PATH|-] [--expected-revision N]

            Changes the title or the source. With --expected-revision, a different stored revision gives exit code 31 and writes nothing.
            A write that loses to a newer revision also gives 31. This command never runs code.
            """,
        "history" => """
            Usage:
              codepad history <id> [--limit N]
              codepad history <id> --run EXECUTION_ID

            Lists the saved runs of an experiment, newest first, or shows one saved run with its source and its result. This command never runs code.
            """,
        _ => General(),
    };
}
