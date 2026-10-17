namespace Beeps.Features.Live;

/// <summary>
/// How many admin pages this instance follows live. Figures such as backlogs change with no event of their own, so
/// they are only looked at (by <c>AdminFiguresWorker</c>) while someone watches them.
/// </summary>
public sealed class AdminWatchers
{
    private int count;

    public bool Any => Volatile.Read(ref count) > 0;

    /// <summary>Counts one admin page until disposed.</summary>
    public IDisposable Watch()
    {
        Interlocked.Increment(ref count);
        return new Watching(this);
    }

    private sealed class Watching(AdminWatchers watchers) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                Interlocked.Decrement(ref watchers.count);
            }
        }
    }
}
