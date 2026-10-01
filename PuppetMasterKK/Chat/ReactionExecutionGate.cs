using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PuppetMasterKK;

internal enum ReactionRejectionReason
{
    None,
    Busy,
    Cooldown,
    // This person's own cooldown (per-person limits).
    SenderCooldown,
}

/// <summary>
/// Who may run now. Busy is per lane: a trigger on its own, or a turn group (its triggers take turns, never
/// overlapping). Cooldown is always per trigger. Locks are taken lane first, then trigger.
/// </summary>
internal sealed class ReactionExecutionGate
{
    private static readonly TimeSpan MaxDelayStep = TimeSpan.FromDays(1);
    private ConditionalWeakTable<Reaction, ReactionState> states = new();
    // Turn groups, by name (TurnGroups.LaneOf: trimmed, case doesn't matter).
    private ConcurrentDictionary<string, Lane> groups = new(StringComparer.OrdinalIgnoreCase);

    public bool TryEnter(
        Reaction reaction,
        TimeSpan cooldown,
        long nowTimestamp,
        out IDisposable? lease,
        out ReactionRejectionReason rejectionReason,
        bool ignoreCooldown = false)
        => TryEnter(null, reaction, cooldown, nowTimestamp, out lease, out rejectionReason, ignoreCooldown);

    /// <param name="lane">The trigger's turn group; null or empty: the trigger runs on its own.</param>
    public bool TryEnter(
        string? lane,
        Reaction reaction,
        TimeSpan cooldown,
        long nowTimestamp,
        out IDisposable? lease,
        out ReactionRejectionReason rejectionReason,
        bool ignoreCooldown = false)
    {
        var state = states.GetValue(reaction, static _ => new ReactionState());
        var laneState = LaneFor(lane, state);
        lock (laneState)
        {
            if (laneState.Running || laneState.WaitingEntrants > 0)
            {
                lease = null;
                rejectionReason = ReactionRejectionReason.Busy;
                return false;
            }

            lock (state)
            {
                if (!ignoreCooldown && nowTimestamp < state.NextAllowedTimestamp)
                {
                    lease = null;
                    rejectionReason = ReactionRejectionReason.Cooldown;
                    return false;
                }

                StartRun(laneState, state, cooldown, nowTimestamp);
            }
            lease = new Lease(laneState);
            rejectionReason = ReactionRejectionReason.None;
            return true;
        }
    }

    public void Reset()
    {
        states = new ConditionalWeakTable<Reaction, ReactionState>();
        groups = new ConcurrentDictionary<string, Lane>(StringComparer.OrdinalIgnoreCase);
    }

    public Task<IDisposable> EnterWhenAvailableAsync(
        Reaction reaction,
        TimeSpan cooldown,
        CancellationToken cancellationToken,
        bool ignoreCooldown = false)
        => EnterWhenAvailableAsync(null, reaction, cooldown, cancellationToken, ignoreCooldown);

    public async Task<IDisposable> EnterWhenAvailableAsync(
        string? lane,
        Reaction reaction,
        TimeSpan cooldown,
        CancellationToken cancellationToken,
        bool ignoreCooldown = false)
    {
        var state = states.GetValue(reaction, static _ => new ReactionState());
        var laneState = LaneFor(lane, state);
        lock (laneState)
            laneState.WaitingEntrants++;

        try
        {
            while (true)
            {
                Task? idleTask = null;
                TimeSpan cooldownDelay = TimeSpan.Zero;
                lock (laneState)
                {
                    if (laneState.Running)
                        idleTask = laneState.Idle.Task;
                    else
                    {
                        lock (state)
                        {
                            var nowTimestamp = Stopwatch.GetTimestamp();
                            if (ignoreCooldown || nowTimestamp >= state.NextAllowedTimestamp)
                            {
                                laneState.WaitingEntrants--;
                                StartRun(laneState, state, cooldown, nowTimestamp);
                                return new Lease(laneState);
                            }

                            // Task.Delay takes at most ~49 days: a longer cooldown is waited out in steps.
                            cooldownDelay = TimeSpan.FromSeconds(Math.Min(
                                (state.NextAllowedTimestamp - nowTimestamp) / (double)Stopwatch.Frequency,
                                MaxDelayStep.TotalSeconds));
                        }
                    }
                }

                if (idleTask != null)
                    await idleTask.WaitAsync(cancellationToken);
                else
                    await Task.Delay(cooldownDelay, cancellationToken);
            }
        }
        catch
        {
            lock (laneState)
                laneState.WaitingEntrants--;
            throw;
        }
    }

    private Lane LaneFor(string? lane, ReactionState state)
        => string.IsNullOrEmpty(lane) ? state.Own : groups.GetOrAdd(lane, static _ => new Lane());

    private static void StartRun(Lane lane, ReactionState state, TimeSpan cooldown, long nowTimestamp)
    {
        lane.Running = true;
        lane.Idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cooldownSeconds = Math.Max(0, cooldown.TotalSeconds);
        var cooldownTicks = cooldownSeconds >= long.MaxValue / (double)Stopwatch.Frequency
            ? long.MaxValue
            : (long)(cooldownSeconds * Stopwatch.Frequency);
        state.NextAllowedTimestamp = cooldownTicks > long.MaxValue - nowTimestamp
            ? long.MaxValue
            : nowTimestamp + cooldownTicks;
    }

    // One trigger: its cooldown, and its lane when it isn't in a turn group.
    private sealed class ReactionState
    {
        public long NextAllowedTimestamp;
        public readonly Lane Own = new();
    }

    // What takes turns: whether something is running, and who's waiting for it.
    private sealed class Lane
    {
        public bool Running;
        public int WaitingEntrants;
        public TaskCompletionSource Idle = CreateIdleSignal();

        private static TaskCompletionSource CreateIdleSignal()
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            signal.SetResult();
            return signal;
        }
    }

    private sealed class Lease(Lane lane) : IDisposable
    {
        private Lane? lane = lane;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref lane, null);
            if (current == null)
                return;
            TaskCompletionSource idle;
            lock (current)
            {
                current.Running = false;
                idle = current.Idle;
            }
            idle.TrySetResult();
        }
    }
}
