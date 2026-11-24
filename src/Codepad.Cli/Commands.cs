using System.Globalization;
using Codepad.Application.Execution;
using Codepad.Application.Experiments;
using Codepad.Application.History;
using Codepad.Application.Json;
using Codepad.Application.Library;
using Codepad.Application.Settings;
using Codepad.Contracts;
using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Domain.Results;

namespace Codepad.Cli;

/// <summary>The commands. Each one reads its input, calls a use case of the Application layer and gives one output with an exit code.</summary>
internal static class Commands
{
    private static readonly IReadOnlyDictionary<string, bool> s_none = new Dictionary<string, bool>();

    public static IReadOnlyDictionary<string, bool> Spec(string command) => command switch
    {
        "run" => new Dictionary<string, bool> { ["--file"] = true, ["--title"] = true, ["--timeout"] = true, ["--experiment"] = true, ["--expected-revision"] = true },
        "json" => new Dictionary<string, bool> { ["--file"] = true },
        "list" => new Dictionary<string, bool> { ["--kind"] = true, ["--limit"] = true, ["--offset"] = true },
        "search" => new Dictionary<string, bool> { ["--kind"] = true, ["--limit"] = true, ["--offset"] = true },
        "create" => new Dictionary<string, bool> { ["--kind"] = true, ["--title"] = true, ["--file"] = true },
        "update" => new Dictionary<string, bool> { ["--title"] = true, ["--file"] = true, ["--expected-revision"] = true },
        "history" => new Dictionary<string, bool> { ["--limit"] = true, ["--run"] = true },
        _ => s_none,
    };

    public static async Task<(object Output, int ExitCode)> RunAsync(Args args, Services services, CliEnvironment environment, CancellationToken cancellationToken)
    {
        if (args.Positional.Count > 0)
        {
            throw new UsageException("The run command takes no words. Use --file PATH, or pipe the snippet to the standard input.");
        }

        string? runTitle = args.Value("--title");
        if (runTitle is not null)
        {
            try
            {
                _ = ExperimentRules.NormalizeTitle(runTitle);
            }
            catch (ArgumentException exception)
            {
                throw new UsageException(exception.Message);
            }
        }

        string? experimentText = args.Value("--experiment");
        long? expectedRevision = args.Number("--expected-revision", 1, long.MaxValue);
        int? timeout = (int?)args.Number("--timeout", UserSettingsRules.Timeout.Min, UserSettingsRules.Timeout.Max);
        ExperimentId? existing = experimentText is null ? null : ParseId(experimentText);
        if (existing is null && expectedRevision is not null)
        {
            throw new UsageException("--expected-revision needs --experiment.");
        }

        // The source comes from a file or the standard input. With --experiment, the input is optional and only an explicit --file gives a new source.
        string? source = null;
        string? file = args.Value("--file");
        if (file is not null)
        {
            source = await Input.ReadAsync(file, environment, cancellationToken).ConfigureAwait(false);
        }
        else if (existing is null)
        {
            source = await Input.ReadStdinAsync(environment, cancellationToken).ConfigureAwait(false);
        }

        ExperimentRunService? runs = services.CreateRunService(timeout, environment.Clock);
        if (runs is null)
        {
            return Error("run", ExitCodes.WORKER_FAILED, "worker_missing", "The worker program is missing, so CodePad cannot run code. Reinstall CodePad.");
        }

        ExperimentRunResult result = existing is null
            ? await runs.RunNewAsync(runTitle ?? DefaultTitle(environment.Clock), source!, cancellationToken).ConfigureAwait(false)
            : await runs.RunExistingAsync(existing, source, expectedRevision, cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            CatalogStatus.NotFound => Error("run", ExitCodes.NOT_FOUND, "not_found", "No experiment has this ID."),
            CatalogStatus.Conflict => Error("run", ExitCodes.CONFLICT, "revision_conflict", $"The experiment changed. Its stored revision is {result.Experiment?.Revision.ToString(CultureInfo.InvariantCulture)}. Nothing was written and nothing ran."),
            CatalogStatus.WrongKind => Error("run", ExitCodes.WRONG_KIND, "wrong_kind", "Only a C# experiment can run. Use 'codepad json' for JSON."),
            _ => Completed(result),
        };
    }

