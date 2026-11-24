using Codepad.Application.Execution;
using Codepad.Application.Experiments;
using Codepad.Application.History;
using Codepad.Contracts;
using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Domain.Results;

namespace Codepad.Desktop.Tests.History;

internal sealed class FakeRunner : ICSharpRunner
{
    public List<ExecutionRequest> Requests { get; } = [];

    public Task? Gate { get; set; }

    public async Task<RunReport> RunAsync(ExecutionRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (Gate is not null)
        {
            await Gate;
        }

        return MemoryHistory.Report(request, ExecutionOutcome.Success);
    }
}

internal sealed class MemoryHistory : IRunHistoryRepository
{
    public static readonly DateTimeOffset Start = new DateTimeOffset(2026, 4, 5, 6, 7, 8, TimeSpan.Zero);

    private readonly List<RunRecord> records = [];

    public IReadOnlyList<RunRecord> Records => records;

    public string? FailWith { get; set; }

    public string? FailList { get; set; }

    public static RunReport Report(ExecutionRequest request, ExecutionOutcome outcome)
    {
        Diagnostic[] diagnostics = outcome switch
        {
            ExecutionOutcome.Success => [],
            ExecutionOutcome.TimedOut => [new Diagnostic(DiagnosticSeverity.Error, "TimedOut", "The run went over the time limit.", null)],
            _ => [new Diagnostic(DiagnosticSeverity.Error, "RuntimeException", "boom", null)],
        };
        return new RunReport(
            request.Id,
            request.ExperimentRevision,
            new ExecutionResult(outcome, diagnostics, outcome == ExecutionOutcome.Success ? new ScalarNode("System.Int32", "2") : null),
            CapturedText.Empty,
            CapturedText.Empty,
            null);
    }

    public RunRecord Seed(Experiment experiment, ExecutionOutcome outcome, int minutes)
    {
        ExecutionRequest request = ExecutionRequest.Snapshot(ExecutionId.New(), experiment, OperationPolicy.CSharpDefault, Start.AddMinutes(minutes));
        RunRecord record = new RunRecord(request, request.RequestedAt.AddMilliseconds(250), Report(request, outcome));
        records.Add(record);
        return record;
    }

    public void Remove(ExecutionId id) => records.RemoveAll(record => record.Request.Id == id);

    public Task AddAsync(RunRecord record, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null)
        {
            throw new ExperimentStorageException(FailWith);
        }

        records.Add(record);
        return Task.CompletedTask;
    }

    public Func<ExperimentId, Task>? BeforeList { get; set; }

    public Func<ExecutionId, Task>? BeforeGet { get; set; }

    public async Task<IReadOnlyList<RunSummary>> ListAsync(ExperimentId experimentId, CancellationToken cancellationToken = default)
    {
        if (BeforeList is not null)
        {
            await BeforeList(experimentId);
        }

        if (FailList is not null)
        {
            throw new ExperimentStorageException(FailList);
        }

        IReadOnlyList<RunSummary> list = records
            .Where(record => record.Request.ExperimentId == experimentId)
            .OrderByDescending(record => record.CompletedAt)
            .Select(record => new RunSummary(record.Request.Id, record.Request.ExperimentId, record.Request.ExperimentRevision, record.Request.RequestedAt, record.CompletedAt, record.Report.Result.Outcome))
            .ToList();
        return list;
    }

    public async Task<RunRecord?> GetAsync(ExecutionId id, CancellationToken cancellationToken = default)
    {
        if (BeforeGet is not null)
        {
            await BeforeGet(id);
        }

        return records.FirstOrDefault(record => record.Request.Id == id);
    }
}
