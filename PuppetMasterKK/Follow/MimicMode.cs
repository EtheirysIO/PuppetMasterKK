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
    // the leader this soon, isn't copied again.
    private static readonly long EchoWindow = Stopwatch.Frequency * 3;
    private static string? lastCommand;
    private static long lastAt;

    public static PlayerName? Leader { get; private set; }

    public static bool IsActive => Leader != null;

    public static void Start(PlayerName leader)
    {
        Leader = leader;
        Service.PluginLog.Information("Mimicking {Leader}.", leader.Name);
    }

    public static void Stop() => Leader = null;

    public static bool IsLeader(SenderInfo who)
    {
        return Leader is { } leader && who.Name.Equals(leader.Name, StringComparison.OrdinalIgnoreCase) &&
               (leader.World.Length == 0 || who.World.Equals(leader.World, StringComparison.OrdinalIgnoreCase));
    }

    // The leader performed `command` aimed at targetId.
    public static void Copy(IPlayerCharacter leader, string command, ulong targetId)
    {
        var settings = Service.configuration?.Follow;
        var local = Service.ObjectTable.LocalPlayer;
        if (settings == null || local == null || Service.Condition[ConditionFlag.InCombat])
            return;
        if (Service.Commands.IsAlwaysBlocked(Service.Commands.Canonicalize(command)))
            return;
        var now = Stopwatch.GetTimestamp();
        if (lastCommand != null && now - lastAt < EchoWindow &&
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
            Service.TargetManager.Target = leader;
        else
            Service.TargetManager.Target = Service.ObjectTable.SearchById(targetId);

        Chat.SendMessage(settings.MimicMotionOnly ? $"{command} motion" : command);
    }
}
