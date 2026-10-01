using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace PuppetMasterKK;

// Follow mode (framework thread only): "Ami follow <player|me>" and "Ami come" (the sender) target the player and
// send /follow, walking there with vnavmesh first when they're in the zone but too far away; "Ami stop" stops every
// trigger and takes one tiny automove step, which ends following, emote loops, sitting and lying down.
internal static class FollowMode
{
    // A sender is answered by tell at most this often, and anyone at all at most every AnyReplyCooldown.
    private static readonly long ReplyCooldown = 10 * Stopwatch.Frequency;
    private static readonly long AnyReplyCooldown = 3 * Stopwatch.Frequency;
    // Stop always stops; its step and stop commands are sent at most this often, whoever asks.
    private static readonly long StopCooldown = 2 * Stopwatch.Frequency;
    private static readonly Cooldowns NextReply = new();
    private static long nextAnyReply;
    private static long nextStopStep;
    // "/automove on" was sent and its "/automove off" hasn't been yet.
    private static bool stopStepPending;
    // Bumped by every stop: a /follow still waiting for its target is dropped.
    private static int followGeneration;
    // Farther than this, walk there first (when vnavmesh can).
    private const float WalkFrom = 20f;

    // Who we were last told to follow (shown in the sidebar until a stop).
    public static string? Following { get; private set; }

    public static void ClearFollowing() => Following = null;

    public static void Reset()
    {
        FollowNavigator.Cancel();
        MimicMode.Stop();
        followGeneration++;
        // Never leave the character running on: the step's "/automove off" may never come now.
        if (stopStepPending && Service.Framework.IsInFrameworkUpdateThread)
            StopMoving();
        stopStepPending = false;
        NextReply.Clear();
        nextAnyReply = 0;
        nextStopStep = 0;
        Following = null;
    }

    // True when the line was a follow or mimic request one of the modes took (the triggers then leave it alone).
    public static bool TryHandle(XivChatType type, SeString sender, SeString message)
    {
        var configuration = Service.configuration;
        if (configuration == null)
            return false;
        var text = ReactionCommandMatcher.SanitizeIncoming(message.ToString());
        return TryHandleFollow(configuration, type, sender, message, text) || TryHandleMimic(configuration, type, sender, message, text);
    }

    private static bool TryHandleMimic(Configuration configuration, XivChatType type, SeString sender, SeString message, string text)
    {
        var settings = configuration.Mimic;
        if (settings == null || !settings.Enabled || !settings.Channels.Contains((int)type))
            return false;
        var request = FollowParser.Parse(text, settings.CallNames, string.Empty, settings.StopWords, string.Empty, settings.MimicWords);
        if (request.Kind == FollowRequestKind.None ||
            AllowedSender(configuration, type, sender, message, settings.Senders, settings.NeverFrom, "Mimic") is not { } who)
            return false;

        if (request.Kind == FollowRequestKind.Stop)
            MimicMode.Stop();
        else
            Mimic(who, request.Target, settings);
        return true;
    }

    private static bool TryHandleFollow(Configuration configuration, XivChatType type, SeString sender, SeString message, string text)
    {
        var settings = configuration.Follow;
        if (settings == null || !settings.Enabled || !settings.Channels.Contains((int)type))
            return false;
        var request = FollowParser.Parse(text, settings.CallNames, settings.FollowWords, settings.StopWords, settings.ComeWords);
        if (request.Kind == FollowRequestKind.None ||
            AllowedSender(configuration, type, sender, message, settings.Senders, settings.NeverFrom, "Follow") is not { } who)
            return false;

        if (request.Kind == FollowRequestKind.Stop)
            Stop(configuration, settings);
        else if (Service.Condition[ConditionFlag.InCombat])
            Service.PluginLog.Information("Follow request from {Sender} ignored: in combat.", who.Name);
        else
            Follow(who, request.Target, settings);
        return true;
    }

    // Who sent the request, when they may make it.
    private static SenderInfo? AllowedSender(Configuration configuration, XivChatType type, SeString sender, SeString message,
                                             SenderFilter senders, List<string> neverFrom, string mode)
    {
        var who = SenderResolver.FromChat(type, sender, message);
        if (who.IsSelf && configuration.IgnoreOwnMessages)
            return null;
        if (!senders.Allows(who) || SenderFilter.MatchesNamed(neverFrom, who))
        {
            Service.PluginLog.Debug("{Mode} request from {Sender} ignored: not allowed.", mode, who.Name);
            return null;
        }
        return who;
    }

