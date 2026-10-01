using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;

namespace PuppetMasterKK;

// Walks to a player with vnavmesh (framework thread only: the path task completes on another thread, but its result
// is only read in Tick), then hands over to Follow mode to target and follow them.
// We ask vnavmesh for the path, round its corners (PathSmoothing) and give it back to follow, so the run is smooth.
// New paths are worked out while still running on the old one and swapped in place: no stopping to think. It gives
// up in combat, after a while, when they leave the zone, or when it keeps getting stuck.
internal static class FollowNavigator
{
    private const string Vnavmesh = "vnavmesh";
    // Close enough: stop beside them, then target and /follow.
    private const float ArriveDistance = 2f;
    // How close vnavmesh has to get to each point before heading to the next (small, for the rounded corners).
    private const float WaypointTolerance = 0.5f;
    // They moved this far from where we're heading (or a fifth of the way left, whichever is more): path again.
    private const float RepathDistance = 3f;
    private static readonly long RepathEvery = Stopwatch.Frequency * 3 / 4;
    // Moving less than StuckDistance over StuckTime while vnavmesh is running counts as stuck.
    private const float StuckDistance = 1f;
    private static readonly long StuckTime = Stopwatch.Frequency * 2;
    private static readonly long GiveUpAfter = Stopwatch.Frequency * 90;
    private const int MaxRetries = 5;

    private static PlayerName? goal;
    private static Vector3 destination;
    private static Task<List<Vector3>>? pending;
    private static long startedAt;
    private static long pathedAt;
    private static Vector3 progressFrom;
    private static long progressAt;
    private static int retries;
    private static float? savedTolerance;
    private static bool hooked;

    public static bool IsWalking => goal != null;

    // vnavmesh is loaded and has this zone's mesh.
    public static bool IsAvailable()
    {
        if (!IsLoaded())
            return false;
        try
        {
            return Service.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady").InvokeFunc();
        }
        catch (Exception ex)
        {
            Service.PluginLog.Debug(ex, "vnavmesh didn't answer.");
            return false;
        }
    }

