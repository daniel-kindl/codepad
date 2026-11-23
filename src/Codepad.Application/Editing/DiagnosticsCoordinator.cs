namespace Codepad.Application.Editing;

/// <summary>
/// Analyzes the source of one open document in the background. It waits for a pause in the edits, then analyzes
/// the newest text once. A newer edit cancels the running analysis, and the coordinator drops its result.
/// It never raises <see cref="Published"/> for an old version, and never after <see cref="Dispose"/>.
/// </summary>
/// <remarks>
/// Use one coordinator for each document. Dispose it when the document closes or the tab changes, so that an old
/// analysis cannot show diagnostics in another document. <see cref="Published"/> and <see cref="Failed"/> run on a
/// thread-pool thread. The UI must move to its own thread and call <see cref="IsCurrent"/> again.
/// </remarks>
public sealed class DiagnosticsCoordinator : IDisposable
{
    private static readonly TimeSpan _defaultDebounce = TimeSpan.FromMilliseconds(250);

    private readonly ICSharpAnalyzer analyzer;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan debounce;
    private readonly object sync = new object();
    private CancellationTokenSource? running;
    private long version;
    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="DiagnosticsCoordinator"/> class.</summary>
    /// <param name="analyzer">Finds the diagnostics.</param>
    /// <param name="timeProvider">Supplies the idle timer.</param>
    /// <param name="debounce">The idle time before an analysis starts.</param>
    public DiagnosticsCoordinator(ICSharpAnalyzer analyzer, TimeProvider timeProvider, TimeSpan debounce)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(debounce, TimeSpan.Zero);
        this.analyzer = analyzer;
        this.timeProvider = timeProvider;
        this.debounce = debounce;
    }

    /// <summary>Raised when the analysis of the newest version ends.</summary>
    public event Action<AnalysisUpdate>? Published;

    /// <summary>Raised when the analyzer fails for the newest version. The argument is the exception type name, which is safe to log.</summary>
    public event Action<string>? Failed;

    /// <summary>Gets the idle time before an analysis starts. The first target is 250 ms. Tune it with tests.</summary>
    public static TimeSpan DefaultDebounce => _defaultDebounce;

    /// <summary>Gets the version of the newest text. It starts at 0 and grows by 1 for each <see cref="Update"/>.</summary>
    public long Version
    {
        get
        {
            lock (sync)
            {
                return version;
            }
        }
    }

    /// <summary>Tells whether a version is still the newest, and the coordinator is alive.</summary>
    /// <param name="candidate">The version from an <see cref="AnalysisUpdate"/>.</param>
    /// <returns>True if the result still describes the text.</returns>
    public bool IsCurrent(long candidate)
    {
        lock (sync)
        {
            return !disposed && candidate == version;
        }
    }

    /// <summary>Takes the newest text. It cancels the older analysis and starts the wait again.</summary>
    /// <param name="source">The text. It is an immutable string, so later edits cannot change it.</param>
    public void Update(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        CancellationTokenSource next = new CancellationTokenSource();
        long mine;
        lock (sync)
        {
            if (disposed)
            {
                next.Dispose();
                return;
            }

            running?.Cancel();
            running?.Dispose();
            running = next;
            mine = ++version;
        }

        _ = RunAsync(mine, source, next.Token);
    }

    /// <summary>Stops the analysis. No event follows.</summary>
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            running?.Cancel();
            running?.Dispose();
            running = null;
        }
    }

    private async Task RunAsync(long mine, string source, CancellationToken token)
    {
        try
        {
            await Task.Delay(debounce, timeProvider, token).ConfigureAwait(false);
            EditorAnalysis analysis = await Task.Run(() => analyzer.AnalyzeAsync(source, token), token).ConfigureAwait(false);
            if (IsCurrent(mine))
            {
                Published?.Invoke(new AnalysisUpdate(mine, analysis));
            }
        }
        catch (OperationCanceledException)
        {
            // A newer edit or Dispose cancelled this analysis. Nothing is wrong.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (IsCurrent(mine))
            {
                Failed?.Invoke(exception.GetType().Name);
            }
        }
    }
}
