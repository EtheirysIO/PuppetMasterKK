using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.Automation;
using System;
using System.Diagnostics;

namespace PuppetMasterKK;

// "Ami mimic me" (framework thread only): every emote the leader performs, you perform too, aimed at the same
// player or object (at the leader, when they aim it at you; at nobody when they aim it at nobody). Ends with the
// stop word, a new mimic request, or unloading.
internal static class MimicMode
{
    // The emote hook's "no target".
    private const ulong NoTarget = 0xE0000000;

    // Two players mimicking each other would copy each other forever: the same emote we just did, coming back from
    // the leader within the repeat guard, isn't copied again.
    private static string? lastCommand;
    private static long lastAt;

    public static PlayerName? Leader { get; private set; }

    public static bool IsActive => Leader != null;

    public static void Start(PlayerName leader)
    {
        Leader = leader;
        Service.PluginLog.Information("Mimicking {Leader}.", leader.Name);
    }

    public static void Stop()
    {
        Leader = null;
        // Copies still waiting out the delay are dropped.
        generation++;
    }

    private static int generation;

    public static bool IsLeader(SenderInfo who)
    {
        return Leader is { } leader && who.Name.Equals(leader.Name, StringComparison.OrdinalIgnoreCase) &&
               (leader.World.Length == 0 || who.World.Equals(leader.World, StringComparison.OrdinalIgnoreCase));
    }

    // The leader performed `command` aimed at targetId: copy it now, or after the delay.
    public static void Copy(IPlayerCharacter leader, string command, ulong targetId)
    {
        var delay = Service.configuration?.Mimic.DelaySeconds ?? 0f;
        if (delay <= 0f)
        {
            CopyNow(leader.GameObjectId, command, targetId);
            return;
        }
        var leaderId = leader.GameObjectId;
        var expected = generation;
        _ = Service.Framework.RunOnTick(() =>
        {
            if (expected == generation && IsActive)
                CopyNow(leaderId, command, targetId);
        }, TimeSpan.FromSeconds(Math.Min(delay, MimicSettings.MaxDelaySeconds)));
    }

    private static void CopyNow(ulong leaderId, string command, ulong targetId)
    {
        var settings = Service.configuration?.Mimic;
        var local = Service.ObjectTable.LocalPlayer;
        if (settings == null || local == null || Service.Condition[ConditionFlag.InCombat])
            return;
        if (Service.Commands.IsAlwaysBlocked(Service.Commands.Canonicalize(command)))
            return;
        var now = Stopwatch.GetTimestamp();
        var guard = (long)(Math.Max(0f, settings.RepeatGuardSeconds) * Stopwatch.Frequency);
        if (guard > 0 && lastCommand != null && now - lastAt < guard &&
            Service.Commands.Canonicalize(command) == Service.Commands.Canonicalize(lastCommand))
            return;
        if (!CommandRateLimiter.Shared.TryAcquire(now))
            return;
        lastCommand = command;
        lastAt = now;

        // Aim it where they aimed theirs. At us: back at them.
        if (targetId == 0 || targetId == NoTarget)
            Service.TargetManager.Target = null;
        else if (targetId == local.GameObjectId)
            Service.TargetManager.Target = Service.ObjectTable.SearchById(leaderId);
        else
            Service.TargetManager.Target = Service.ObjectTable.SearchById(targetId);

        Chat.SendMessage(settings.MotionOnly ? $"{command} motion" : command);
    }
}
