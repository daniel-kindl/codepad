using Codepad.Application.Execution;
using Codepad.Application.History;
using Codepad.Contracts;
using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Domain.Results;

namespace Codepad.Tests.History;

/// <summary>Builders for run records that the history tests share.</summary>
internal static class RunSamples
{
    public static readonly DateTimeOffset Start = new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.FromHours(2)).AddTicks(1234);

    public static readonly WorkerHostInfo Host = new WorkerHostInfo(".NET 11", "Linux", "X64", "1.2.3", "C# 15", "/work");

    public static Experiment Experiment(string source = "1 + 1") =>
        Codepad.Domain.Experiments.Experiment.Create(ExperimentId.New(), OperationKind.CSharp, "Scratch", source, Start);

    public static RunRecord Record(Experiment experiment, ExecutionOutcome outcome = ExecutionOutcome.Success, int minutes = 0, ResultNode? value = null)
    {
        ExecutionRequest request = ExecutionRequest.Snapshot(ExecutionId.New(), experiment, OperationPolicy.CSharpDefault, Start.AddMinutes(minutes));
        return new RunRecord(request, request.RequestedAt.AddMilliseconds(250), Report(request, outcome, value));
    }

    public static RunReport Report(ExecutionRequest request, ExecutionOutcome outcome = ExecutionOutcome.Success, ResultNode? value = null)
    {
        Diagnostic[] diagnostics = outcome switch
        {
            ExecutionOutcome.CompilationFailed => [new Diagnostic(DiagnosticSeverity.Error, "CS1002", "; expected", new SourceLocation(1, 6))],
            ExecutionOutcome.RuntimeFailed => [new Diagnostic(DiagnosticSeverity.Error, "RuntimeException", "System.InvalidOperationException: boom", null)],
            ExecutionOutcome.TimedOut => [new Diagnostic(DiagnosticSeverity.Error, "TimedOut", "The run went over the time limit.", null)],
            ExecutionOutcome.Canceled => [new Diagnostic(DiagnosticSeverity.Info, "Canceled", "The run was stopped.", null)],
            ExecutionOutcome.WorkerFailed => [new Diagnostic(DiagnosticSeverity.Error, "WorkerFailed", "The worker exited early.", null)],
            _ => [new Diagnostic(DiagnosticSeverity.Warning, "CS0219", "unused", new SourceLocation(2, 3))],
        };
        bool answered = outcome is ExecutionOutcome.Success or ExecutionOutcome.CompilationFailed or ExecutionOutcome.RuntimeFailed;
        return new RunReport(
            request.Id,
            request.ExperimentRevision,
            new ExecutionResult(outcome, diagnostics, outcome == ExecutionOutcome.Success ? value ?? new ScalarNode("System.Int32", "2") : null),
            answered ? new CapturedText("out \U0001F600\r\n", 99) : CapturedText.Empty,
            answered ? new CapturedText("err", null) : CapturedText.Empty,
            answered ? Host : null);
    }
}
