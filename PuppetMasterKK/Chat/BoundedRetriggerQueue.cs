using System;
using System.Collections.Generic;

namespace PuppetMasterKK;

internal sealed class BoundedRetriggerQueue<T>(int capacity)
{
    private readonly Queue<T> items = new();

    public int Count => items.Count;

    public int Enqueue(ReactionExecutionPolicy policy, T item) => Enqueue(policy, item, null, out _);

    /// <summary>
    /// Adds a request; returns how many waiting ones it dropped (Queue latest and Restart: all of them; Queue every:
    /// the oldest, when full). With Queue every, the waiting ones <paramref name="replaces"/> matches (the same
    /// sender's) are removed first and counted in <paramref name="replaced"/>.
    /// </summary>
    public int Enqueue(ReactionExecutionPolicy policy, T item, Predicate<T>? replaces, out int replaced)
    {
        replaced = 0;
        if (policy == ReactionExecutionPolicy.IgnoreWhileRunning)
            return 0;

        var dropped = 0;
        if (policy is ReactionExecutionPolicy.QueueLatestTrigger or ReactionExecutionPolicy.RestartImmediately)
        {
            dropped = items.Count;
            items.Clear();
        }
        else
        {
            if (replaces != null)
                replaced = RemoveWhere(replaces);
            if (items.Count >= capacity)
            {
                items.Dequeue();
                dropped = 1;
            }
        }

        items.Enqueue(item);
        return dropped;
    }

    private int RemoveWhere(Predicate<T> match)
    {
        var kept = new List<T>(items.Count);
        foreach (var item in items)
        {
            if (!match(item))
                kept.Add(item);
        }
        var removed = items.Count - kept.Count;
        if (removed == 0)
            return 0;
        items.Clear();
        foreach (var item in kept)
            items.Enqueue(item);
        return removed;
    }

    public bool TryDequeue(out T item)
    {
        if (items.Count == 0)
        {
            item = default!;
            return false;
        }

        item = items.Dequeue();
        return true;
    }

    public bool TryPeek(out T item)
    {
        if (items.Count == 0)
        {
            item = default!;
            return false;
        }

        item = items.Peek();
        return true;
    }

    public void Clear()
    {
        items.Clear();
    }
}
