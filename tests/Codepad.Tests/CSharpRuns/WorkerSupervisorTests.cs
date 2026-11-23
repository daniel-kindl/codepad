using System.Diagnostics;
using Codepad.Application.Execution;
using Codepad.Domain.Executions;
using Codepad.Domain.Results;
using Codepad.Infrastructure.Execution;

namespace Codepad.Tests.CSharpRuns;

/// <summary>Integration tests. Each test starts the real worker process.</summary>
public sealed class WorkerSupervisorTests : IDisposable
{
    private readonly WorkerHarness _harness = new WorkerHarness();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ExpressionReturnsItsValue()
    {
        RunReport report = await _harness.RunAsync("1 + 2");

        Assert.Equal(ExecutionOutcome.Success, report.Result.Outcome);
        ScalarNode value = Assert.IsType<ScalarNode>(report.Result.Value);
        Assert.Equal("System.Int32", value.TypeName);
        Assert.Equal("3", value.Value);
    }

    [Fact]
    public async Task MultiStatementSnippetReturnsTheLastExpression()
    {
        RunReport report = await _harness.RunAsync("int a = 20;\nint b = a * 2;\nstring.Join(\",\", new[] { a, b + 2 })");

        Assert.Equal(ExecutionOutcome.Success, report.Result.Outcome);
        Assert.Equal("20,42", Assert.IsType<TextNode>(report.Result.Value).Value);
    }

    [Fact]
    public async Task SnippetWithNoValueSucceedsWithANullValue()
    {
        RunReport report = await _harness.RunAsync("int a = 1;");

        Assert.Equal(ExecutionOutcome.Success, report.Result.Outcome);
        Assert.True(report.Result.Value is null or NullNode);
    }

    [Fact]
    public async Task AwaitWorksAtTheTopLevel()
    {
        RunReport report = await _harness.RunAsync("await System.Threading.Tasks.Task.Delay(10);\n7");

        Assert.Equal("7", Assert.IsType<ScalarNode>(report.Result.Value).Value);
    }

    [Fact]
    public async Task LinqIsAvailableWithTheDefaultImports()
    {
        RunReport report = await _harness.RunAsync("Enumerable.Range(1, 3).Select(x => x * x).ToList()");

        SequenceNode sequence = Assert.IsType<SequenceNode>(report.Result.Value);
        Assert.Equal(["1", "4", "9"], sequence.Items.Cast<ScalarNode>().Select(item => item.Value));
    }

    [Fact]
    public async Task ConsoleOutputIsCapturedAndDoesNotCorruptTheProtocol()
    {
        RunReport report = await _harness.RunAsync("Console.WriteLine(\"hello\"); Console.Error.WriteLine(\"oops\"); Console.Out.Flush(); 5");

        Assert.Equal(ExecutionOutcome.Success, report.Result.Outcome);
        Assert.Equal("hello" + Environment.NewLine, report.Stdout.Text);
        Assert.Equal("oops" + Environment.NewLine, report.Stderr.Text);
        Assert.Equal("5", Assert.IsType<ScalarNode>(report.Result.Value).Value);
    }

    [Fact]
    public async Task OutputAboveTheLimitIsCutWithItsOriginalLength()
    {
        _harness.Options = _harness.Options with { MaxOutputChars = 10 };

        RunReport report = await _harness.RunAsync("Console.Write(new string('x', 500)); 1");

        Assert.Equal(ExecutionOutcome.Success, report.Result.Outcome);
        Assert.Equal(new string('x', 10), report.Stdout.Text);
        Assert.Equal(500, report.Stdout.OriginalLength);
        Assert.False(report.Stderr.IsTruncated);
    }

    [Fact]
    public async Task ReportCarriesTheHostFactsAndTheStableWorkingDirectory()
    {
        RunReport report = await _harness.RunAsync("System.IO.Directory.GetCurrentDirectory()");

        Assert.NotNull(report.Host);
        Assert.Equal(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, report.Host.Runtime);
        Assert.False(string.IsNullOrWhiteSpace(report.Host.Language));
        Assert.Equal(Path.GetFullPath(_harness.WorkingDirectory), Path.GetFullPath(report.Host.WorkingDirectory));
        Assert.Equal(Path.GetFullPath(_harness.WorkingDirectory), Path.GetFullPath(Assert.IsType<TextNode>(report.Result.Value).Value));
    }

