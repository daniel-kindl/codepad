using System.ComponentModel;
using System.Diagnostics;
using Codepad.Application.Execution;
using Codepad.Contracts;
using Codepad.Domain.Executions;
using Codepad.Domain.Operations;
using Codepad.Domain.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Codepad.Infrastructure.Execution;

/// <summary>
/// Runs each C# snapshot in a new worker process and supervises it. The supervisor enforces the time limit and the
/// cancel token, stops the whole process tree, and limits the size of each message. It turns every end of a run into an
/// outcome. A process exit code is never an outcome. See ARCHITECTURE.md, "Worker trust boundary".
/// </summary>
/// <param name="options">How to start the worker.</param>
/// <param name="logger">The logger, or null for no log events.</param>
public sealed partial class WorkerSupervisor(WorkerLaunchOptions options, ILogger<WorkerSupervisor>? logger = null) : ICSharpRunner
{
    private static readonly TimeSpan s_stopWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_pipeGrace = TimeSpan.FromSeconds(1);

    private readonly WorkerLaunchOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger _logger = logger ?? NullLogger<WorkerSupervisor>.Instance;

    /// <inheritdoc />
    public async Task<RunReport> RunAsync(ExecutionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Policy.Kind != OperationKind.CSharp)
        {
            throw new ArgumentException($"The policy is for {request.Policy.Kind}. The worker runs only C#.", nameof(request));
        }

