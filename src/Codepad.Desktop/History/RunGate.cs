namespace Codepad.Desktop.History;

/// <summary>
/// Lets only one run go at a time. A Run command and a Rerun from history share one gate, so the app never starts
/// two worker processes together and two reports never fight over the result panel.
/// </summary>
public sealed class RunGate
{
    private readonly object sync = new object();
    private bool running;

    /// <summary>Raised after a run enters or leaves the gate.</summary>
    public event Action? Changed;

    /// <summary>Gets a value indicating whether a run is in progress.</summary>
    public bool IsRunning
    {
        get
        {
            lock (sync)
            {
                return running;
            }
        }
    }

    /// <summary>Tries to start a run.</summary>
    /// <returns>True when the caller owns the gate and must call <see cref="Exit"/>. False when another run is in progress.</returns>
    public bool TryEnter()
    {
        lock (sync)
        {
            if (running)
            {
                return false;
            }

            running = true;
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Ends the run that entered the gate.</summary>
    public void Exit()
    {
        lock (sync)
        {
            running = false;
        }

        Changed?.Invoke();
    }
}
