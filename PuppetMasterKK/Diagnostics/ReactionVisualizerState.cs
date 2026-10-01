using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace PuppetMasterKK;

// Interrupted: stopped by a newer request (Restart immediately). Replaced: a waiting request dropped for a newer one.
internal enum VisualizerRunStatus { Running, Completed, Cancelled, Disabled, Interrupted, Replaced }

internal sealed record VisualizerRunSnapshot(long Id, long ReactionId, string ReactionName, string Command,
    VisualizerRunStatus Status, DateTime StartedAt, DateTime? FinishedAt, bool Practice = false)
{
    // Practice mode: the lines this run would have sent.
    public string[] WouldSend { get; init; } = [];
}
internal sealed record VisualizerQueueSnapshot(long Id, long ReactionId, string ReactionName, string Command, DateTime QueuedAt);
internal sealed record ReactionVisualizerSnapshot(VisualizerRunSnapshot[] Active, VisualizerQueueSnapshot[] Queued,
    VisualizerRunSnapshot[] Recent);

internal enum VisualizerCounter
{
    Started,
    Completed,
    Stopped,
    Interrupted,
    IgnoredBusy,
    IgnoredCooldown,
    Replaced,
    DiscardedFull,
    BlockedLines,
    TimedOut,
}

/// <summary>What happened to one trigger's requests this session.</summary>
internal readonly record struct VisualizerCounts(
    long Started,
    long Completed,
    long Stopped,
    long Interrupted,
    long IgnoredBusy,
    long IgnoredCooldown,
    long Replaced,
    long DiscardedFull,
    long BlockedLines,
    long TimedOut)
{
    public long Ignored => IgnoredBusy + IgnoredCooldown;

    public static VisualizerCounts operator +(VisualizerCounts a, VisualizerCounts b) => new(
        a.Started + b.Started, a.Completed + b.Completed, a.Stopped + b.Stopped, a.Interrupted + b.Interrupted,
        a.IgnoredBusy + b.IgnoredBusy, a.IgnoredCooldown + b.IgnoredCooldown, a.Replaced + b.Replaced,
        a.DiscardedFull + b.DiscardedFull, a.BlockedLines + b.BlockedLines, a.TimedOut + b.TimedOut);
}

/// <summary>A one-way runtime projection. It owns no Reaction references and exposes immutable snapshots only.</summary>
internal static class ReactionVisualizerState
{
    private const int RecentCapacity = 24;
    private const int WouldSendCapacity = 32;
    private static readonly int CounterCount = Enum.GetValues<VisualizerCounter>().Length;
    private static readonly object Sync = new();
    private static readonly List<VisualizerRunSnapshot> Active = [];
    private static readonly List<VisualizerQueueSnapshot> Queued = [];
    private static readonly List<VisualizerRunSnapshot> Recent = [];
    // Keyed by visualizer id: one entry per trigger the user has, never per sender or message.
    private static readonly ConcurrentDictionary<long, long[]> CounterTable = new();
    private static long nextId;

    public static ReactionVisualizerSnapshot Snapshot()
    {
        lock (Sync) return new([.. Active], [.. Queued], [.. Recent]);
    }

    public static void Count(long reactionId, VisualizerCounter counter, long amount = 1)
    {
        if (amount <= 0)
            return;
        var values = CounterTable.GetOrAdd(reactionId, static _ => new long[CounterCount]);
        Interlocked.Add(ref values[(int)counter], amount);
    }

    public static VisualizerCounts Counters(long reactionId)
        => CounterTable.TryGetValue(reactionId, out var values) ? ToCounts(values) : default;

    public static VisualizerCounts TotalCounters()
    {
        var total = default(VisualizerCounts);
        foreach (var values in CounterTable.Values)
            total += ToCounts(values);
        return total;
    }

    public static void ResetCounters() => CounterTable.Clear();

    private static VisualizerCounts ToCounts(long[] v)
    {
        long R(VisualizerCounter counter) => Interlocked.Read(ref v[(int)counter]);
        return new(R(VisualizerCounter.Started), R(VisualizerCounter.Completed), R(VisualizerCounter.Stopped),
                   R(VisualizerCounter.Interrupted), R(VisualizerCounter.IgnoredBusy), R(VisualizerCounter.IgnoredCooldown),
                   R(VisualizerCounter.Replaced), R(VisualizerCounter.DiscardedFull), R(VisualizerCounter.BlockedLines),
                   R(VisualizerCounter.TimedOut));
    }