    [Fact]
    public async Task ReportKeepsTheSourceRevision()
    {
        RunReport report = await _harness.RunAsync(WorkerHarness.Request("1", revision: 9));

        Assert.Equal(9, report.SourceRevision);
    }

    [Fact]
    public async Task CompileErrorReportsTheLocationAndNeverRunsTheCode()
    {
        string marker = Path.Combine(_harness.WorkingDirectory, "ran.txt");
        string source = $"System.IO.File.WriteAllText(@\"{marker}\", \"ran\");\nint x = \"text\";";

        RunReport report = await _harness.RunAsync(source);

        Assert.Equal(ExecutionOutcome.CompilationFailed, report.Result.Outcome);
        Diagnostic error = Assert.Single(report.Result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("CS0029", error.Code);
        Assert.Equal(2, error.Location?.Line);
        Assert.Equal(9, error.Location?.Column);
        Assert.False(File.Exists(marker));
        Assert.Null(report.Result.Value);
    }

    [Fact]
    public async Task ThrownExceptionIsRuntimeFailedWithABoundedStackTrace()
    {
        string source = "int Down(int n) => n == 0 ? throw new InvalidOperationException(\"boom\") : Down(n - 1);\nDown(500)";

        RunReport report = await _harness.RunAsync(source);

        Assert.Equal(ExecutionOutcome.RuntimeFailed, report.Result.Outcome);
        Diagnostic error = Assert.Single(report.Result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("RuntimeException", error.Code);
        Assert.StartsWith("System.InvalidOperationException: boom", error.Message, StringComparison.Ordinal);
        Assert.Contains("Down", error.Message, StringComparison.Ordinal);
        Assert.True(error.Message.Length < 20_000, $"The message has {error.Message.Length} characters.");
    }

    [Fact]
    public async Task InfiniteLoopTimesOutAndItsProcessTreeIsTerminated()
    {
        string childFile = Path.Combine(_harness.WorkingDirectory, "child.pid");
        Directory.CreateDirectory(_harness.WorkingDirectory);
        string source = $$"""
            var info = OperatingSystem.IsWindows()
                ? new System.Diagnostics.ProcessStartInfo("ping", "-n 300 127.0.0.1")
                : new System.Diagnostics.ProcessStartInfo("sleep", "300");
            info.UseShellExecute = false;
            var child = System.Diagnostics.Process.Start(info)!;
            System.IO.File.WriteAllText(@"{{childFile}}", child.Id.ToString());
            while (true) { }
            """;
        Stopwatch clock = Stopwatch.StartNew();

        Task<RunReport> run = _harness.RunAsync(source, TimeSpan.FromSeconds(8));
        RunReport report = await run;
        clock.Stop();

        Assert.Equal(ExecutionOutcome.TimedOut, report.Result.Outcome);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"The run took {clock.Elapsed}.");
        Assert.True(File.Exists(childFile), "The script did not start its child process, so the test proves nothing.");
        int childId = int.Parse(await File.ReadAllTextAsync(childFile, TestContext.Current.CancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(await ProcessGoneAsync(childId), "The child process of the worker is still alive.");
    }

    [Fact]
    public async Task RunAsyncReturnsAPendingTaskSoTheCallerStaysResponsive()
    {
        Task<RunReport> run = _harness.RunAsync("await System.Threading.Tasks.Task.Delay(300); 1");

        Assert.False(run.IsCompleted);
        await run;
    }

    [Fact]
    public async Task CancelStopsTheRunAndReportsCanceled()
    {
        string started = Path.Combine(_harness.WorkingDirectory, "started.txt");
        Directory.CreateDirectory(_harness.WorkingDirectory);
        using CancellationTokenSource cancel = new CancellationTokenSource();
        Task<RunReport> run = _harness.RunCancelableAsync($"System.IO.File.WriteAllText(@\"{started}\", \"x\");\nwhile (true) {{ }}", cancel.Token);

        await WaitForFileAsync(started);
        await cancel.CancelAsync();
        RunReport report = await run;

        Assert.Equal(ExecutionOutcome.Canceled, report.Result.Outcome);
    }

    [Fact]
    public async Task CancelBeforeStartReportsCanceled()
    {
        using CancellationTokenSource cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        RunReport report = await _harness.RunCancelableAsync("1", cancel.Token);

        Assert.Equal(ExecutionOutcome.Canceled, report.Result.Outcome);
    }

    [Fact]
    public async Task NonZeroExitWithNoResponseIsWorkerCrashed()
    {
        RunReport report = await _harness.RunAsync("Environment.Exit(3);");

        AssertWorkerFailed(report, "WorkerCrashed");
    }

    [Fact]
    public async Task FailFastIsWorkerCrashed()
    {
        RunReport report = await _harness.RunAsync("Environment.FailFast(\"boom\");");

        AssertWorkerFailed(report, "WorkerCrashed");
    }

    [Fact]
    public async Task ExitWithCodeZeroAndNoResponseIsWorkerExitedEarly()
    {
        RunReport report = await _harness.RunAsync("Environment.Exit(0);");

        AssertWorkerFailed(report, "WorkerExitedEarly");
    }

    [Fact]
    public async Task TextOnTheProtocolStreamIsWorkerMalformedMessage()
    {
        // "dotnet --version" prints text and exits. It is a worker that does not speak the protocol.
        _harness.Options = _harness.Options with { LeadingArguments = ["--version"] };

        RunReport report = await _harness.RunAsync("1");

        AssertWorkerFailed(report, "WorkerMalformedMessage");
    }

    [Fact]
    public async Task ProcessThatPrintsNothingIsWorkerExitedEarly()
    {
        _harness.Options = _harness.Options with { LeadingArguments = [Path.Combine(_harness.WorkingDirectory, "missing.dll")] };
        Directory.CreateDirectory(_harness.WorkingDirectory);

        RunReport report = await _harness.RunAsync("1");

        Assert.Equal(ExecutionOutcome.WorkerFailed, report.Result.Outcome);
    }

    [Fact]
    public async Task MissingWorkerProgramIsWorkerStartFailed()
    {
        _harness.Options = _harness.Options with { FileName = Path.Combine(_harness.WorkingDirectory, "no-such-program") };

        RunReport report = await _harness.RunAsync("1");

        AssertWorkerFailed(report, "WorkerStartFailed");
    }

    [Fact]
    public async Task EachRunUsesAFreshProcess()
    {
        RunReport first = await _harness.RunAsync("Environment.ProcessId");
        RunReport second = await _harness.RunAsync("Environment.ProcessId");

        Assert.NotEqual(Assert.IsType<ScalarNode>(first.Result.Value).Value, Assert.IsType<ScalarNode>(second.Result.Value).Value);
    }

    [Fact]
    public async Task WorkerRejectsABadRequestWithTheBadRequestExitCode()
    {
        ProcessStartInfo info = new ProcessStartInfo(_harness.Options.FileName) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true };
        foreach (string argument in _harness.Options.LeadingArguments)
        {
            info.ArgumentList.Add(argument);
        }

        using Process worker = Process.Start(info)!;
        await worker.StandardInput.BaseStream.WriteAsync(new byte[] { 0x7F, 0x7F, 0x7F, 0x7F }, TestContext.Current.CancellationToken);
        worker.StandardInput.Close();
        await worker.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        Assert.Equal(Codepad.Contracts.WorkerProtocol.ExitBadRequest, worker.ExitCode);
    }

    [Fact]
    public void LocateFindsTheWorkerNextToTheApp()
    {
        WorkerLaunchOptions? options = WorkerLaunchOptions.Locate(AppContext.BaseDirectory, _harness.WorkingDirectory);

        Assert.NotNull(options);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Codepad.Worker.dll"), Assert.Single(options.LeadingArguments));
    }

    [Fact]
    public void LocateReturnsNullWhenTheFolderHasNoWorker()
    {
        Assert.Null(WorkerLaunchOptions.Locate(Path.GetTempPath(), _harness.WorkingDirectory));
    }

    private static void AssertWorkerFailed(RunReport report, string code)
    {
        Assert.Equal(ExecutionOutcome.WorkerFailed, report.Result.Outcome);
        Assert.Contains(report.Result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    private static async Task WaitForFileAsync(string path)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(60);
        while (!File.Exists(path))
        {
            Assert.True(DateTime.UtcNow < deadline, $"The file {path} did not appear.");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    private static async Task<bool> ProcessGoneAsync(int processId)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (!IsAlive(processId))
            {
                return true;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return !IsAlive(processId);
    }

    private static bool IsAlive(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return false;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }

        // A killed process can stay as a zombie until its parent reaps it. A zombie runs no code.
        string stat = $"/proc/{processId}/stat";
        if (OperatingSystem.IsLinux() && File.Exists(stat))
        {
            string text = File.ReadAllText(stat);
            return !text.Contains(") Z", StringComparison.Ordinal);
        }

        return true;
    }
}