    private static void Follow(SenderInfo who, string targetText, FollowSettings settings)
    {
        var shownName = targetText.Length == 0 ? who.Name : targetText;
        var local = Service.ObjectTable.LocalPlayer;
        if (local == null)
            return;
        var requested = targetText.Length == 0 ? new PlayerName(who.Name, who.World) : FollowParser.SplitName(targetText);
        if (requested.Name.Length == 0)
            return;
        if (FindCandidate(requested, local, out var ambiguous) is not { } target)
        {
            NotFound(who, shownName, ambiguous, settings.ReplyWhenNotNearby, settings.NotNearbyMessage);
            return;
        }
        if (!FollowParser.MayFollow(target.Name, settings.OnlyFollow, settings.NeverFollow))
        {
            Service.PluginLog.Information("Not following {Target}: blocked by the follow lists.", target.Name.Name);
            return;
        }

        // A new request replaces any walk in progress.
        FollowNavigator.Cancel();
        if (settings.WalkWithVnavmesh && Vector3.Distance(local.Position, target.Position) > WalkFrom &&
            FollowNavigator.IsAvailable() && FollowNavigator.Start(target.Name, target.Position))
        {
            Following = target.Name.Name;
            return;
        }
        if (target.Character == null)
        {
            // In the zone (a party member) but too far to see, and no way to walk there.
            ReplyNotNearby(who, shownName, settings.ReplyWhenNotNearby, settings.NotNearbyMessage);
            return;
        }
        TargetAndFollow(target.Character, target.Name.Name);
    }

    // "Ami mimic me" / "Ami mimic Nova": from now on, copy that player's emotes.
    private static void Mimic(SenderInfo who, string targetText, MimicSettings settings)
    {
        PlayerName leader;
        if (targetText.Length == 0)
        {
            leader = new PlayerName(who.Name, who.World);
        }
        else
        {
            // A named player has to be around (their emotes are only seen nearby anyway).
            var local = Service.ObjectTable.LocalPlayer;
            if (local == null)
                return;
            if (FindCandidate(FollowParser.SplitName(targetText), local, out var ambiguous) is not { } found)
            {
                NotFound(who, targetText, ambiguous, settings.ReplyWhenNotNearby, settings.NotNearbyMessage);
                return;
            }
            leader = found.Name;
        }
        // Without a world, anyone with that name on any world would lead.
        if (leader.Name.Length == 0 || leader.World.Length == 0)
        {
            Service.PluginLog.Debug("Not mimicking {Leader}: the game doesn't show their world.", leader.Name);
            return;
        }
        if (!FollowParser.MayFollow(leader, settings.OnlyMimic, settings.NeverMimic))
        {
            Service.PluginLog.Information("Not mimicking {Leader}: blocked by the mimic lists.", leader.Name);
            return;
        }
        MimicMode.Start(leader);
    }

    private readonly record struct Candidate(PlayerName Name, IPlayerCharacter? Character, Vector3 Position);

    // Several players match the name: "I don't see them" would be wrong, so just log it.
    private static void NotFound(SenderInfo who, string shownName, bool ambiguous, bool reply, string template)
    {
        if (ambiguous)
            Service.PluginLog.Information("More than one {Name} nearby; ask with Name@World.", shownName);
        else
            ReplyNotNearby(who, shownName, reply, template);
    }

    // The requested player among the candidates (see FollowParser.FindNearby), or null.
    private static Candidate? FindCandidate(PlayerName requested, IPlayerCharacter local, out bool ambiguous)
    {
        ambiguous = false;
        if (requested.Name.Length == 0)
            return null;
        var candidates = Candidates(local);
        var names = new List<PlayerName>(candidates.Count);
        foreach (var candidate in candidates)
            names.Add(candidate.Name);
        var index = FollowParser.FindNearby(requested, names, out ambiguous);
        return index < 0 ? null : candidates[index];
    }

    // Players we could follow: everyone around, plus party members elsewhere in the zone (by their map position).
    private static List<Candidate> Candidates(IPlayerCharacter local)
    {
        var found = new List<Candidate>();
        foreach (var gameObject in Service.ObjectTable)
        {
            if (gameObject is not IPlayerCharacter player || player.GameObjectId == local.GameObjectId)
                continue;
            found.Add(new Candidate(new PlayerName(player.Name.TextValue, WorldName(player)), player, player.Position));
        }
        var territory = Service.ClientState.TerritoryType;
        var localName = local.Name.TextValue;
        foreach (var member in Service.PartyList)
        {
            if (member == null || member.Territory.RowId != territory)
                continue;
            var name = new PlayerName(member.Name.TextValue, member.World.ValueNullable?.Name.ExtractText() ?? string.Empty);
            if (name.Name.Length == 0 || name.Name == localName ||
                found.Exists(entry => entry.Name.Name.Equals(name.Name, StringComparison.OrdinalIgnoreCase) &&
                                      entry.Name.World.Equals(name.World, StringComparison.OrdinalIgnoreCase)))
                continue;
            found.Add(new Candidate(name, null, member.Position));
        }
        return found;
    }

    private static string WorldName(IPlayerCharacter player) => player.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty;