    public static long Started(long reactionId, string reactionName, string command, bool practice = false)
    {
        Count(reactionId, VisualizerCounter.Started);
        lock (Sync)
        {
            var id = ++nextId;
            Active.Add(new(id, reactionId, DisplayName(reactionName), command,
                VisualizerRunStatus.Running, DateTime.Now, null, practice));
            return id;
        }
    }

    /// <summary>Practice mode: a line the run would have sent.</summary>
    public static void WouldSend(long runId, string line)
    {
        lock (Sync)
        {
            var index = Active.FindIndex(item => item.Id == runId);
            if (index < 0 || Active[index].WouldSend.Length >= WouldSendCapacity)
                return;
            Active[index] = Active[index] with { WouldSend = [.. Active[index].WouldSend, line] };
        }
    }

    public static void Finished(long runId, bool cancelled, bool reactionEnabled, bool interrupted = false)
    {
        lock (Sync)
        {
            var index = Active.FindIndex(item => item.Id == runId);
            if (index < 0) return;
            var completed = Active[index] with
            {
                Status = ResolveFinishedStatus(cancelled, reactionEnabled, interrupted),
                FinishedAt = DateTime.Now,
            };
            Active.RemoveAt(index);
            AddRecent(completed);
            Count(completed.ReactionId, completed.Status switch
            {
                VisualizerRunStatus.Completed => VisualizerCounter.Completed,
                VisualizerRunStatus.Interrupted => VisualizerCounter.Interrupted,
                _ => VisualizerCounter.Stopped,
            });
        }
    }

    public static void QueuedRun(long reactionId, string reactionName, string command, ReactionExecutionPolicy policy)
    {
        lock (Sync)
        {
            if (policy is ReactionExecutionPolicy.QueueLatestTrigger or ReactionExecutionPolicy.RestartImmediately)
                RemoveQueued(reactionId, replaced: true);
            Queued.Add(new(++nextId, reactionId, DisplayName(reactionName), command, DateTime.Now));
            while (Queued.Count(item => item.ReactionId == reactionId) > 16)
            {
                var oldest = Queued.FindIndex(item => item.ReactionId == reactionId);
                if (oldest < 0) break;
                Queued.RemoveAt(oldest);
            }
        }
    }

    public static void DequeuedRun(long reactionId)
    {
        lock (Sync)
        {
            var index = Queued.FindIndex(item => item.ReactionId == reactionId);
            if (index >= 0) Queued.RemoveAt(index);
        }
    }

    /// <summary>Drops a trigger's waiting requests; <paramref name="replaced"/> lists them in Recent as Replaced.</summary>
    public static void ClearQueued(long reactionId, bool replaced = false)
    {
        lock (Sync) RemoveQueued(reactionId, replaced);
    }

    public static void Reset()
    {
        lock (Sync) { Active.Clear(); Queued.Clear(); Recent.Clear(); }
        ResetCounters();
    }

    public static VisualizerRunStatus ResolveFinishedStatus(bool cancelled, bool reactionEnabled, bool interrupted = false)
    {
        if (!cancelled)
            return VisualizerRunStatus.Completed;
        if (interrupted)
            return VisualizerRunStatus.Interrupted;
        return reactionEnabled ? VisualizerRunStatus.Cancelled : VisualizerRunStatus.Disabled;
    }

    private static void RemoveQueued(long reactionId, bool replaced)
    {
        if (replaced)
        {
            var now = DateTime.Now;
            foreach (var item in Queued)
            {
                if (item.ReactionId == reactionId)
                    AddRecent(new(item.Id, reactionId, item.ReactionName, item.Command, VisualizerRunStatus.Replaced,
                                  item.QueuedAt, now));
            }
        }
        Queued.RemoveAll(item => item.ReactionId == reactionId);
    }

    private static void AddRecent(VisualizerRunSnapshot run)
    {
        Recent.Insert(0, run);
        if (Recent.Count > RecentCapacity) Recent.RemoveRange(RecentCapacity, Recent.Count - RecentCapacity);
    }

    private static string DisplayName(string name) => string.IsNullOrWhiteSpace(name) ? "Unnamed trigger" : name;
}
