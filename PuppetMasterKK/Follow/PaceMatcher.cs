using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace PuppetMasterKK;

// While following someone (Follow mode or Mimic): walk when they walk, run when they run, and run to catch up when
// you've fallen behind. Your own walk toggle is put back when following ends. Framework thread only.
internal static unsafe class PaceMatcher
{
    // Farther behind than this, always run.
    private const float CatchUpFrom = 10f;
    // The game doesn't say when /follow ends (you moved, they mounted). Farther than this for this long, with no
    // vnavmesh walk going, means you're not following any more: stop and give the walk toggle back.
    private const float LostFrom = 15f;
    private static readonly long LostAfter = 3 * Stopwatch.Frequency;
    private static long farSince;
    private static readonly long LookEvery = Stopwatch.Frequency / 10;

    private static readonly PaceEstimator Pace = new();
    private static PlayerName? leader;
    private static Func<bool>? enabled;
    private static bool watching;
    private static long nextLook;
    // Your walk toggle before matching started, once it's been changed.
    private static bool? walkingBefore;

    public static void Start(PlayerName who, Func<bool> isEnabled)
    {
        leader = who;
        enabled = isEnabled;
        Pace.Reset();
        nextLook = 0;
        farSince = 0;
        if (watching)
            return;
        watching = true;
        Service.Framework.Update += Tick;
    }

    public static void Stop()
    {
        leader = null;
        enabled = null;
        if (watching)
        {
            watching = false;
            Service.Framework.Update -= Tick;
        }
        if (walkingBefore is { } before)
        {
            SetWalking(before);
            walkingBefore = null;
        }
    }

    private static void Tick(IFramework framework)
    {
        try
        {
            if (leader is not { } who || enabled?.Invoke() != true)
            {
                Restore();
                return;
            }
            var now = Stopwatch.GetTimestamp();
            if (now < nextLook)
                return;
            nextLook = now + LookEvery;
            var local = Service.ObjectTable.LocalPlayer;
            if (local == null || FollowMode.Locate(who, local) is not { } found)
                return;
            Pace.Add(found.Position, now / (double)Stopwatch.Frequency);
            var distance = Vector3.Distance(local.Position, found.Position);
            if (distance > LostFrom && !FollowNavigator.IsWalking)
            {
                if (farSince == 0)
                    farSince = now;
                else if (now - farSince > LostAfter)
                {
                    Service.PluginLog.Information("No longer following {Leader}: too far behind.", who.Name);
                    FollowMode.ClearFollowing();
                    return;
                }
            }
            else
            {
                farSince = 0;
            }
            var behind = distance > CatchUpFrom;
            var walk = !behind && Pace.Walking == true;
            if (Pace.Walking == null && !behind)
                return; // not seen moving yet: leave the toggle alone
            Apply(walk);
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "Stopped matching pace.");
            Stop();
        }
    }

    private static void Apply(bool walk)
    {
        var control = Control.Instance();
        if (control == null || control->IsWalking == walk)
            return;
        walkingBefore ??= control->IsWalking;
        control->IsWalking = walk;
    }

    // Matching was switched off mid-follow: give the toggle back now, keep watching in case it's switched on again.
    private static void Restore()
    {
        if (walkingBefore is not { } before)
            return;
        SetWalking(before);
        walkingBefore = null;
    }

    private static void SetWalking(bool walk)
    {
        var control = Control.Instance();
        if (control != null)
            control->IsWalking = walk;
    }
}
