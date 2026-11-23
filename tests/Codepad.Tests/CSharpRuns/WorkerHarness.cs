using Codepad.Application.Execution;
using Codepad.Domain.Executions;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Infrastructure.Execution;

namespace Codepad.Tests.CSharpRuns;

/// <summary>Runs C# source in the real worker process. Each harness has its own working directory.</summary>
public sealed class WorkerHarness : IDisposable
{
    private static readonly DateTimeOffset s_created = new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);

    public WorkerHarness()
    {
        WorkingDirectory = Path.Combine(Path.GetTempPath(), "codepad-tests-" + Guid.NewGuid().ToString("N"));
        Options = WorkerLaunchOptions.ForWorkerAssembly(Path.Combine(AppContext.BaseDirectory, "Codepad.Worker.dll"), WorkingDirectory);
    }

    public string WorkingDirectory { get; }

    public WorkerLaunchOptions Options { get; set; }

    public ICSharpRunner Runner => new WorkerSupervisor(Options);

    public static ExecutionRequest Request(string source, TimeSpan? timeLimit = null, long revision = 1)
    {
        TimeSpan limit = timeLimit ?? TimeSpan.FromSeconds(60);
        OperationPolicy policy = new OperationPolicy(OperationKind.CSharp, 1, OperationTrigger.ExplicitOnly, limit, ResultPolicy.Default);
        Experiment experiment = Experiment.Restore(ExperimentId.New(), OperationKind.CSharp, "t", source, string.Empty, s_created, s_created, revision);
        return ExecutionRequest.Snapshot(ExecutionId.New(), experiment, policy, s_created);
    }

    public Task<RunReport> RunAsync(string source, TimeSpan? timeLimit = null)
    {
        return Runner.RunAsync(Request(source, timeLimit), TestContext.Current.CancellationToken);
    }

    public Task<RunReport> RunAsync(ExecutionRequest request)
    {
        return Runner.RunAsync(request, TestContext.Current.CancellationToken);
    }

    public Task<RunReport> RunCancelableAsync(string source, CancellationToken cancellationToken)
    {
        return Runner.RunAsync(Request(source), cancellationToken);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(WorkingDirectory))
            {
                Directory.Delete(WorkingDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A worker that is still stopping can hold the folder. A temp folder is not worth a failed test.
        }
    }
}
