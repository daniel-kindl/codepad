namespace Codepad.Application.Json;

/// <summary>
/// Evaluates the text of one JSON document after a pause in the edits. A newer edit cancels the running
/// evaluation, and the coordinator drops its result even when the evaluator ignores the cancellation. It never raises
/// <see cref="Published"/> for an old version, and never after <see cref="Dispose"/>. Each version is one request
/// sequence number of one document. It creates no history record.
/// </summary>
/// <remarks>
/// Use one coordinator for each document. <see cref="Published"/> and <see cref="Failed"/> run on a thread-pool
/// thread. The UI must move to its own thread and call <see cref="IsCurrent"/> again.
/// </remarks>
public sealed class LiveJsonCoordinator : IDisposable
{
    private static readonly TimeSpan _defaultDebounce = TimeSpan.FromMilliseconds(250);

    private readonly IJsonEvaluator evaluator;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan debounce;
    private readonly object sync = new object();
    private CancellationTokenSource? running;
    private long version;
    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="LiveJsonCoordinator"/> class.</summary>
    /// <param name="evaluator">Evaluates the text.</param>
    /// <param name="timeProvider">Supplies the idle timer.</param>
    /// <param name="debounce">The idle time before an evaluation starts.</param>
    public LiveJsonCoordinator(IJsonEvaluator evaluator, TimeProvider timeProvider, TimeSpan debounce)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(debounce, TimeSpan.Zero);
        this.evaluator = evaluator;
        this.timeProvider = timeProvider;
        this.debounce = debounce;
    }

    /// <summary>Raised when the evaluation of the newest version ends.</summary>
    public event Action<JsonUpdate>? Published;

    /// <summary>Raised when the evaluator fails for the newest version. The argument is the exception type name, which is safe to log.</summary>
    public event Action<string>? Failed;

    /// <summary>Gets the idle time before an evaluation starts.</summary>
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
    /// <param name="candidate">The version from a <see cref="JsonUpdate"/>.</param>
    /// <returns>True if the result still describes the text.</returns>
    public bool IsCurrent(long candidate)
    {
        lock (sync)
        {
            return !disposed && candidate == version;
        }
    }

    /// <summary>Takes the newest text. It cancels the older evaluation and starts the wait again.</summary>
    /// <param name="text">The text. It is an immutable string, so later edits cannot change it.</param>
    public void Update(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        CancellationTokenSource next = new CancellationTokenSource();
        long mine;
        CancellationToken token;
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
            token = next.Token;
        }

        _ = RunAsync(mine, text, token);
    }

    /// <summary>Stops the evaluation. No event follows.</summary>
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

    private async Task RunAsync(long mine, string text, CancellationToken token)
    {
        try
        {
            await Task.Delay(debounce, timeProvider, token).ConfigureAwait(false);
            JsonEvaluation evaluation = await Task.Run(() => evaluator.EvaluateAsync(text, token), token).ConfigureAwait(false);
            if (IsCurrent(mine))
            {
                Published?.Invoke(new JsonUpdate(mine, evaluation));
            }
        }
        catch (OperationCanceledException)
        {
            // A newer edit or Dispose cancelled this evaluation. The newer one reports.
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
