using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Domain.Results;

namespace Codepad.Tests.Domain;

public sealed class ExecutionTests
{
    private static readonly DateTimeOffset s_requested = new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset s_started = s_requested.AddMilliseconds(50);
    private static readonly DateTimeOffset s_completed = s_requested.AddSeconds(1);

    private static readonly ExecutionResult s_success = new ExecutionResult(ExecutionOutcome.Success, [], new ScalarNode("System.Int32", "3"));
    private static readonly ExecutionResult s_canceled = new ExecutionResult(ExecutionOutcome.Canceled, [], null);

    [Fact]
    public void NewExecutionIsPending()
    {
        ExecutionRequest request = NewRequest();

        Execution execution = Execution.Create(request);

        Assert.Equal(ExecutionStatus.Pending, execution.Status);
        Assert.Same(request, execution.Request);
        Assert.Equal(request.Id, execution.Id);
        Assert.Null(execution.StartedAt);
        Assert.Null(execution.CompletedAt);
        Assert.Null(execution.Result);
    }

    [Fact]
    public void StartMovesPendingToRunning()
    {
        Execution pending = Execution.Create(NewRequest());

        Execution running = pending.Start(s_started);

        Assert.Equal(ExecutionStatus.Running, running.Status);
        Assert.Equal(s_started, running.StartedAt);
        Assert.Equal(ExecutionStatus.Pending, pending.Status);
    }

    [Fact]
    public void CompleteMovesRunningToCompletedWithTheResult()
    {
        Execution running = Execution.Create(NewRequest()).Start(s_started);

        Execution completed = running.Complete(s_success, s_completed);

        Assert.Equal(ExecutionStatus.Completed, completed.Status);
        Assert.Same(s_success, completed.Result);
        Assert.Equal(s_started, completed.StartedAt);
        Assert.Equal(s_completed, completed.CompletedAt);
        Assert.Equal(ExecutionStatus.Running, running.Status);
    }

    [Fact]
    public void PendingCanCompleteAsCanceledWithNoStart()
    {
        Execution completed = Execution.Create(NewRequest()).Complete(s_canceled, s_completed);

        Assert.Equal(ExecutionStatus.Completed, completed.Status);
        Assert.Equal(ExecutionOutcome.Canceled, completed.Result!.Outcome);
        Assert.Null(completed.StartedAt);
    }

    [Theory]
    [InlineData(ExecutionOutcome.Success)]
    [InlineData(ExecutionOutcome.CompilationFailed)]
    [InlineData(ExecutionOutcome.RuntimeFailed)]
    [InlineData(ExecutionOutcome.TimedOut)]
    [InlineData(ExecutionOutcome.WorkerFailed)]
    public void PendingCannotCompleteWithAnOutcomeOtherThanCanceled(ExecutionOutcome outcome)
    {
        Execution pending = Execution.Create(NewRequest());

        Assert.Throws<InvalidOperationException>(() => pending.Complete(ResultFor(outcome), s_completed));
    }

    [Fact]
    public void RunningCannotStartAgain()
    {
        Execution running = Execution.Create(NewRequest()).Start(s_started);

        Assert.Throws<InvalidOperationException>(() => running.Start(s_started));
    }

    [Fact]
    public void CompletedIsFinal()
    {
        Execution completed = Execution.Create(NewRequest()).Start(s_started).Complete(s_success, s_completed);

        Assert.Throws<InvalidOperationException>(() => completed.Start(s_started));
        Assert.Throws<InvalidOperationException>(() => completed.Complete(s_canceled, s_completed));
    }

    [Fact]
    public void CompleteRejectsANullResult()
    {
        Execution running = Execution.Create(NewRequest()).Start(s_started);

        Assert.Throws<ArgumentNullException>(() => running.Complete(null!, s_completed));
    }

    [Fact]
    public void CreateRejectsANullRequest()
    {
        Assert.Throws<ArgumentNullException>(() => Execution.Create(null!));
    }

    [Fact]
    public void ResultHoldsAValueOnlyForSuccess()
    {
        ResultNode value = new ScalarNode("System.Int32", "3");

        Assert.Throws<ArgumentException>(() => new ExecutionResult(ExecutionOutcome.RuntimeFailed, [], value));
        Assert.Throws<ArgumentException>(() => new ExecutionResult(ExecutionOutcome.TimedOut, [], value));
    }

    [Fact]
    public void CompilationFailedNeedsAnErrorDiagnostic()
    {
        Diagnostic warning = new Diagnostic(DiagnosticSeverity.Warning, "CS0219", "Unused variable.", new SourceLocation(1, 5));

        Assert.Throws<ArgumentException>(() => new ExecutionResult(ExecutionOutcome.CompilationFailed, [], null));
        Assert.Throws<ArgumentException>(() => new ExecutionResult(ExecutionOutcome.CompilationFailed, [warning], null));
    }

    [Fact]
    public void SuccessCannotHaveAnErrorDiagnostic()
    {
        Diagnostic error = new Diagnostic(DiagnosticSeverity.Error, "CS0103", "Name not found.", new SourceLocation(1, 1));

        Assert.Throws<ArgumentException>(() => new ExecutionResult(ExecutionOutcome.Success, [error], null));
    }

    [Fact]
    public void ResultRejectsUndefinedOutcomeAndNullDiagnostics()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionResult((ExecutionOutcome)42, [], null));
        Assert.Throws<ArgumentNullException>(() => new ExecutionResult(ExecutionOutcome.Canceled, null!, null));
        Assert.Throws<ArgumentException>(() => new ExecutionResult(ExecutionOutcome.Canceled, [null!], null));
    }

    [Fact]
    public void ResultKeepsItsOwnCopyOfTheDiagnostics()
    {
        Diagnostic error = new Diagnostic(DiagnosticSeverity.Error, "CS1002", "; expected.", new SourceLocation(2, 7));
        List<Diagnostic> diagnostics = [error];
        ExecutionResult result = new ExecutionResult(ExecutionOutcome.CompilationFailed, diagnostics, null);

        diagnostics.Clear();

        Assert.Equal([error], result.Diagnostics);
    }

    private static ExecutionRequest NewRequest()
    {
        Experiment experiment = Experiment.Create(ExperimentId.New(), OperationKind.CSharp, "Sum", "1 + 2", s_requested);
        return ExecutionRequest.Snapshot(ExecutionId.New(), experiment, OperationPolicy.CSharpDefault, s_requested);
    }

    private static ExecutionResult ResultFor(ExecutionOutcome outcome)
    {
        Diagnostic error = new Diagnostic(DiagnosticSeverity.Error, "CS1002", "; expected.", new SourceLocation(1, 6));
        return outcome == ExecutionOutcome.CompilationFailed
            ? new ExecutionResult(outcome, [error], null)
            : new ExecutionResult(outcome, [], null);
    }
}
