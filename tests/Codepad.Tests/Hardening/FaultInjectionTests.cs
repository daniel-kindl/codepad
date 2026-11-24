using System.Diagnostics;
using Codepad.Application.Execution;
using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Infrastructure.Execution;
using Microsoft.Extensions.Logging;

namespace Codepad.Tests.Hardening;

/// <summary>
/// Runs the supervisor against a worker that breaks the protocol in one chosen way. Every failure must come back as a
/// report with the right outcome and code. None may throw, hang the caller, leave a process behind or log the source.
/// </summary>
public sealed class FaultInjectionTests : IDisposable
{
    private const string SECRET = "SECRET_SOURCE_MARKER_8841";

    private static readonly DateTimeOffset s_created = new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);

    private readonly string workingDirectory = Path.Combine(Path.GetTempPath(), "codepad-fault-" + Guid.NewGuid().ToString("N"));
    private readonly CapturingLogger logger = new CapturingLogger();

    public FaultInjectionTests()
    {
        Directory.CreateDirectory(workingDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A child process can still hold the folder for a moment on Windows. The folder is temporary.
        }
    }

    private string PidFile => Path.Combine(workingDirectory, "worker.pid");

    private string ChildFile => Path.Combine(workingDirectory, "child.pid");

    private WorkerSupervisor Supervisor(string mode)
    {
        string assembly = Path.Combine(AppContext.BaseDirectory, "Codepad.FaultWorker.dll");
        WorkerLaunchOptions options = WorkerLaunchOptions.ForWorkerAssembly(assembly, workingDirectory) with { LeadingArguments = [assembly, mode, PidFile, ChildFile] };
        return new WorkerSupervisor(options, logger);
    }

    private static ExecutionRequest Request(TimeSpan? limit = null)
    {
        OperationPolicy policy = new OperationPolicy(OperationKind.CSharp, 1, OperationTrigger.ExplicitOnly, limit ?? TimeSpan.FromSeconds(60), Codepad.Domain.Operations.ResultPolicy.Default);
        Experiment experiment = Experiment.Restore(ExperimentId.New(), OperationKind.CSharp, "t", SECRET, string.Empty, s_created, s_created, 3);
        return ExecutionRequest.Snapshot(ExecutionId.New(), experiment, policy, s_created);
    }

    private async Task<RunReport> RunAsync(string mode, TimeSpan? limit = null, CancellationToken? cancellationToken = null)
    {
        ExecutionRequest request = Request(limit);
        RunReport report = await Supervisor(mode).RunAsync(request, cancellationToken ?? TestContext.Current.CancellationToken);

        // The report always belongs to the request, whatever the worker did.
        Assert.Equal(request.Id, report.ExecutionId);
        Assert.Equal(request.ExperimentRevision, report.SourceRevision);
        return report;
    }

    private static void AssertFailed(RunReport report, ExecutionOutcome outcome, string code)
    {
        Assert.True(report.Result.Outcome == outcome, $"Outcome {report.Result.Outcome}: {string.Join(" | ", report.Result.Diagnostics.Select(item => item.Code + " " + item.Message))}");
        Assert.Contains(report.Result.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Null(report.Result.Value);
    }

    [Fact]
    public async Task TheBaselineWorkerSucceeds()
    {
        RunReport report = await RunAsync("ok");

        Assert.Equal(ExecutionOutcome.Success, report.Result.Outcome);
    }

    [Theory]
    [InlineData("oversized-prefix")]
    [InlineData("negative-prefix")]
    [InlineData("partial-prefix")]
    [InlineData("truncated-payload")]
    [InlineData("invalid-json")]
    [InlineData("json-null")]
    public async Task ABrokenFrameIsWorkerMalformedMessageAndNeverASuccess(string mode)
    {
        RunReport report = await RunAsync(mode, TimeSpan.FromSeconds(30));

        AssertFailed(report, ExecutionOutcome.WorkerFailed, "WorkerMalformedMessage");
    }

    [Theory]
    [InlineData("wrong-id")]
    [InlineData("wrong-revision")]
    [InlineData("bad-outcome")]
    [InlineData("deep-value")]
    [InlineData("huge-output")]
    [InlineData("success-without-value-but-error")]
    public async Task AValidFrameWithAnImpossibleContentIsWorkerMalformedMessage(string mode)
    {
        RunReport report = await RunAsync(mode);

        AssertFailed(report, ExecutionOutcome.WorkerFailed, "WorkerMalformedMessage");
    }

    [Fact]
    public async Task AnotherProtocolVersionIsWorkerProtocolMismatch()
    {
        RunReport report = await RunAsync("wrong-version");

        AssertFailed(report, ExecutionOutcome.WorkerFailed, "WorkerProtocolMismatch");
    }

    [Fact]
    public async Task ACrashIsWorkerCrashedWithTheExitCodeAndNoException()
    {
        RunReport report = await RunAsync("crash");

        AssertFailed(report, ExecutionOutcome.WorkerFailed, "WorkerCrashed");
        Assert.Contains("exit code", report.Result.Diagnostics.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHangingWorkerTimesOutAndIsGone()
    {
        Stopwatch clock = Stopwatch.StartNew();

        RunReport report = await RunAsync("hang", TimeSpan.FromSeconds(2));

        clock.Stop();
        AssertFailed(report, ExecutionOutcome.TimedOut, "TimeLimitExceeded");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(25), $"The run took {clock.Elapsed}.");
        Assert.True(await ProcessGoneAsync(ReadId(PidFile)), "The worker is still alive.");
    }

    [Fact]
    public async Task AHangingWorkerWithAChildLeavesNoProcessAfterTheTimeout()
    {
        RunReport report = await RunAsync("hang-with-child", TimeSpan.FromSeconds(3));

        AssertFailed(report, ExecutionOutcome.TimedOut, "TimeLimitExceeded");
        Assert.True(File.Exists(ChildFile), "The worker did not start its child, so the test proves nothing.");
        Assert.True(await ProcessGoneAsync(ReadId(PidFile)), "The worker is still alive.");
        Assert.True(await ProcessGoneAsync(ReadId(ChildFile)), "The child of the worker is still alive.");
    }

    [Fact]
    public async Task ASlowResponseThatMissesTheLimitIsTimedOutAndALateOneIsNeverUsed()
    {
        RunReport report = await RunAsync("slow", TimeSpan.FromSeconds(1));

        AssertFailed(report, ExecutionOutcome.TimedOut, "TimeLimitExceeded");
    }

    [Fact]
    public async Task CancelStopsAHangingWorkerWithTheRightOutcomeAndNoProcessLeft()
    {
        using CancellationTokenSource cancel = new CancellationTokenSource();
        Task<RunReport> run = RunAsync("hang", TimeSpan.FromSeconds(60), cancel.Token);
        await WaitForFileAsync(PidFile);

        await cancel.CancelAsync();
        RunReport report = await run;

        Assert.Equal(ExecutionOutcome.Canceled, report.Result.Outcome);
        Assert.True(await ProcessGoneAsync(ReadId(PidFile)), "The worker is still alive.");
    }

    [Fact]
    public async Task AnErrorStreamFloodDoesNotBlockTheRun()
    {
        Stopwatch clock = Stopwatch.StartNew();

        RunReport report = await RunAsync("stderr-flood", TimeSpan.FromSeconds(30));

        clock.Stop();
        Assert.Equal(ExecutionOutcome.Success, report.Result.Outcome);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(25), $"The run took {clock.Elapsed}.");
    }

    [Fact]
    public async Task TheCallerStaysResponsiveWhileAWorkerHangs()
    {
        Task<RunReport> run = RunAsync("hang", TimeSpan.FromSeconds(3));

        // RunAsync did not block this thread. The caller can do other work while the worker hangs.
        Assert.False(run.IsCompleted);
        int work = 0;
        while (!run.IsCompleted && work < 5)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            work++;
        }

        Assert.Equal(5, work);
        await run;
    }

    [Theory]
    [InlineData("ok", "run.completed")]
    [InlineData("crash", "run.worker_failed")]
    [InlineData("wrong-id", "run.malformed_response")]
    public async Task EachRunLogsItsStartAndItsEndWithTheOperationIdAndNeverTheSource(string mode, string failureEvent)
    {
        ExecutionRequest request = Request();

        await Supervisor(mode).RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Contains(logger.Entries, entry => entry.EventName == "run.started" && entry.Text.Contains(request.Id.Value.ToString(), StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.EventName == failureEvent);
        LogEntry completed = Assert.Single(logger.Entries, entry => entry.EventName == "run.completed");
        Assert.Contains(request.Id.Value.ToString(), completed.Text, StringComparison.Ordinal);
        Assert.Matches(@"after \d+ ms", completed.Text);
        Assert.DoesNotContain(logger.Entries, entry => entry.Text.Contains(SECRET, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry => entry.Exception?.ToString().Contains(SECRET, StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task ATimeoutAndACancelLogTheirReason()
    {
        ExecutionRequest request = Request(TimeSpan.FromSeconds(1));
        await Supervisor("hang").RunAsync(request, TestContext.Current.CancellationToken);
        Assert.Contains(logger.Entries, entry => entry.EventName == "run.stopped" && entry.Text.Contains("time_limit", StringComparison.Ordinal));

        logger.Entries.Clear();
        using CancellationTokenSource cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        await Supervisor("hang").RunAsync(Request(), cancel.Token);
        Assert.Contains(logger.Entries, entry => entry.EventName == "run.stopped" && entry.Text.Contains("canceled", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry => entry.Text.Contains(SECRET, StringComparison.Ordinal));
    }

    private static int ReadId(string file) => int.Parse(File.ReadAllText(file), System.Globalization.CultureInfo.InvariantCulture);

    private static async Task WaitForFileAsync(string file)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!File.Exists(file) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(file), "The worker did not start.");
        await Task.Delay(100, TestContext.Current.CancellationToken);
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
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private sealed record LogEntry(string EventName, LogLevel Level, string Text, Exception? Exception);

    private sealed class CapturingLogger : ILogger<WorkerSupervisor>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add(new LogEntry(eventId.Name ?? string.Empty, logLevel, formatter(state, exception), exception));
            }
        }
    }
}