    public static async Task<(object Output, int ExitCode)> JsonAsync(Args args, CliEnvironment environment, CancellationToken cancellationToken)
    {
        if (args.Positional.Count > 0)
        {
            throw new UsageException("The json command takes no words. Use --file PATH, or pipe the JSON to the standard input.");
        }

        string text = await Input.ReadAsync(args.Value("--file") ?? "-", environment, cancellationToken).ConfigureAwait(false);
        JsonEvaluation evaluation = new JsonEvaluator().Evaluate(text);
        if (evaluation.IsValid)
        {
            return (new JsonOutput(Schema.VERSION, "json", true, ExitCodes.SUCCESS, true, evaluation.Formatted, evaluation.Tree, null), ExitCodes.SUCCESS);
        }

        return (new JsonOutput(Schema.VERSION, "json", false, ExitCodes.INVALID_JSON, false, null, null, ToDto(evaluation.Problem!)), ExitCodes.INVALID_JSON);
    }

    public static async Task<(object Output, int ExitCode)> ListAsync(string command, Args args, Services services, CancellationToken cancellationToken)
    {
        string text = string.Empty;
        if (command == "search")
        {
            if (args.Positional.Count == 0)
            {
                throw new UsageException("Give the text to search: codepad search <text>.");
            }

            text = string.Join(' ', args.Positional);
        }
        else if (args.Positional.Count > 0)
        {
            throw new UsageException("The list command takes no words. Use 'codepad search <text>' to search.");
        }

        LibraryQuery query = new LibraryQuery(
            text,
            ParseKind(args.Value("--kind")),
            (int?)args.Number("--limit", 1, LibraryQuery.MaxLimit) ?? LibraryQuery.DefaultLimit,
            (int?)args.Number("--offset", 0, int.MaxValue) ?? 0);
        LibraryPage page = await services.Library.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        List<SummaryDto> items = [.. page.Items.Select(item => new SummaryDto(item.Id.ToString(), item.Kind.ToString(), item.Title, item.Preview, item.ModifiedAt))];
        return (new ListOutput(Schema.VERSION, command, true, ExitCodes.SUCCESS, items, page.HasMore, page.NextOffset), ExitCodes.SUCCESS);
    }

    public static async Task<(object Output, int ExitCode)> GetAsync(Args args, Services services, TimeProvider clock, CancellationToken cancellationToken)
    {
        ExperimentId id = ParseId(OnlyWord(args, "get"));
        Experiment? stored = await services.CreateCatalog(clock).GetAsync(id, cancellationToken).ConfigureAwait(false);
        return stored is null
            ? Error("get", ExitCodes.NOT_FOUND, "not_found", "No experiment has this ID.")
            : (new ExperimentOutput(Schema.VERSION, "get", true, ExitCodes.SUCCESS, ToDto(stored)), ExitCodes.SUCCESS);
    }

    public static async Task<(object Output, int ExitCode)> CreateAsync(Args args, Services services, CliEnvironment environment, CancellationToken cancellationToken)
    {
        if (args.Positional.Count > 0)
        {
            throw new UsageException("The create command takes no words. Use --kind, --title and --file.");
        }

        OperationKind kind = ParseKind(args.Value("--kind")) ?? throw new UsageException("Give the kind with --kind csharp or --kind json.");
        string title = args.Value("--title") ?? throw new UsageException("Give a title with --title.");
        string source = args.Value("--file") is string file
            ? await Input.ReadAsync(file, environment, cancellationToken).ConfigureAwait(false)
            : environment.StdinIsRedirected ? await Input.ReadStdinAsync(environment, cancellationToken).ConfigureAwait(false) : string.Empty;

        try
        {
            Experiment created = await services.CreateCatalog(environment.Clock).CreateAsync(kind, title, source, cancellationToken).ConfigureAwait(false);
            return (new ExperimentOutput(Schema.VERSION, "create", true, ExitCodes.SUCCESS, ToDto(created)), ExitCodes.SUCCESS);
        }
        catch (ArgumentException exception)
        {
            throw new UsageException(exception.Message);
        }
    }

