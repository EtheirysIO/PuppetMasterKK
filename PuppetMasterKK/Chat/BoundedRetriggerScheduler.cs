using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PuppetMasterKK;

/// <summary>
/// Runs one lane's waiting requests in order, one at a time: a trigger's, or a turn group's (see
/// <see cref="BoundedRetriggerQueue{T}"/> for <paramref name="sameOwner"/>). <paramref name="reportRemoved"/> hears about
/// every request that leaves without running, so each can be counted against its own trigger.
/// </summary>
internal sealed class BoundedRetriggerScheduler<T>(
    int capacity,
    Func<T, CancellationToken, Task<IDisposable>> acquire,
    Func<T, IDisposable, Task> execute,
    Action<int>? reportDropped = null,
    Action<Exception, int>? reportFailure = null,
    Action<int>? reportReplaced = null,
    Func<T, T, bool>? sameOwner = null,
    Action<T, RetriggerRemoval>? reportRemoved = null)
{
    private readonly object sync = new();
    private readonly BoundedRetriggerQueue<T> queue = new(capacity, sameOwner);
    private bool isDraining;
    private long generation;
    private CancellationTokenSource? drainerCancellation;
    private CancellationToken lifetime;
    // The request the drainer is waiting for a turn for (the first in line when it started waiting).
    private Acquiring? acquiring;

    private sealed class Acquiring(long seq, CancellationTokenSource cancellation)
    {
        public readonly long Seq = seq;
        public readonly CancellationTokenSource Cancellation = cancellation;
        // Removed while waiting: the turn it gets must not go to whoever is first now.
        public bool Gone;
    }

    public int PendingCount
    {
        get
        {
            lock (sync)
                return queue.Count;
        }
    }

    // True while queued triggers are waiting or being drained; new triggers must queue behind them.
    public bool IsActive
    {
        get
        {
            lock (sync)
                return isDraining || queue.Count > 0;
        }
    }

    /// <param name="replaces">Queue every: waiting requests this one takes the place of (the same sender's).</param>
    public Task? Enqueue(ReactionExecutionPolicy policy, T item, CancellationToken lifetimeToken, Predicate<T>? replaces = null)
    {
        List<(T Item, RetriggerRemoval Kind)>? removed = null;
        CancellationTokenSource? stale;
        Task? drainer = null;
        lock (sync)
        {
            queue.Enqueue(policy, item, replaces, out _, (gone, kind) => (removed ??= []).Add((gone, kind)));
            stale = MarkAcquiringIfGone();
            if (policy != ReactionExecutionPolicy.IgnoreWhileRunning && !isDraining)
            {
                isDraining = true;
                generation++;
                lifetime = lifetimeToken;
                drainerCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
                drainer = DrainAsync(generation, drainerCancellation.Token);
            }
        }

        CancelQuietly(stale);
        if (removed != null)
            Report(removed);
        return drainer;
    }

    /// <summary>
    /// Drops the waiting requests <paramref name="match"/> matches (one trigger's, in a turn group) and keeps the rest
    /// in line. Returns how many it dropped.
    /// </summary>
    public int RemoveWhere(Predicate<T> match)
    {
        int removed;
        CancellationTokenSource? stale;
        lock (sync)
        {
            removed = queue.RemoveWhere(match);
            stale = MarkAcquiringIfGone();
        }
        CancelQuietly(stale);
        return removed;
    }

    /// <summary>How many waiting requests <paramref name="match"/> matches.</summary>
    public int CountWhere(Predicate<T> match)
    {
        lock (sync)
            return Array.FindAll(queue.ToArray(), match).Length;
    }

    /// <summary>Drops everything waiting and stops the drainer. Returns how many waiting items were dropped.</summary>
    public int Cancel()
    {
        CancellationTokenSource? cancellation;
        int cleared;
        lock (sync)
        {
            cleared = queue.Count;
            queue.Clear();
            isDraining = false;
            generation++;
            cancellation = drainerCancellation;
            drainerCancellation = null;
        }

        if (cancellation == null)
            return cleared;
        CancelQuietly(cancellation);
        cancellation.Dispose();
        return cleared;
    }

    // Called under sync. The token is cancelled by the caller after the lock is released: cancelling runs the waiting
    // drainer's continuation, which must not run inside this lock.
    private CancellationTokenSource? MarkAcquiringIfGone()
    {
        if (acquiring == null || acquiring.Gone || queue.Contains(acquiring.Seq))
            return null;
        acquiring.Gone = true;
        return acquiring.Cancellation;
    }

    private static void CancelQuietly(CancellationTokenSource? cancellation)
    {
        if (cancellation == null)
            return;
        try { cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void Report(List<(T Item, RetriggerRemoval Kind)> removed)
    {
        var discarded = removed.FindAll(entry => entry.Kind == RetriggerRemoval.Discarded).Count;
        if (discarded > 0)
            reportDropped?.Invoke(discarded);
        if (removed.Count - discarded > 0)
            reportReplaced?.Invoke(removed.Count - discarded);
        if (reportRemoved != null)
        {
            foreach (var (item, kind) in removed)
                reportRemoved(item, kind);
        }
    }

    private async Task DrainAsync(long drainerGeneration, CancellationToken drainerToken)
    {
        Exception? failure = null;
        try
        {
            while (true)
            {
                T pending;
                Acquiring current;
                lock (sync)
                {
                    if (generation != drainerGeneration || !queue.TryPeek(out pending, out var seq))
                        return;
                    current = new Acquiring(seq, CancellationTokenSource.CreateLinkedTokenSource(drainerToken));
                    acquiring = current;
                }

                IDisposable? lease = null;
                try
                {
                    lease = await acquire(pending, current.Cancellation.Token);
                }
                catch (OperationCanceledException) when (current.Cancellation.IsCancellationRequested &&
                                                         !drainerToken.IsCancellationRequested)
                {
                    // Its request was removed while it waited: go on with whoever is first now.
                }
                finally
                {
                    if (lease == null)
                        Release(current);
                }
                if (lease == null)
                    continue;

                var run = false;
                var stop = false;
                lock (sync)
                {
                    if (acquiring == current)
                        acquiring = null;
                    if (generation != drainerGeneration)
                        stop = true;
                    // Gone: removed just as its turn came. That turn belongs to its own trigger, so it's given back.
                    else if (!current.Gone)
                        run = queue.TryDequeue(out pending);
                    if (!run)
                        lease.Dispose();
                }
                current.Cancellation.Dispose();
                if (stop)
                    return;
                if (run)
                    await execute(pending, lease);
            }
        }
        catch (OperationCanceledException) when (drainerToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            T[]? discarded = null;
            lock (sync)
            {
                if (generation == drainerGeneration)
                {
                    drainerCancellation?.Dispose();
                    drainerCancellation = null;
                    if (failure == null && queue.Count > 0 && !lifetime.IsCancellationRequested)
                    {
                        // A trigger arrived after the last peek but before this cleanup: keep draining.
                        generation++;
                        drainerCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
                        _ = DrainAsync(generation, drainerCancellation.Token);
                    }
                    else
                    {
                        if (failure != null)
                            discarded = queue.ToArray();
                        queue.Clear();
                        isDraining = false;
                    }
                }
            }

            if (failure != null)
            {
                reportFailure?.Invoke(failure, discarded?.Length ?? 0);
                if (discarded != null && reportRemoved != null)
                {
                    foreach (var item in discarded)
                        reportRemoved(item, RetriggerRemoval.Discarded);
                }
            }
        }
    }

    private void Release(Acquiring current)
    {
        lock (sync)
        {
            if (acquiring == current)
                acquiring = null;
        }
        current.Cancellation.Dispose();
    }
}
