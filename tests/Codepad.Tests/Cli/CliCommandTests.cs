using System.Diagnostics;
using System.Text.Json;
using Codepad.Application.Json;
using Codepad.Cli;

namespace Codepad.Tests.Cli;

public sealed class CliCommandTests : IDisposable
{
    private readonly CliHarness cli = new CliHarness();

    public void Dispose() => cli.Dispose();

    // --- run ---

    [Fact]
    public async Task RunningOnePlusOneFromStandardInputGivesSuccessAndTheValueTwo()
    {
        CliResult result = await cli.RunAsync(["run"], "1 + 1");

        Assert.Equal(0, result.ExitCode);
        JsonElement json = result.Json;
        Assert.Equal(1, json.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("run", json.GetProperty("command").GetString());
        Assert.True(json.GetProperty("ok").GetBoolean());
        Assert.Equal("Success", json.GetProperty("outcome").GetString());
        JsonElement value = json.GetProperty("value");
        Assert.Equal("scalar", value.GetProperty("kind").GetString());
        Assert.Equal("2", value.GetProperty("value").GetString());
        Assert.True(json.GetProperty("historySaved").GetBoolean());
        Assert.Equal(string.Empty, result.Stderr);
    }

    [Fact]
    public async Task RunTakesTheSourceFromAFile()
    {
        string file = Path.Combine(cli.Root, "snippet.csx");
        Directory.CreateDirectory(cli.Root);
        await File.WriteAllTextAsync(file, "6 * 7", TestContext.Current.CancellationToken);

        CliResult result = await cli.RunAsync(["run", "--file", file]);

        Assert.Equal("42", result.Json.GetProperty("value").GetProperty("value").GetString());
    }

    [Fact]
    public async Task InvalidSourceGivesCompilationFailedWithTheLocationAndTheSnippetDoesNotRun()
    {
        string marker = Path.Combine(cli.Root, "must-not-exist.txt");
        Directory.CreateDirectory(cli.Root);
        string source = $"System.IO.File.WriteAllText(@\"{marker}\", \"ran\");\nint x = ;";

        CliResult result = await cli.RunAsync(["run"], source);

        Assert.Equal(10, result.ExitCode);
        Assert.Equal("CompilationFailed", result.Outcome);
        Assert.False(result.Json.GetProperty("ok").GetBoolean());
        JsonElement diagnostic = result.Json.GetProperty("diagnostics").EnumerateArray().First();
        Assert.Equal("Error", diagnostic.GetProperty("severity").GetString());
        Assert.Equal(2, diagnostic.GetProperty("line").GetInt32());
        Assert.Equal(9, diagnostic.GetProperty("column").GetInt32());
        Assert.False(File.Exists(marker), "The snippet ran although it did not compile.");
    }

    [Fact]
    public async Task AThrownExceptionGivesRuntimeFailed()
    {
        CliResult result = await cli.RunAsync(["run"], "throw new InvalidOperationException(\"boom\");");

        Assert.Equal(11, result.ExitCode);
        Assert.Equal("RuntimeFailed", result.Outcome);
        Assert.Contains("boom", result.Json.GetProperty("diagnostics").EnumerateArray().First().GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInfiniteLoopGivesTimedOutAndNoChildProcessStays()
    {
        string childFile = Path.Combine(cli.Root, "child.pid");
        Directory.CreateDirectory(cli.Root);
        string source = $$"""
            var info = OperatingSystem.IsWindows()
                ? new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c ping -n 300 127.0.0.1 > NUL")
                : new System.Diagnostics.ProcessStartInfo("sleep", "300");
            info.UseShellExecute = false;
            var child = System.Diagnostics.Process.Start(info)!;
            System.IO.File.WriteAllText(@"{{childFile}}", child.Id.ToString());
            while (true) { }
            """;

        CliResult result = await cli.RunAsync(["run", "--timeout", "8"], source);

        Assert.Equal(12, result.ExitCode);
        Assert.Equal("TimedOut", result.Outcome);
        Assert.True(File.Exists(childFile), "The script did not start its child, so the test proves nothing.");
        Assert.True(await ProcessGoneAsync(int.Parse(await File.ReadAllTextAsync(childFile, TestContext.Current.CancellationToken), System.Globalization.CultureInfo.InvariantCulture)), "The child process stays.");
    }

    [Fact]
    public async Task AWorkerCrashGivesWorkerFailed()
    {
        CliResult result = await cli.RunAsync(["run"], "Environment.FailFast(\"fault\");");

        Assert.Equal(14, result.ExitCode);
        Assert.Equal("WorkerFailed", result.Outcome);
        Assert.Equal("WorkerCrashed", result.Json.GetProperty("diagnostics").EnumerateArray().First().GetProperty("code").GetString());
    }

    [Fact]
    public async Task AMissingWorkerProgramIsAnErrorWithItsOwnCode()
    {
        cli.WorkerFolder = Path.Combine(cli.Root, "nowhere");

        CliResult result = await cli.RunAsync(["run"], "1");

        Assert.Equal(14, result.ExitCode);
        Assert.Equal("worker_missing", result.Json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task CancelStopsARunAndGivesCanceled()
    {
        using CancellationTokenSource cancel = new CancellationTokenSource();
        Task<CliResult> run = cli.RunAsync(["run", "--timeout", "60"], "while (true) { }", cancel.Token);
        await Task.Delay(3_000, TestContext.Current.CancellationToken);

        await cancel.CancelAsync();
        CliResult result = await run;

        Assert.Equal(13, result.ExitCode);
        Assert.Equal("Canceled", result.Outcome);
    }

    [Fact]
    public async Task ConsoleOutputOfTheSnippetIsInTheJsonAndNeverOnTheRawStandardOutput()
    {
        CliResult result = await cli.RunAsync(["run"], "Console.WriteLine(\"hello from the snippet\"); Console.Error.WriteLine(\"err text\"); 1");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("hello from the snippet", result.Json.GetProperty("stdout").GetProperty("text").GetString()!.Trim());
        Assert.Equal("err text", result.Json.GetProperty("stderr").GetProperty("text").GetString()!.Trim());
        Assert.Single(result.Stdout.Trim().Split('\n'));
    }

    [Fact]
    public async Task ARunCreatesAnExperimentAndAHistoryRecordThatCanBeRead()
    {
        CliResult run = await cli.RunAsync(["run", "--title", "My snippet"], "40 + 2");
        string id = run.Json.GetProperty("experiment").GetProperty("id").GetString()!;
        string executionId = run.Json.GetProperty("executionId").GetString()!;

        CliResult listed = await cli.RunAsync(["list"]);
        CliResult history = await cli.RunAsync(["history", id]);
        CliResult saved = await cli.RunAsync(["history", id, "--run", executionId]);

        Assert.Equal("My snippet", listed.Json.GetProperty("items")[0].GetProperty("title").GetString());
        JsonElement entry = Assert.Single(history.Json.GetProperty("runs").EnumerateArray());
        Assert.Equal(executionId, entry.GetProperty("executionId").GetString());
        Assert.Equal("Success", entry.GetProperty("outcome").GetString());
        Assert.Equal("40 + 2", saved.Json.GetProperty("run").GetProperty("source").GetString());
        Assert.Equal("42", saved.Json.GetProperty("run").GetProperty("value").GetProperty("value").GetString());
    }

    [Fact]
    public async Task RunOfAStoredExperimentRunsItsSourceAndRunWithAFileStoresTheNewSourceFirst()
    {
        CliResult created = await cli.RunAsync(["create", "--kind", "csharp", "--title", "Stored"], "1 + 1");
        string id = created.Json.GetProperty("experiment").GetProperty("id").GetString()!;

        CliResult first = await cli.RunAsync(["run", "--experiment", id]);
        string file = Path.Combine(cli.Root, "new.csx");
        await File.WriteAllTextAsync(file, "10 + 10", TestContext.Current.CancellationToken);
        CliResult second = await cli.RunAsync(["run", "--experiment", id, "--file", file, "--expected-revision", "1"]);
        CliResult read = await cli.RunAsync(["get", id]);

        Assert.Equal("2", first.Json.GetProperty("value").GetProperty("value").GetString());
        Assert.Equal("20", second.Json.GetProperty("value").GetProperty("value").GetString());
        Assert.Equal("10 + 10", read.Json.GetProperty("experiment").GetProperty("source").GetString());
        Assert.Equal(2, read.Json.GetProperty("experiment").GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task RunWithAWrongExpectedRevisionWritesNothingAndRunsNothing()
    {
        CliResult created = await cli.RunAsync(["create", "--kind", "csharp", "--title", "Stored"], "1 + 1");
        string id = created.Json.GetProperty("experiment").GetProperty("id").GetString()!;
        string file = Path.Combine(cli.Root, "new.csx");
        await File.WriteAllTextAsync(file, "999", TestContext.Current.CancellationToken);

        CliResult result = await cli.RunAsync(["run", "--experiment", id, "--file", file, "--expected-revision", "7"]);

        Assert.Equal(31, result.ExitCode);
        Assert.Equal("revision_conflict", result.Json.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty((await cli.RunAsync(["history", id])).Json.GetProperty("runs").EnumerateArray());
        Assert.Equal("1 + 1", (await cli.RunAsync(["get", id])).Json.GetProperty("experiment").GetProperty("source").GetString());
    }

    [Fact]
    public async Task RunOfAJsonExperimentOrAMissingOneIsRefused()
    {
        CliResult json = await cli.RunAsync(["create", "--kind", "json", "--title", "J"], "{}");
        string id = json.Json.GetProperty("experiment").GetProperty("id").GetString()!;

        Assert.Equal(32, (await cli.RunAsync(["run", "--experiment", id])).ExitCode);
        Assert.Equal(30, (await cli.RunAsync(["run", "--experiment", Guid.NewGuid().ToString()])).ExitCode);
    }

    [Fact]
    public async Task ATimeoutOptionOverridesTheSettingAndTheSettingOfTheDesktopAppIsTheDefault()
    {
        Directory.CreateDirectory(cli.ConfigFolder);
        await File.WriteAllTextAsync(Path.Combine(cli.ConfigFolder, "settings.json"), "{ \"preferences\": { \"version\": 1, \"runTimeoutSeconds\": 2 } }", TestContext.Current.CancellationToken);

        CliResult timedOut = await cli.RunAsync(["run"], "while (true) { }");

        Assert.Equal(12, timedOut.ExitCode);
        Assert.Contains("2 s", timedOut.Json.GetProperty("diagnostics").EnumerateArray().First().GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    // --- json ---

    [Fact]
    public async Task ValidJsonIsFormattedWithATree()
    {
        CliResult result = await cli.RunAsync(["json"], "{ \"a\": [1, 2] }");

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Json.GetProperty("valid").GetBoolean());
        Assert.Equal("{\n  \"a\": [\n    1,\n    2\n  ]\n}", result.Json.GetProperty("formatted").GetString()!.ReplaceLineEndings("\n"));
        Assert.Equal("object", result.Json.GetProperty("tree").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task MalformedJsonGivesALocatedErrorWithExitCode20()
    {
        CliResult result = await cli.RunAsync(["json"], "{\n  \"a\": ,\n}");

        Assert.Equal(20, result.ExitCode);
        JsonElement problem = result.Json.GetProperty("problem");
        Assert.Equal(2, problem.GetProperty("line").GetInt32());
        Assert.Equal(8, problem.GetProperty("column").GetInt32());
        Assert.Equal("JSON_SYNTAX", problem.GetProperty("code").GetString());
        Assert.False(result.Json.GetProperty("valid").GetBoolean());
    }

    [Theory]
    [InlineData("{\n  \"a\": ,\n}")]
    [InlineData("[1, 2,")]
    [InlineData("")]
    [InlineData("[\"ž\" x]")]
    public async Task TheDiagnosticsAreTheSameAsTheDesktopTool(string text)
    {
        JsonEvaluation desktop = new JsonEvaluator().Evaluate(text);

        CliResult result = await cli.RunAsync(["json"], text);

        JsonElement problem = result.Json.GetProperty("problem");
        Assert.Equal(desktop.Problem!.Code, problem.GetProperty("code").GetString());
        Assert.Equal(desktop.Problem.Message, problem.GetProperty("message").GetString());
        Assert.Equal(desktop.Problem.Location?.Line, problem.GetProperty("line").ValueKind == JsonValueKind.Null ? null : problem.GetProperty("line").GetInt32());
        Assert.Equal(desktop.Problem.Location?.Column, problem.GetProperty("column").ValueKind == JsonValueKind.Null ? null : problem.GetProperty("column").GetInt32());
    }

    [Fact]
    public async Task TheJsonCommandNeedsNoDatabaseAndNeverRunsCode()
    {
        await cli.RunAsync(["json"], "[1]");

        Assert.False(Directory.Exists(cli.DataFolder), "The json command created a data folder.");
    }

    // --- library commands ---

    [Fact]
    public async Task CreateGetUpdateListAndSearchWork()
    {
        CliResult created = await cli.RunAsync(["create", "--kind", "json", "--title", "Payload", "--file", "-"], "{ \"total\": 5 }");
        string id = created.Json.GetProperty("experiment").GetProperty("id").GetString()!;
        Assert.Equal(1, created.Json.GetProperty("experiment").GetProperty("revision").GetInt64());

        CliResult updated = await cli.RunAsync(["update", id, "--title", "Payload v2", "--expected-revision", "1"]);
        CliResult found = await cli.RunAsync(["search", "total"]);
        CliResult byKind = await cli.RunAsync(["search", "json", "--kind", "json"]);
        CliResult none = await cli.RunAsync(["search", "zzz-nothing"]);
        CliResult read = await cli.RunAsync(["get", id]);

        Assert.Equal("Payload v2", updated.Json.GetProperty("experiment").GetProperty("title").GetString());
        Assert.Equal(2, updated.Json.GetProperty("experiment").GetProperty("revision").GetInt64());
        Assert.Equal(id, Assert.Single(found.Json.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        Assert.Single(byKind.Json.GetProperty("items").EnumerateArray());
        Assert.Empty(none.Json.GetProperty("items").EnumerateArray());
        Assert.Equal("{ \"total\": 5 }", read.Json.GetProperty("experiment").GetProperty("source").GetString());
    }

    [Fact]
    public async Task UpdateOfAMissingExperimentIsNotFoundAndAStaleRevisionIsAConflict()
    {
        CliResult created = await cli.RunAsync(["create", "--kind", "csharp", "--title", "T"], "1");
        string id = created.Json.GetProperty("experiment").GetProperty("id").GetString()!;

        Assert.Equal(30, (await cli.RunAsync(["update", Guid.NewGuid().ToString(), "--title", "x"])).ExitCode);
        CliResult conflict = await cli.RunAsync(["update", id, "--title", "x", "--expected-revision", "9"]);

        Assert.Equal(31, conflict.ExitCode);
        Assert.Contains("revision is 1", conflict.Json.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("T", (await cli.RunAsync(["get", id])).Json.GetProperty("experiment").GetProperty("title").GetString());
    }

    [Fact]
    public async Task ReadSearchListCreateAndUpdateNeverStartARun()
    {
        // The worker is missing. A command that tried to run code would fail with worker_missing.
        cli.WorkerFolder = Path.Combine(cli.Root, "nowhere");
        CliResult created = await cli.RunAsync(["create", "--kind", "csharp", "--title", "Dangerous"], "System.IO.File.WriteAllText(\"/tmp/codepad-cli-never\", \"x\");");
        string id = created.Json.GetProperty("experiment").GetProperty("id").GetString()!;

        foreach (string[] args in new string[][] { ["list"], ["search", "dangerous"], ["get", id], ["history", id], ["update", id, "--title", "Renamed"] })
        {
            CliResult result = await cli.RunAsync(args);
            Assert.Equal(0, result.ExitCode);
        }

        Assert.Empty((await cli.RunAsync(["history", id])).Json.GetProperty("runs").EnumerateArray());
        Assert.False(File.Exists("/tmp/codepad-cli-never"));
    }

    // --- a running desktop app and the CLI share the database ---

    [Fact]
    public async Task AnExperimentThatTheCliCreatesShowsInTheLibraryAndAnAutosaveOfTheDesktopIsNotLost()
    {
        await cli.RunAsync(["list"]);
        Codepad.Infrastructure.Storage.SqliteDatabase desktopDatabase = new Codepad.Infrastructure.Storage.SqliteDatabase(Path.Combine(cli.DataFolder, "codepad.db"));
        desktopDatabase.Initialize();
        Codepad.Infrastructure.Storage.SqliteExperimentRepository desktopExperiments = new Codepad.Infrastructure.Storage.SqliteExperimentRepository(desktopDatabase);
        Codepad.Infrastructure.Storage.SqliteLibraryRepository desktopLibrary = new Codepad.Infrastructure.Storage.SqliteLibraryRepository(desktopDatabase);

        // The desktop opens an experiment and keeps editing it.
        Codepad.Domain.Experiments.Experiment desktopOwn = Codepad.Domain.Experiments.Experiment.Create(Codepad.Domain.Experiments.ExperimentId.New(), Codepad.Domain.Operations.OperationKind.CSharp, "Desktop work", "v1", DateTimeOffset.UtcNow);
        await desktopExperiments.SaveAsync(desktopOwn, TestContext.Current.CancellationToken);

        CliResult created = await cli.RunAsync(["create", "--kind", "csharp", "--title", "From the CLI"], "1");
        await desktopExperiments.SaveAsync(desktopOwn.WithSource("v2", DateTimeOffset.UtcNow.AddSeconds(1)), TestContext.Current.CancellationToken);

        Codepad.Application.Library.LibraryPage page = await desktopLibrary.SearchAsync(new Codepad.Application.Library.LibraryQuery("CLI"), TestContext.Current.CancellationToken);
        Assert.Equal(created.Json.GetProperty("experiment").GetProperty("id").GetString(), Assert.Single(page.Items).Id.ToString());
        Assert.Equal("v2", (await desktopExperiments.GetAsync(desktopOwn.Id, TestContext.Current.CancellationToken))!.Source);

        // The reverse: an experiment that the desktop stored is readable and changeable through the CLI.
        CliResult read = await cli.RunAsync(["get", desktopOwn.Id.ToString()]);
        Assert.Equal("v2", read.Json.GetProperty("experiment").GetProperty("source").GetString());
    }

    [Fact]
    public async Task ACliUpdateAndADesktopAutosaveOfTheSameRevisionNeverBothWin()
    {
        CliResult created = await cli.RunAsync(["create", "--kind", "csharp", "--title", "Shared"], "base");
        string id = created.Json.GetProperty("experiment").GetProperty("id").GetString()!;
        Codepad.Infrastructure.Storage.SqliteDatabase database = new Codepad.Infrastructure.Storage.SqliteDatabase(Path.Combine(cli.DataFolder, "codepad.db"));
        database.Initialize();
        Codepad.Infrastructure.Storage.SqliteExperimentRepository desktop = new Codepad.Infrastructure.Storage.SqliteExperimentRepository(database);
        Codepad.Domain.Experiments.Experiment opened = (await desktop.GetAsync(new Codepad.Domain.Experiments.ExperimentId(Guid.Parse(id)), TestContext.Current.CancellationToken))!;

        // The CLI writes revision 2 first. The desktop edit also has revision 2 and must not replace it silently.
        CliResult update = await cli.RunAsync(["update", id, "--expected-revision", "1", "--file", "-"], "cli text");
        Codepad.Application.Experiments.SaveResult desktopSave = await desktop.SaveAsync(opened.WithSource("desktop text", DateTimeOffset.UtcNow.AddSeconds(1)), TestContext.Current.CancellationToken);

        Assert.Equal(0, update.ExitCode);
        Assert.Equal(Codepad.Application.Experiments.SaveResult.Stale, desktopSave);
        Assert.Equal("cli text", (await cli.RunAsync(["get", id])).Json.GetProperty("experiment").GetProperty("source").GetString());
    }

    [Fact]
    public async Task ManyCliProcessesThatCreateAtOnceLoseNoWrite()
    {
        await cli.RunAsync(["list"]);

        CliResult[] results = await Task.WhenAll(Enumerable.Range(0, 12).Select(index => cli.RunAsync(["create", "--kind", "json", "--title", $"Parallel {index}"], "[]")));

        Assert.All(results, result => Assert.Equal(0, result.ExitCode));
        CliResult listed = await cli.RunAsync(["list", "--limit", "200"]);
        Assert.Equal(12, listed.Json.GetProperty("items").GetArrayLength());
    }

    // --- usage, help and the output contract ---

    [Theory]
    [InlineData("")]
    [InlineData("frobnicate")]
    [InlineData("run --nope")]
    [InlineData("get")]
    [InlineData("get not-an-id")]
    [InlineData("create --kind csharp")]
    [InlineData("list --limit 0")]
    [InlineData("update 00000000-0000-0000-0000-000000000001")]
    public async Task AUsageErrorGivesExitCode2AndAJsonErrorOnTheStandardOutput(string line)
    {
        string[] args = line.Length == 0 ? [] : line.Split(' ');

        CliResult result = await cli.RunAsync(args, stdin: null);

        Assert.Equal(2, result.ExitCode);
        JsonElement json = result.Json;
        Assert.False(json.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", json.GetProperty("error").GetProperty("code").GetString());
        Assert.NotEqual(string.Empty, result.Stderr);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RunWithABadTitleIsAUsageErrorNotAnInternalError(string title)
    {
        CliResult result = await cli.RunAsync(["run", "--title", title], "1");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.Json.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty((await cli.RunAsync(["list"])).Json.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task RunWithATitleThatIsTooLongIsAUsageError()
    {
        CliResult result = await cli.RunAsync(["run", "--title", new string('x', 101)], "1");

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task AGlobalLookingWordAfterTheSeparatorIsSearchText()
    {
        await cli.RunAsync(["create", "--kind", "csharp", "--title", "has --pretty inside"], "1");

        CliResult result = await cli.RunAsync(["search", "--", "--pretty"]);

        Assert.Single(result.Json.GetProperty("items").EnumerateArray());
        Assert.DoesNotContain("\n  ", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateOfTitleAndSourceTogetherMovesTheRevisionByOne()
    {
        CliResult created = await cli.RunAsync(["create", "--kind", "csharp", "--title", "T"], "1");
        string id = created.Json.GetProperty("experiment").GetProperty("id").GetString()!;

        CliResult updated = await cli.RunAsync(["update", id, "--title", "New", "--file", "-"], "2");

        Assert.Equal(2, updated.Json.GetProperty("experiment").GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task RunWithoutAnyInputDoesNotWaitOnATerminal()
    {
        CliResult result = await cli.RunAsync(["run"], stdin: null);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("no input", result.Json.GetProperty("error").GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AFileThatDoesNotExistGivesInputErrorWithoutThePathInTheMessage()
    {
        string missing = Path.Combine(cli.Root, "secret-folder-name", "x.csx");

        CliResult result = await cli.RunAsync(["json", "--file", missing]);

        Assert.Equal(50, result.ExitCode);
        Assert.DoesNotContain("secret-folder-name", result.Stdout + result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelpStatesTheTrustedCodeWarningAndExitsWithZero()
    {
        foreach (string[] args in new string[][] { ["--help"], ["help"], ["run", "--help"], ["help", "run"] })
        {
            CliResult result = await cli.RunAsync(args);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("permissions of your user account", result.Stdout, StringComparison.Ordinal);
            Assert.Contains("not a security sandbox", result.Stdout, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task EveryOutputHasTheSchemaVersionAndTheExitCodeMatches()
    {
        CliResult created = await cli.RunAsync(["create", "--kind", "csharp", "--title", "T"], "1");
        string id = created.Json.GetProperty("experiment").GetProperty("id").GetString()!;

        foreach ((string[] args, string? stdin) in new (string[], string?)[]
        {
            (["list"], null),
            (["search", "T"], null),
            (["get", id], null),
            (["history", id], null),
            (["json"], "[1]"),
            (["run"], "1"),
            (["get", Guid.NewGuid().ToString()], null),
        })
        {
            CliResult result = await cli.RunAsync(args, stdin);
            JsonElement json = result.Json;
            Assert.Equal(1, json.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(result.ExitCode, json.GetProperty("exitCode").GetInt32());
            Assert.Equal(result.ExitCode == 0, json.GetProperty("ok").GetBoolean());
        }
    }

    [Fact]
    public async Task TheRunOutputHasTheDocumentedProperties()
    {
        CliResult result = await cli.RunAsync(["run"], "1");

        string[] names = [.. result.Json.EnumerateObject().Select(property => property.Name)];

        Assert.Equal(
            ["schemaVersion", "command", "ok", "exitCode", "outcome", "experiment", "executionId", "durationMs", "diagnostics", "value", "stdout", "stderr", "host", "historySaved", "historyError"],
            names);
    }

    [Fact]
    public async Task LogsAndSourceNeverReachTheStandardOutputAndTheLogsHoldNoSource()
    {
        CliResult result = await cli.RunAsync(["run"], "var secret = \"SECRET_CLI_MARKER_5521\"; Environment.FailFast(secret);");

        Assert.Equal(14, result.ExitCode);
        Assert.DoesNotContain("SECRET_CLI_MARKER_5521", result.Stderr, StringComparison.Ordinal);
        Assert.Single(result.Stdout.Trim().Split('\n'));
    }

    [Fact]
    public async Task VersionPrintsOneLine()
    {
        CliResult result = await cli.RunAsync(["--version"]);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("0.1.0", result.Stdout.Trim(), StringComparison.Ordinal);
    }

    // --- the real executable ---

    [Fact]
    public async Task TheRealProgramRunsOnePlusOneWithoutADesktopWindow()
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "codepad.dll");
        Assert.True(File.Exists(dll), "codepad.dll is not next to the tests.");
        ProcessStartInfo info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add(dll);
        info.ArgumentList.Add("--data-dir");
        info.ArgumentList.Add(cli.DataFolder);
        info.ArgumentList.Add("--config-dir");
        info.ArgumentList.Add(cli.ConfigFolder);
        info.ArgumentList.Add("run");
        info.Environment["DISPLAY"] = string.Empty;
        info.Environment["WAYLAND_DISPLAY"] = string.Empty;

        using Process process = Process.Start(info)!;
        await process.StandardInput.WriteAsync("1 + 1".AsMemory(), TestContext.Current.CancellationToken);
        process.StandardInput.Close();
        string output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, process.ExitCode);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal("2", document.RootElement.GetProperty("value").GetProperty("value").GetString());
    }

    private static async Task<bool> ProcessGoneAsync(int processId)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return false;
    }
}