    public static async Task<(object Output, int ExitCode)> UpdateAsync(Args args, Services services, CliEnvironment environment, CancellationToken cancellationToken)
    {
        ExperimentId id = ParseId(OnlyWord(args, "update"));
        string? title = args.Value("--title");
        string? source = args.Value("--file") is string file ? await Input.ReadAsync(file, environment, cancellationToken).ConfigureAwait(false) : null;
        if (title is null && source is null)
        {
            throw new UsageException("Give something to change: --title or --file.");
        }

        CatalogUpdate update;
        try
        {
            update = await services.CreateCatalog(environment.Clock).UpdateAsync(id, title, source, args.Number("--expected-revision", 1, long.MaxValue), cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            throw new UsageException(exception.Message);
        }

        return update.Status switch
        {
            CatalogStatus.NotFound => Error("update", ExitCodes.NOT_FOUND, "not_found", "No experiment has this ID."),
            CatalogStatus.Conflict => Error("update", ExitCodes.CONFLICT, "revision_conflict", $"The experiment changed. Its stored revision is {update.Experiment?.Revision.ToString(CultureInfo.InvariantCulture)}. Nothing was written."),
            _ => (new ExperimentOutput(Schema.VERSION, "update", true, ExitCodes.SUCCESS, ToDto(update.Experiment!)), ExitCodes.SUCCESS),
        };
    }

    public static async Task<(object Output, int ExitCode)> HistoryAsync(Args args, Services services, TimeProvider clock, CancellationToken cancellationToken)
    {
        ExperimentId id = ParseId(OnlyWord(args, "history"));
        if (await services.CreateCatalog(clock).GetAsync(id, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error("history", ExitCodes.NOT_FOUND, "not_found", "No experiment has this ID.");
        }

        if (args.Value("--run") is string runText)
        {
            if (args.Has("--limit"))
            {
                throw new UsageException("--limit cannot be used with --run.");
            }

            if (!Guid.TryParse(runText, out Guid runGuid) || runGuid == Guid.Empty)
            {
                throw new UsageException("--run needs the execution ID of a saved run.");
            }

            RunRecord? record = await services.History.GetAsync(new ExecutionId(runGuid), cancellationToken).ConfigureAwait(false);
            return record is null || record.Request.ExperimentId != id
                ? Error("history", ExitCodes.NOT_FOUND, "not_found", "This experiment has no saved run with this ID.")
                : (new SavedRunOutput(Schema.VERSION, "history", true, ExitCodes.SUCCESS, ToDto(record)), ExitCodes.SUCCESS);
        }

        int limit = (int?)args.Number("--limit", 1, 1_000) ?? 100;
        IReadOnlyList<RunSummary> runs = await services.History.ListAsync(id, cancellationToken).ConfigureAwait(false);
        List<HistoryItemDto> items = [.. runs.Take(limit).Select(run => new HistoryItemDto(run.Id.ToString(), run.ExperimentRevision, run.RequestedAt, run.CompletedAt, (long)run.Duration.TotalMilliseconds, run.Outcome.ToString()))];
        return (new HistoryOutput(Schema.VERSION, "history", true, ExitCodes.SUCCESS, id.ToString(), items), ExitCodes.SUCCESS);
    }

    public static (object Output, int ExitCode) Error(string command, int exitCode, string code, string message) =>
        (new ErrorOutput(Schema.VERSION, command, false, exitCode, new ErrorDto(code, message)), exitCode);

    private static (object Output, int ExitCode) Completed(ExperimentRunResult result)
    {
        RunRecord record = result.Run!.Record;
        ExecutionOutcome outcome = record.Report.Result.Outcome;
        int exitCode = ExitCodeFor(outcome);
        RunOutput output = new RunOutput(
            Schema.VERSION,
            "run",
            outcome == ExecutionOutcome.Success,
            exitCode,
            outcome.ToString(),
            new ExperimentRefDto(result.Experiment!.Id.ToString(), result.Experiment.Title, result.Experiment.Kind.ToString(), result.Experiment.Revision),
            record.Request.Id.ToString(),
            (long)record.Duration.TotalMilliseconds,
            [.. record.Report.Result.Diagnostics.Select(ToDto)],
            record.Report.Result.Value,
            ToDto(record.Report.Stdout),
            ToDto(record.Report.Stderr),
            ToDto(record.Report.Host),
            result.Run.StorageFailure is null,
            result.Run.StorageFailure);
        return (output, exitCode);
    }

    private static int ExitCodeFor(ExecutionOutcome outcome) => outcome switch
    {
        ExecutionOutcome.Success => ExitCodes.SUCCESS,
        ExecutionOutcome.CompilationFailed => ExitCodes.COMPILATION_FAILED,
        ExecutionOutcome.RuntimeFailed => ExitCodes.RUNTIME_FAILED,
        ExecutionOutcome.TimedOut => ExitCodes.TIMED_OUT,
        ExecutionOutcome.Canceled => ExitCodes.CANCELED,
        _ => ExitCodes.WORKER_FAILED,
    };

    private static string DefaultTitle(TimeProvider clock) => "CLI run " + clock.GetUtcNow().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string OnlyWord(Args args, string command)
    {
        if (args.Positional.Count != 1)
        {
            throw new UsageException($"Give the experiment ID: codepad {command} <id>.");
        }

        return args.Positional[0];
    }

    private static ExperimentId ParseId(string text) =>
        Guid.TryParse(text, out Guid value) && value != Guid.Empty ? new ExperimentId(value) : throw new UsageException("An experiment ID looks like 01a20ac6-fe78-7265-883a-c313e431c7bf.");

    private static OperationKind? ParseKind(string? text) => text?.ToLowerInvariant() switch
    {
        null => null,
        "csharp" or "c#" => OperationKind.CSharp,
        "json" => OperationKind.Json,
        _ => throw new UsageException("--kind must be csharp or json."),
    };

    private static ExperimentDto ToDto(Experiment experiment) =>
        new(experiment.Id.ToString(), experiment.Kind.ToString(), experiment.Title, experiment.Source, experiment.Input, experiment.CreatedAt, experiment.ModifiedAt, experiment.Revision);

    private static DiagnosticDto ToDto(Diagnostic diagnostic) =>
        new(diagnostic.Severity.ToString(), diagnostic.Code, diagnostic.Message, diagnostic.Location?.Line, diagnostic.Location?.Column);

    private static TextDto ToDto(CapturedText text) => new(text.Text, text.IsTruncated, text.OriginalLength);

    private static HostDto? ToDto(WorkerHostInfo? host) =>
        host is null ? null : new HostDto(host.Runtime, host.OperatingSystem, host.Architecture, host.WorkerVersion, host.Language);

    private static SavedRunDto ToDto(RunRecord record) => new(
        record.Request.Id.ToString(),
        record.Request.ExperimentId.ToString(),
        record.Request.ExperimentRevision,
        record.Request.RequestedAt,
        record.CompletedAt,
        record.Report.Result.Outcome.ToString(),
        record.Request.Source,
        record.Request.Input,
        [.. record.Report.Result.Diagnostics.Select(ToDto)],
        record.Report.Result.Value,
        ToDto(record.Report.Stdout),
        ToDto(record.Report.Stderr),
        ToDto(record.Report.Host));
}
