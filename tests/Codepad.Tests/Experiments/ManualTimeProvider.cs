namespace Codepad.Tests.Experiments;

/// <summary>A clock and timers that move only when a test calls <see cref="Advance"/>. Tests stay fast and deterministic.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> timers = [];
    private DateTimeOffset now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ManualTimer timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        timers.Add(timer);
        return timer;
    }

    /// <summary>Moves the clock forward and runs each timer that is due, in order.</summary>
    public void Advance(TimeSpan amount)
    {
        DateTimeOffset target = now + amount;
        while (true)
        {
            ManualTimer? next = timers.Where(timer => timer.Due is not null && timer.Due <= target).MinBy(timer => timer.Due);
            if (next is null)
            {
                break;
            }

            now = next.Due!.Value > now ? next.Due.Value : now;
            next.Fire();
        }

        now = target;
    }

    private DateTimeOffset? DueAt(TimeSpan dueTime) => dueTime == Timeout.InfiniteTimeSpan ? null : now + dueTime;

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = owner.DueAt(dueTime);
            return true;
        }

        public void Fire()
        {
            Due = null;
            callback(state);
        }

        public void Dispose() => Due = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
