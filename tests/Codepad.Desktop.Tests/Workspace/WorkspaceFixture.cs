using Codepad.Application.Editing;
using Codepad.Application.Experiments;
using Codepad.Application.Json;
using Codepad.Application.Workspace;
using Codepad.Desktop.Editor;
using Codepad.Desktop.Json;
using Codepad.Desktop.Workspace;
using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;
using Codepad.Tests.Experiments;

namespace Codepad.Desktop.Tests.Workspace;

/// <summary>A workspace over in-memory storage and a manual clock.</summary>
internal sealed class WorkspaceFixture
{
    private readonly Queue<Action> ui = new Queue<Action>();

    public WorkspaceFixture(Func<WorkspaceFixture, RunController>? createRuns = null, Action<string>? reportSaveFailure = null)
    {
        Session = new SessionStore(Preferences);
        Model = new WorkspaceViewModel(
            Experiments,
            Session,
            Time,
            () => new CSharpEditorViewModel(new NoHighlighter(), new NoAnalyzer(), Time, TimeSpan.FromMilliseconds(250), Post),
            () => new JsonToolViewModel(new JsonEvaluator(), Time, TimeSpan.FromMilliseconds(250), Post),
            Post,
            Failures.Add,
            createRuns?.Invoke(this),
            reportSaveFailure);
    }

    public ManualTimeProvider Time { get; } = new ManualTimeProvider();

    public FakeExperimentRepository Experiments { get; } = new FakeExperimentRepository();

    public MemoryPreferences Preferences { get; } = new MemoryPreferences();

    public SessionStore Session { get; }

    public List<string> Failures { get; } = [];

    public WorkspaceViewModel Model { get; }

    public void Post(Action action)
    {
        lock (ui)
        {
            ui.Enqueue(action);
        }
    }

    public void RunUi()
    {
        while (true)
        {
            Action? next;
            lock (ui)
            {
                next = ui.Count > 0 ? ui.Dequeue() : null;
            }

            if (next is null)
            {
                return;
            }

            next();
        }
    }

    public Experiment Seed(OperationKind kind, string title, string source)
    {
        Experiment experiment = Experiment.Create(ExperimentId.New(), kind, title, source, Time.GetUtcNow());
        Experiments.Seed(experiment);
        return experiment;
    }

    private sealed class NoHighlighter : ICSharpHighlighter
    {
        public IReadOnlyList<HighlightSpan> Highlight(string source) => [];
    }

    private sealed class NoAnalyzer : ICSharpAnalyzer
    {
        public Task<EditorAnalysis> AnalyzeAsync(string source, CancellationToken cancellationToken) => Task.FromResult(EditorAnalysis.Empty);
    }
}

internal sealed class MemoryPreferences : IPreferenceRepository
{
    public Dictionary<string, string> Values { get; } = [];

    public Exception? Failure { get; set; }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(Values.GetValueOrDefault(key));

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        Values[key] = value;
        return Task.CompletedTask;
    }
}
