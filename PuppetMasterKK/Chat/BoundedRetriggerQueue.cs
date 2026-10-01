using System;
using System.Collections.Generic;

namespace PuppetMasterKK;

// Why a waiting request left the queue without running.
internal enum RetriggerRemoval
{
    // A newer request from the same trigger took its place (Queue latest, Restart immediately, one per person).
    Replaced,
    // Dropped under load: the oldest, when the queue was full.
    Discarded,
}

/// <summary>
/// The requests waiting for one lane: a trigger, or a turn group's triggers. <paramref name="sameOwner"/> tells whether
/// two requests are from the same trigger (null: one trigger owns the whole queue); Queue latest, Restart immediately
/// and per-person replacement only ever touch the new request's own trigger's requests. Every entry carries a
/// sequence number, so the drainer can tell whether the request it's waiting for is still there.
/// </summary>
internal sealed class BoundedRetriggerQueue<T>(int capacity, Func<T, T, bool>? sameOwner = null)
{
    private readonly List<(long Seq, T Item)> items = [];
    private long nextSeq;

    public int Count => items.Count;

    public int Enqueue(ReactionExecutionPolicy policy, T item) => Enqueue(policy, item, null, out _);

    public int Enqueue(ReactionExecutionPolicy policy, T item, Predicate<T>? replaces, out int replaced)
        => Enqueue(policy, item, replaces, out replaced, null);

    /// <summary>
    /// Adds a request; returns how many waiting ones it dropped (Queue latest and Restart: all of its own trigger's,
    /// the newest taking the first one's place in line; Queue every: the oldest, when full). Queue latest and Restart
    /// with none of their own waiting also drop the oldest when full (a turn group's), reported only to
    /// <paramref name="removed"/>. With Queue every, its own
    /// trigger's waiting ones <paramref name="replaces"/> matches (the same sender's) are removed first and counted in
    /// <paramref name="replaced"/>. Each request that leaves is reported to <paramref name="removed"/>.
    /// </summary>
    public int Enqueue(ReactionExecutionPolicy policy, T item, Predicate<T>? replaces, out int replaced,
        Action<T, RetriggerRemoval>? removed)
    {
        replaced = 0;
        if (policy == ReactionExecutionPolicy.IgnoreWhileRunning)
            return 0;

        if (policy is ReactionExecutionPolicy.QueueLatestTrigger or ReactionExecutionPolicy.RestartImmediately)
        {
            var first = items.FindIndex(entry => Owns(entry.Item, item));
            if (first < 0)
            {
                // A turn group's other triggers can fill the queue: the cap holds for the whole lane.
                DropOldestIfFull(removed);
                items.Add((++nextSeq, item));
                return 0;
            }
            // Keeps its turn: the newest takes the first one's place (and sequence number, so a drainer already
            // waiting for it carries on), the rest of the trigger's are dropped.
            var previous = items[first].Item;
            items[first] = (items[first].Seq, item);
            removed?.Invoke(previous, RetriggerRemoval.Replaced);
            return 1 + RemoveWhere(entry => Owns(entry, item), first + 1, RetriggerRemoval.Replaced, removed);
        }

        if (replaces != null)
            replaced = RemoveWhere(entry => Owns(entry, item) && replaces(entry), 0, RetriggerRemoval.Replaced, removed);
        var dropped = DropOldestIfFull(removed);
        items.Add((++nextSeq, item));
        return dropped;
    }

    private int DropOldestIfFull(Action<T, RetriggerRemoval>? removed)
    {
        if (items.Count < capacity || items.Count == 0)
            return 0;
        var oldest = items[0].Item;
        items.RemoveAt(0);
        removed?.Invoke(oldest, RetriggerRemoval.Discarded);
        return 1;
    }

    /// <summary>Removes every waiting request <paramref name="match"/> matches (Stop, a change to one trigger).</summary>
    public int RemoveWhere(Predicate<T> match) => RemoveWhere(match, 0, RetriggerRemoval.Discarded, null);

    private int RemoveWhere(Predicate<T> match, int start, RetriggerRemoval kind, Action<T, RetriggerRemoval>? removed)
    {
        List<T>? gone = null;
        for (var index = start; index < items.Count;)
        {
            if (!match(items[index].Item))
            {
                index++;
                continue;
            }
            (gone ??= []).Add(items[index].Item);
            items.RemoveAt(index);
        }
        if (gone == null)
            return 0;
        // Reported oldest first, once the queue is consistent again.
        if (removed != null)
        {
            foreach (var item in gone)
                removed(item, kind);
        }
        return gone.Count;
    }

    private bool Owns(T existing, T item) => sameOwner == null || sameOwner(existing, item);

    public bool Contains(long seq) => items.Exists(entry => entry.Seq == seq);

    public bool TryDequeue(out T item)
    {
        if (items.Count == 0)
        {
            item = default!;
            return false;
        }

        item = items[0].Item;
        items.RemoveAt(0);
        return true;
    }

    public bool TryPeek(out T item) => TryPeek(out item, out _);

    public bool TryPeek(out T item, out long seq)
    {
        if (items.Count == 0)
        {
            item = default!;
            seq = 0;
            return false;
        }

        (seq, item) = items[0];
        return true;
    }

    public T[] ToArray() => items.ConvertAll(entry => entry.Item).ToArray();

    public void Clear()
    {
        items.Clear();
    }
}
