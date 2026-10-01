using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using System;
using System.Diagnostics;
using System.Numerics;

namespace PuppetMasterKK;

// Walks to a player with vnavmesh (framework thread only), then hands over to Follow mode to target and follow them.
// The path is redone when they move away from where we're heading; it gives up in combat, after a while, or when
// they leave the zone.
internal static class FollowNavigator
{
    private const string Vnavmesh = "vnavmesh";
    // Close enough to target and /follow.
    private const float ArriveDistance = 3.5f;
    // They moved this far from where we're heading: path again.
    private const float RepathDistance = 6f;
    private static readonly long RepathEvery = Stopwatch.Frequency * 3 / 2;
    private static readonly long RetryEvery = Stopwatch.Frequency * 2;
    private static readonly long GiveUpAfter = Stopwatch.Frequency * 90;
    private const int MaxRetries = 5;

    private static PlayerName? goal;
    private static Vector3 destination;
    private static long startedAt;
    private static long pathedAt;
    private static int retries;
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
        if (!PathTo(position))
            return false;
        goal = who;
        startedAt = Stopwatch.GetTimestamp();
        retries = 0;
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
    }

    private static void GiveUp(string why)
    {
        Service.PluginLog.Information("Stopped walking to {Target}: {Reason}.", goal?.Name ?? "?", why);
        Cancel();
        FollowMode.ClearFollowing();
    }

    private static bool PathTo(Vector3 position)
    {
        try
        {
            var fly = Service.Condition[ConditionFlag.InFlight] || Service.Condition[ConditionFlag.Diving];
            var started = Service.PluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo")
                                 .InvokeFunc(position, fly);
            destination = position;
            pathedAt = Stopwatch.GetTimestamp();
            return started;
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "vnavmesh couldn't path there.");
            return false;
        }
    }

    private static bool IsMoving()
    {
        try
        {
            return Service.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning").InvokeFunc() ||
                   Service.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress").InvokeFunc();
        }
        catch
        {
            return false;
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
            if (character != null && Vector3.Distance(local.Position, position) <= ArriveDistance)
            {
                Cancel();
                FollowMode.TargetAndFollow(character, who.Name);
                return;
            }

            if (Vector3.Distance(position, destination) > RepathDistance && now - pathedAt > RepathEvery)
            {
                PathTo(position);
            }
            else if (!IsMoving() && now - pathedAt > RetryEvery)
            {
                // The path ended (or never started) short of them.
                if (++retries > MaxRetries)
                {
                    GiveUp("vnavmesh can't reach them");
                    return;
                }
                PathTo(position);
            }
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "Walking to {Target} failed.", who.Name);
            Cancel();
            FollowMode.ClearFollowing();
        }
    }
}
