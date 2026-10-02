using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Diagnostics;
using GameCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace PuppetMasterKK;

// "Ami mimic me" (framework thread only): every emote the leader performs, you perform too, aimed at the same
// player or object (at the leader, when they aim it at you; at nobody when they aim it at nobody). Ends with the
// stop word, a new mimic request, turning Mimic off, or unloading.
internal static unsafe class MimicMode
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

    // Jumps: the leader is watched every frame while mimicking, and a jump starting is copied.
    private const uint JumpGeneralAction = 2;
    private static readonly long LookupEvery = Stopwatch.Frequency / 2;
    private static readonly long MinJumpGap = Stopwatch.Frequency * 2 / 5;
    private static bool watching;
    private static ulong leaderObjectId;
    private static long nextLookup;
    private static bool leaderWasJumping;
    private static long lastJumpAt;

    public static PlayerName? Leader { get; private set; }

    public static bool IsActive => Leader != null && Service.configuration?.Mimic?.Enabled == true;

    public static void Start(PlayerName leader)
    {
        Leader = leader;
        generation++;
        lastCommand = null;
        leaderObjectId = 0;
        nextLookup = 0;
        leaderWasJumping = false;
        lastJumpAt = 0;
        Watch(true);
        Service.PluginLog.Information("Mimicking {Leader}.", leader.Name);
    }

    public static void Stop()
    {
        Leader = null;
        generation++;
        Watch(false);
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
                ScheduleRefollow(leaderId);
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "Couldn't copy {Command}.", command);
        }
    }

    // Only the last copy's re-follow goes out.
    private static void ScheduleRefollow(ulong leaderId)
    {
        var expected = generation;
        var copy = ++copies;
        _ = Service.Framework.RunOnTick(() =>
        {
            if (expected == generation && copy == copies)
                FollowMode.FollowMimicLeaderAgain(leaderId);
        }, RefollowAfter);
    }

    private static void Watch(bool on)
    {
        if (on == watching)
            return;
        watching = on;
        if (on)
            Service.Framework.Update += Tick;
        else
            Service.Framework.Update -= Tick;
    }

    // Every frame while mimicking: copy the leader's jump as it starts.
    private static void Tick(IFramework framework)
    {
        try
        {
            var settings = Service.configuration?.Mimic;
            if (settings == null || !IsActive)
            {
                Watch(false);
                return;
            }
            var local = Service.ObjectTable.LocalPlayer;
            if (!settings.CopyJumps || local == null || Leader is not { } leader || FindLeader(leader, local) is not { } character)
            {
                leaderWasJumping = false;
                return;
            }
            var jumping = ((GameCharacter*)character.Address)->IsJumping();
            if (jumping && !leaderWasJumping)
                CopyJump(character.GameObjectId, settings);
            leaderWasJumping = jumping;
        }
        catch (Exception ex)
        {
            // Never every frame: stop watching jumps until the next mimic.
            Watch(false);
            Service.PluginLog.Warning(ex, "Stopped copying jumps.");
        }
    }

    // The leader, when close enough to see: by the remembered object id, or looked up twice a second.
    private static IPlayerCharacter? FindLeader(PlayerName leader, IPlayerCharacter local)
    {
        if (leaderObjectId != 0 && Service.ObjectTable.SearchById(leaderObjectId) is IPlayerCharacter known &&
            known.Name.TextValue.Equals(leader.Name, StringComparison.OrdinalIgnoreCase))
            return known;
        leaderObjectId = 0;
        var now = Stopwatch.GetTimestamp();
        if (now < nextLookup)
            return null;
        nextLookup = now + LookupEvery;
        if (FollowMode.Locate(leader, local)?.Character is not { } found)
            return null;
        leaderObjectId = found.GameObjectId;
        return found;
    }

    private static void CopyJump(ulong leaderId, MimicSettings settings)
    {
        var delay = Math.Clamp(settings.DelaySeconds, 0f, MimicSettings.MaxDelaySeconds);
        // Our own jump coming back from a partner who mimics us: the same guard as emotes.
        if (settings.RepeatGuardSeconds > 0f && lastJumpAt != 0 &&
            Stopwatch.GetTimestamp() - lastJumpAt < (long)((settings.RepeatGuardSeconds + delay) * Stopwatch.Frequency))
            return;
        if (delay <= 0f)
        {
            JumpNow(leaderId);
            return;
        }
        var expected = generation;
        _ = Service.Framework.RunOnTick(() =>
        {
            if (expected == generation)
                JumpNow(leaderId);
        }, TimeSpan.FromSeconds(delay));
    }

    private static void JumpNow(ulong leaderId)
    {
        try
        {
            var settings = Service.configuration?.Mimic;
            var now = Stopwatch.GetTimestamp();
            if (settings == null || !IsActive || !settings.CopyJumps || Service.ObjectTable.LocalPlayer == null ||
                Service.Condition[ConditionFlag.InCombat] || now - lastJumpAt < MinJumpGap)
                return;
            var actions = ActionManager.Instance();
            if (actions == null || !actions->UseAction(ActionType.GeneralAction, JumpGeneralAction))
                return;
            lastJumpAt = now;
            if (settings.FollowLeader)
                ScheduleRefollow(leaderId);
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "Couldn't copy a jump.");
        }
    }
}