    public static bool IsLoaded()
    {
        try
        {
            foreach (var plugin in Service.PluginInterface.InstalledPlugins)
            {
                if (plugin.IsLoaded && plugin.InternalName.Equals(Vnavmesh, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch (Exception ex)
        {
            Service.PluginLog.Debug(ex, "Couldn't read the plugin list.");
        }
        return false;
    }

    public static bool Start(PlayerName who, Vector3 position)
    {
        Cancel();
        var local = Service.ObjectTable.LocalPlayer;
        if (local == null || !RequestPath(local.Position, position))
            return false;
        goal = who;
        startedAt = Stopwatch.GetTimestamp();
        progressFrom = local.Position;
        progressAt = startedAt;
        retries = 0;
        SetTolerance();
        if (!hooked)
        {
            Service.Framework.Update += Tick;
            hooked = true;
        }
        Service.PluginLog.Information("Walking to {Target}.", who.Name);
        return true;
    }

    // Stops walking (and vnavmesh's path, if we started one).
    public static void Cancel()
    {
        if (hooked)
        {
            Service.Framework.Update -= Tick;
            hooked = false;
        }
        pending = null;
        if (goal == null)
            return;
        goal = null;
        try
        {
            Service.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop").InvokeAction();
        }
        catch (Exception ex)
        {
            Service.PluginLog.Debug(ex, "Couldn't stop vnavmesh's path.");
        }
        RestoreTolerance();
    }

    private static void GiveUp(string why)
    {
        Service.PluginLog.Information("Stopped walking to {Target}: {Reason}.", goal?.Name ?? "?", why);
        Cancel();
        FollowMode.ClearFollowing();
    }

    private static bool Flying => Service.Condition[ConditionFlag.InFlight] || Service.Condition[ConditionFlag.Diving];

    // Asks vnavmesh for a path; it arrives on a later frame (see Tick).
    private static bool RequestPath(Vector3 from, Vector3 to)
    {
        try
        {
            pending = Service.PluginInterface.GetIpcSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>>("vnavmesh.Nav.Pathfind")
                             .InvokeFunc(from, to, Flying);
            destination = to;
            pathedAt = Stopwatch.GetTimestamp();
            return true;
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "vnavmesh couldn't path there.");
            pending = null;
            return false;
        }
    }

    // A finished path: drop what's behind us, round the corners, and run it (replacing the one we're on).
    private static bool RunPath(List<Vector3> path, Vector3 position, float rotation)
    {
        if (path.Count == 0)
            return false;
        var heading = new Vector3(MathF.Sin(rotation), 0f, MathF.Cos(rotation));
        var ahead = PathSmoothing.TrimBehind(path, position, IsRunning() ? heading : Vector3.Zero);
        var smooth = PathSmoothing.RoundCorners(position, ahead);
        Service.PluginInterface.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo").InvokeAction(smooth, Flying);
        return true;
    }

    private static bool IsRunning()
    {
        try
        {
            return Service.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning").InvokeFunc();
        }
        catch
        {
            return false;
        }
    }

    // vnavmesh's waypoint tolerance is its own setting: ours while walking, theirs back after.
    private static void SetTolerance()
    {
        try
        {
            savedTolerance ??= Service.PluginInterface.GetIpcSubscriber<float>("vnavmesh.Path.GetTolerance").InvokeFunc();
            Service.PluginInterface.GetIpcSubscriber<float, object>("vnavmesh.Path.SetTolerance").InvokeAction(WaypointTolerance);
        }
        catch (Exception ex)
        {
            Service.PluginLog.Debug(ex, "Couldn't set vnavmesh's tolerance.");
        }
    }

    private static void RestoreTolerance()
    {
        if (savedTolerance is not { } tolerance)
            return;
        savedTolerance = null;
        try
        {
            Service.PluginInterface.GetIpcSubscriber<float, object>("vnavmesh.Path.SetTolerance").InvokeAction(tolerance);
        }
        catch (Exception ex)
        {
            Service.PluginLog.Debug(ex, "Couldn't restore vnavmesh's tolerance.");
        }
    }

    private static void Tick(IFramework framework)
    {
        if (goal is not { } who)
        {
            Cancel();
            return;
        }
        try
        {
            var local = Service.ObjectTable.LocalPlayer;
            if (local == null)
            {
                GiveUp("no character");
                return;
            }
            if (Service.configuration?.Follow?.Enabled != true)
            {
                GiveUp("Follow mode is off");
                return;
            }
            var now = Stopwatch.GetTimestamp();
            if (Service.Condition[ConditionFlag.InCombat])
            {
                GiveUp("in combat");
                return;
            }
            if (now - startedAt > GiveUpAfter)
            {
                GiveUp("took too long");
                return;
            }

            var found = FollowMode.Locate(who, local);
            if (found == null)
            {
                GiveUp("they're not in the zone any more");
                return;
            }
            var (position, character) = found.Value;
            var distance = Vector3.Distance(local.Position, position);
            if (character != null && distance <= ArriveDistance)
            {
                Cancel();
                FollowMode.TargetAndFollow(character, who.Name);
                return;
            }

            // A path came back.
            if (pending is { IsCompleted: true } done)
            {
                pending = null;
                if (done.IsFaulted)
                    Service.PluginLog.Debug(done.Exception, "vnavmesh couldn't path there.");
                var ran = done.IsCompletedSuccessfully && done.Result is { } path && RunPath(path, local.Position, local.Rotation);
                if (!ran && ++retries > MaxRetries)
                {
                    GiveUp("vnavmesh can't reach them");
                    return;
                }
            }
            if (pending != null)
                return;

            // Progress, or stuck.
            var running = IsRunning();
            if (Vector3.Distance(local.Position, progressFrom) >= StuckDistance)
            {
                progressFrom = local.Position;
                progressAt = now;
                retries = 0;
            }
            var stuck = now - progressAt > StuckTime;
            if (stuck)
            {
                progressAt = now;
                if (++retries > MaxRetries)
                {
                    GiveUp("stuck");
                    return;
                }
            }

            // They moved (compared with how far is left), the path ended short of them, or we're stuck: path again.
            var moved = Vector3.Distance(position, destination) > MathF.Max(RepathDistance, distance * 0.2f);
            if (stuck || (!running && now - pathedAt > RepathEvery) || (moved && now - pathedAt > RepathEvery))
                RequestPath(local.Position, position);
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "Walking to {Target} failed.", who.Name);
            GiveUp("an error");
        }
    }
}