        LogRunStarted(_logger, request.Id.Value, request.Policy.TimeLimit.TotalSeconds);
        Stopwatch clock = Stopwatch.StartNew();
        RunReport report = await SuperviseAsync(request, cancellationToken).ConfigureAwait(false);
        LogRunCompleted(_logger, request.Id.Value, report.Result.Outcome, clock.ElapsedMilliseconds);
        return report;
    }

    private async Task<RunReport> SuperviseAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            LogStopped(_logger, request.Id.Value, "canceled");
            return Failed(request, ExecutionOutcome.Canceled, null, null);
        }

        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(request.Policy.TimeLimit);
        Process? process = null;
        try
        {
            process = StartWorker();
            if (process is null)
            {
                return Failed(request, ExecutionOutcome.WorkerFailed, "WorkerStartFailed", "The worker process did not start.");
            }

            return await ExchangeAsync(request, process, limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested)
        {
            // A cancel from the caller wins over a time limit that ends at the same time.
            LogStopped(_logger, request.Id.Value, cancellationToken.IsCancellationRequested ? "canceled" : "time_limit");
            return cancellationToken.IsCancellationRequested
                ? Failed(request, ExecutionOutcome.Canceled, null, null)
                : Failed(request, ExecutionOutcome.TimedOut, "TimeLimitExceeded", $"The run did not finish in {request.Policy.TimeLimit.TotalSeconds:0.###} s. The worker was stopped.");
        }
        finally
        {
            if (process is not null)
            {
                await StopAsync(process).ConfigureAwait(false);
                process.Dispose();
            }
        }
    }

    private async Task<RunReport> ExchangeAsync(ExecutionRequest request, Process process, CancellationToken token)
    {
        // Drain the standard error of the worker. A full pipe would block the worker. The text is not used.
        _ = DrainAsync(process.StandardError.BaseStream);

        WorkerRunRequest message = new WorkerRunRequest(
            WorkerProtocol.Version,
            request.Id.Value,
            request.ExperimentRevision,
            request.Source,
            request.Policy.Results,
            _options.MaxOutputChars);

        try
        {
            await FrameCodec.WriteAsync(process.StandardInput.BaseStream, message, WorkerProtocol.MaxMessageBytes, token).ConfigureAwait(false);
        }
        catch (FrameException)
        {
            // The request is larger than the protocol allows. The worker never gets it.
            return Failed(request, ExecutionOutcome.WorkerFailed, "WorkerRequestTooLarge", "The source is too large to send to the worker.");
        }
        catch (IOException)
        {
            // The worker closed its input. It may have written a response or exited. The read below finds out.
        }

        try
        {
            WorkerRunResponse? response = await ReadResponseAsync(process, token).ConfigureAwait(false);
            if (response is null)
            {
                return await NoResponseAsync(request, process).ConfigureAwait(false);
            }

            return Accept(request, response);
        }
        catch (FrameException exception)
        {
            return Failed(request, ExecutionOutcome.WorkerFailed, "WorkerMalformedMessage", $"The worker sent a message that CodePad cannot use ({exception.Kind}).");
        }
    }

    private static async Task<WorkerRunResponse?> ReadResponseAsync(Process process, CancellationToken token)
    {
        Task<WorkerRunResponse?> read = FrameCodec.ReadAsync<WorkerRunResponse>(process.StandardOutput.BaseStream, WorkerProtocol.MaxMessageBytes, token);
        Task exited = process.WaitForExitAsync(token);
        await Task.WhenAny(read, exited).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();

        if (!read.IsCompleted)
        {
            // The worker exited first. A child that inherited the pipe can keep it open, so wait only a short time.
            Task grace = Task.Delay(s_pipeGrace, token);
            await Task.WhenAny(read, grace).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!read.IsCompleted)
            {
                _ = read.ContinueWith(static task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                return null;
            }
        }

        return await read.ConfigureAwait(false);
    }

    private async Task<RunReport> NoResponseAsync(ExecutionRequest request, Process process)
    {
        // The worker has no result to send. Stop it, so that its exit code is final.
        await StopAsync(process).ConfigureAwait(false);
        int exitCode = process.HasExited ? process.ExitCode : -1;
        LogWorkerFailed(_logger, request.Id.Value, exitCode);

        return exitCode == 0
            ? Failed(request, ExecutionOutcome.WorkerFailed, "WorkerExitedEarly", "The worker exited with code 0 before it sent a result.")
            : Failed(request, ExecutionOutcome.WorkerFailed, "WorkerCrashed", $"The worker stopped with exit code {exitCode} before it sent a result.");
    }

    private RunReport Accept(ExecutionRequest request, WorkerRunResponse response)
    {
        try
        {
            if (response.ProtocolVersion != WorkerProtocol.Version)
            {
                return Failed(request, ExecutionOutcome.WorkerFailed, "WorkerProtocolMismatch", $"The worker speaks protocol {response.ProtocolVersion}. CodePad speaks {WorkerProtocol.Version}.");
            }

            Validate(request, response, _options.MaxOutputChars);
            ExecutionResult result = new ExecutionResult(response.Outcome, response.Diagnostics, response.Value);
            return new RunReport(request.Id, request.ExperimentRevision, result, response.Stdout, response.Stderr, response.Host);
        }
        catch (ArgumentException exception)
        {
            LogMalformedResponse(_logger, request.Id.Value, exception.GetType().Name);
            return Failed(request, ExecutionOutcome.WorkerFailed, "WorkerMalformedMessage", "The worker sent a result that breaks the protocol rules.");
        }
    }

    /// <summary>Checks that a response belongs to the request and respects the limits. Throws <see cref="ArgumentException"/> if not.</summary>
    internal static void Validate(ExecutionRequest request, WorkerRunResponse response, int maxOutputChars)
    {
        if (response.ExecutionId != request.Id.Value || response.SourceRevision != request.ExperimentRevision)
        {
            throw new ArgumentException("The response is for another execution or revision.");
        }

        if (response.Outcome is not (ExecutionOutcome.Success or ExecutionOutcome.CompilationFailed or ExecutionOutcome.RuntimeFailed))
        {
            throw new ArgumentException($"A worker cannot report the outcome {response.Outcome}.");
        }

        if (response.Diagnostics is null || response.Stdout is null || response.Stderr is null || response.Host is null)
        {
            throw new ArgumentException("The response lacks a required part.");
        }

        foreach (CapturedText text in new[] { response.Stdout, response.Stderr })
        {
            if (text.Text is null || text.Text.Length > maxOutputChars || (text.OriginalLength is int original && original <= text.Text.Length))
            {
                throw new ArgumentException("The captured text breaks the output limit.");
            }
        }

        if (response.Value is not null && request.Policy.Results.FindExceededLimit(response.Value) is not null)
        {
            throw new ArgumentException("The value is larger than the result policy allows.");
        }
    }

    private Process? StartWorker()
    {
        ProcessStartInfo info = new ProcessStartInfo(_options.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _options.WorkingDirectory,
        };

        foreach (string argument in _options.LeadingArguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            Directory.CreateDirectory(_options.WorkingDirectory);
            return Process.Start(info);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LogStartFailed(_logger, exception);
            return null;
        }
    }

    private static async Task StopAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The process ended on its own. There is nothing to stop.
        }

        try
        {
            await process.WaitForExitAsync().WaitAsync(s_stopWait).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The system did not report the exit in time. The kill request is sent. Do not block the caller.
        }
    }

    private static async Task DrainAsync(Stream stream)
    {
        try
        {
            await stream.CopyToAsync(Stream.Null).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The worker is gone. There is nothing left to drain.
        }
    }

    private static RunReport Failed(ExecutionRequest request, ExecutionOutcome outcome, string? code, string? message)
    {
        List<Diagnostic> diagnostics = [];
        if (message is not null)
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, code, message, null));
        }

        return new RunReport(
            request.Id,
            request.ExperimentRevision,
            new ExecutionResult(outcome, diagnostics, null),
            CapturedText.Empty,
            CapturedText.Empty,
            null);
    }

    [LoggerMessage(EventId = 1, EventName = "run.completed", Level = LogLevel.Information, Message = "C# run {ExecutionId} ended with {Outcome} after {ElapsedMs} ms.")]
    private static partial void LogRunCompleted(ILogger logger, Guid executionId, ExecutionOutcome outcome, long elapsedMs);

    [LoggerMessage(EventId = 2, EventName = "run.worker_failed", Level = LogLevel.Warning, Message = "The worker of run {ExecutionId} ended with exit code {ExitCode} and no result.")]
    private static partial void LogWorkerFailed(ILogger logger, Guid executionId, int exitCode);

    [LoggerMessage(EventId = 3, EventName = "run.malformed_response", Level = LogLevel.Warning, Message = "The worker of run {ExecutionId} sent a result that breaks the protocol rules ({Reason}).")]
    private static partial void LogMalformedResponse(ILogger logger, Guid executionId, string reason);

    [LoggerMessage(EventId = 4, EventName = "run.start_failed", Level = LogLevel.Error, Message = "The worker process did not start.")]
    private static partial void LogStartFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5, EventName = "run.started", Level = LogLevel.Information, Message = "C# run {ExecutionId} started with a time limit of {TimeLimitSeconds} s.")]
    private static partial void LogRunStarted(ILogger logger, Guid executionId, double timeLimitSeconds);

    [LoggerMessage(EventId = 6, EventName = "run.stopped", Level = LogLevel.Warning, Message = "C# run {ExecutionId} was stopped. Reason: {Reason}.")]
    private static partial void LogStopped(ILogger logger, Guid executionId, string reason);
}