    private static bool WorldMatches(PlayerName who, string world) =>
        who.World.Length == 0 || world.Equals(who.World, StringComparison.OrdinalIgnoreCase);

    // Where that player is now (and their character, once they're close enough to see), or null when they've left.
    // Runs every frame while walking, so it only reads a world name for a player whose name matches.
    public static (Vector3 Position, IPlayerCharacter? Character)? Locate(PlayerName who, IPlayerCharacter local)
    {
        foreach (var gameObject in Service.ObjectTable)
        {
            if (gameObject is IPlayerCharacter player && player.GameObjectId != local.GameObjectId &&
                player.Name.TextValue.Equals(who.Name, StringComparison.OrdinalIgnoreCase) && WorldMatches(who, WorldName(player)))
                return (player.Position, player);
        }
        var territory = Service.ClientState.TerritoryType;
        foreach (var member in Service.PartyList)
        {
            if (member != null && member.Territory.RowId == territory &&
                member.Name.TextValue.Equals(who.Name, StringComparison.OrdinalIgnoreCase) &&
                WorldMatches(who, member.World.ValueNullable?.Name.ExtractText() ?? string.Empty))
                return (member.Position, null);
        }
        return null;
    }

    public static void TargetAndFollow(IPlayerCharacter character, string name)
    {
        if (!CommandRateLimiter.Shared.TryAcquire(Stopwatch.GetTimestamp()))
        {
            Service.PluginLog.Debug("Follow request for {Target} dropped: sending too fast.", name);
            return;
        }

        // Target first. The game takes the new target on a later frame, so /follow goes out a moment after, as
        // "/follow <t>" (the game's own "my current target"), and only if the target is still that player and no stop
        // came in between.
        var targetId = character.GameObjectId;
        var expected = followGeneration;
        Service.TargetManager.Target = character;
        _ = Service.Framework.RunOnTick(() => SendFollow(targetId, name, expected), TimeSpan.FromMilliseconds(150));
    }

    private static void SendFollow(ulong targetId, string name, int expected)
    {
        try
        {
            if (expected != followGeneration || Service.configuration?.Follow?.Enabled != true)
                return;
            if (Service.TargetManager.Target?.GameObjectId != targetId)
            {
                Service.PluginLog.Information("Not following {Target}: the target changed before /follow.", name);
                return;
            }
            if (TrySend("/follow <t>", "follow"))
                Following = name;
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "Couldn't follow {Target}.", name);
        }
    }

    // The tell back is the requester's own (so it goes to nobody else), limited per player and overall.
    private static void ReplyNotNearby(SenderInfo who, string shownName, bool enabled, string template)
    {
        if (!enabled || who.IsSelf || who.Name.Length == 0 || who.World.Length == 0)
            return;
        var now = Stopwatch.GetTimestamp();
        var key = who.Key;
        if (now < nextAnyReply || NextReply.IsWaiting(key, now))
            return;
        var reply = FollowParser.FormatReply(template, shownName);
        if (reply.Length == 0 || !CommandRateLimiter.Shared.TryAcquire(now))
            return;
        NextReply.Start(key, now, ReplyCooldown);
        nextAnyReply = now + AnyReplyCooldown;
        // Fails (logged) when the custom message is too long or has a character chat can't send.
        TrySend($"/tell {key} {reply}", "the not-nearby reply");
    }

    private static void Stop(Configuration configuration, FollowSettings settings)
    {
        ChatHandler.CancelAll(configuration);
        FollowNavigator.Cancel();
        MimicMode.Stop();
        followGeneration++;
        Following = null;

        // Stop isn't held back by the shared rate limit (it's how you get your character back), but a flood of stops
        // sends the step and the stop commands only once in a while.
        var now = Stopwatch.GetTimestamp();
        if (now < nextStopStep)
            return;
        nextStopStep = now + StopCooldown;
        if (settings.StopMoves && TrySend("/automove on", "the stop step"))
        {
            stopStepPending = true;
            _ = Service.Framework.RunOnTick(StopMoving, TimeSpan.FromMilliseconds(150));
        }
        foreach (var command in settings.StopCommands)
        {
            if (string.IsNullOrWhiteSpace(command))
                continue;
            var parsed = ReactionCommandMatcher.FormatCommand(command);
            if (parsed.Main.Length == 0 || !parsed.Main.StartsWith('/') ||
                Service.Commands.IsAlwaysBlocked(Service.Commands.Canonicalize(parsed.Main)))
                continue;
            TrySend(parsed.ToString(), $"stop command {command}");
        }
    }

    private static void StopMoving()
    {
        if (!stopStepPending)
            return;
        stopStepPending = false;
        TrySend("/automove off", "the end of the stop step");
    }

    // Sends a line; false (logged) when chat refused it.
    private static bool TrySend(string line, string what)
    {
        try
        {
            GameChat.Send(line);
            return true;
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "Couldn't send {What}.", what);
            return false;
        }
    }
}
