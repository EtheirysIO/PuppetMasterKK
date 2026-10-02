using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using System;
using System.Diagnostics;

namespace PuppetMasterKK;

// "Ami mimic me" (framework thread only): every emote the leader performs, you perform too, aimed at the same
// player or object (at the leader, when they aim it at you; at nobody when they aim it at nobody). Ends with the
// stop word, a new mimic request, turning Mimic off, or unloading.
internal static class MimicMode
{
    // The emote hook's "no target".
    private const ulong NoTarget = 0xE0000000;

    // Two players mimicking each other would copy each other forever: the same emote we just did, coming back from
    // the leader within the repeat guard, isn't copied again.
    private static string? lastCommand;
    private static long lastAt;
    // Bumped on every start and stop: copies still waiting out the delay are dropped.
    private static int generation;
    // Bumped on every copy: only the last copy's re-follow goes out.
    private static int copies;
    // An emote ends /follow: after copying one, follow the leader again this much later (once the emote is out).
    private static readonly TimeSpan RefollowAfter = TimeSpan.FromSeconds(1);

    public static PlayerName? Leader { get; private set; }

    public static bool IsActive => Leader != null && Service.configuration?.Mimic?.Enabled == true;

    public static void Start(PlayerName leader)
    {
        Leader = leader;
        generation++;
        lastCommand = null;
        Service.PluginLog.Information("Mimicking {Leader}.", leader.Name);
    }

    public static void Stop()
    {
        Leader = null;
        generation++;
    }

    public static bool IsLeader(SenderInfo who)
    {
        return IsActive && Leader is { } leader && who.Name.Equals(leader.Name, StringComparison.OrdinalIgnoreCase) &&
               who.World.Equals(leader.World, StringComparison.OrdinalIgnoreCase);
    }

    // The leader performed `command` aimed at targetId: copy it now, or after the delay.
    public static void Copy(IPlayerCharacter leader, string command, ulong targetId)
    {
        var settings = Service.configuration?.Mimic;
        if (settings == null)
            return;
        var delay = Math.Clamp(settings.DelaySeconds, 0f, MimicSettings.MaxDelaySeconds);
        // A partner mimicking us with the same delay sends our emote back that much later.
        if (IsRepeat(command, settings.RepeatGuardSeconds, delay))
            return;
        var leaderId = leader.GameObjectId;
        if (delay <= 0f)
        {
            CopyNow(leaderId, command, targetId);
            return;
        }
        var expected = generation;
        _ = Service.Framework.RunOnTick(() =>
        {
            if (expected == generation)
                CopyNow(leaderId, command, targetId);
        }, TimeSpan.FromSeconds(delay));
    }

    // The emote we copied last, again within guardSeconds (plus extraSeconds) of copying it.
    private static bool IsRepeat(string command, float guardSeconds, float extraSeconds = 0f)
    {
        if (lastCommand == null || guardSeconds <= 0f)
            return false;
        var window = guardSeconds + extraSeconds;
        return Stopwatch.GetTimestamp() - lastAt < (long)(window * Stopwatch.Frequency) &&
               Service.Commands.Canonicalize(command) == Service.Commands.Canonicalize(lastCommand);
    }

    private static void CopyNow(ulong leaderId, string command, ulong targetId)
    {
        try
        {
            var settings = Service.configuration?.Mimic;
            var local = Service.ObjectTable.LocalPlayer;
            if (settings == null || !IsActive || local == null || Service.Condition[ConditionFlag.InCombat])
                return;
            // Only emotes, whatever the sheet says.
            if (!Service.Commands.IsEmote(command) || Service.Commands.IsAlwaysBlocked(Service.Commands.Canonicalize(command)))
                return;
            // Repeats queued during the delay collapse into one copy.
            if (IsRepeat(command, settings.RepeatGuardSeconds))
                return;
            var now = Stopwatch.GetTimestamp();
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

            GameChat.Send(CommandPolicy.EmoteLine(command, settings.MotionOnly));

            if (settings.FollowLeader)
            {
                var expected = generation;
                var copy = ++copies;
                _ = Service.Framework.RunOnTick(() =>
                {
                    if (expected == generation && copy == copies)
                        FollowMode.FollowMimicLeaderAgain(leaderId);
                }, RefollowAfter);
            }
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "Couldn't copy {Command}.", command);
        }
    }
}
