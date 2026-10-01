using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using ECommons.Automation;
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
    // A sender can be answered by tell (or have a stop honored) at most this often.
    private static readonly long ReplyCooldown = 10 * Stopwatch.Frequency;
    private static readonly long StopCooldown = 2 * Stopwatch.Frequency;
    private static readonly Dictionary<string, long> NextReply = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, long> NextStop = new(StringComparer.OrdinalIgnoreCase);
    // Farther than this, walk there first (when vnavmesh can).
    private const float WalkFrom = 20f;

    // Who we were last told to follow (shown in the sidebar until a stop).
    public static string? Following { get; private set; }

    public static void ClearFollowing() => Following = null;

    public static void Reset()
    {
        FollowNavigator.Cancel();
        MimicMode.Stop();
        NextReply.Clear();
        NextStop.Clear();
        Following = null;
    }

    // True when the line was a follow request Follow mode took (the triggers then leave it alone).
    public static bool TryHandle(XivChatType type, SeString sender, SeString message)
    {
        var configuration = Service.configuration;
        var settings = configuration?.Follow;
        if (configuration == null || settings == null || !settings.Enabled || !settings.Channels.Contains((int)type))
            return false;

        var text = ReactionCommandMatcher.SanitizeIncoming(message.ToString());
        var request = FollowParser.Parse(text, settings.CallNames, settings.FollowWords, settings.StopWords, settings.ComeWords,
                                         settings.MimicWords);
        if (request.Kind == FollowRequestKind.None)
            return false;

        var who = SenderResolver.FromChat(type, sender, message);
        if (who.IsSelf && configuration.IgnoreOwnMessages)
            return false;
        if (!settings.Senders.Allows(who) || SenderFilter.MatchesNamed(settings.NeverFrom, who))
        {
            Service.PluginLog.Debug("Follow request from {Sender} ignored: not allowed.", who.Name);
            return false;
        }

        if (request.Kind == FollowRequestKind.Stop)
            Stop(who, settings);
        else if (request.Kind == FollowRequestKind.Mimic)
            Mimic(who, request.Target, settings);
        else
            Follow(who, request.Target, settings);
        return true;
    }

    private static void Follow(SenderInfo who, string targetText, FollowSettings settings)
    {
        var requested = targetText.Length == 0
            ? new PlayerName(who.Name, who.World)
            : FollowParser.SplitName(targetText);
        var shownName = targetText.Length == 0 ? who.Name : targetText;
        if (requested.Name.Length == 0)
            return;

        var local = Service.ObjectTable.LocalPlayer;
        if (local == null)
            return;

        var candidates = Candidates(local);
        var names = new List<PlayerName>(candidates.Count);
        foreach (var candidate in candidates)
            names.Add(candidate.Name);
        var index = FollowParser.FindNearby(requested, names);
        if (index < 0)
        {
            ReplyNotNearby(who, shownName, settings);
            return;
        }

        var target = candidates[index];
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
            ReplyNotNearby(who, shownName, settings);
            return;
        }
        TargetAndFollow(target.Character, target.Name.Name);
    }

    // "Ami mimic me" / "Ami mimic Nova": from now on, copy that player's emotes.
    private static void Mimic(SenderInfo who, string targetText, FollowSettings settings)
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
            var candidates = Candidates(local);
            var names = new List<PlayerName>(candidates.Count);
            foreach (var candidate in candidates)
                names.Add(candidate.Name);
            var index = FollowParser.FindNearby(FollowParser.SplitName(targetText), names);
            if (index < 0)
            {
                ReplyNotNearby(who, targetText, settings);
                return;
            }
            leader = candidates[index].Name;
        }
        if (leader.Name.Length == 0)
            return;
        if (!FollowParser.MayFollow(leader, settings.OnlyFollow, settings.NeverFollow))
        {
            Service.PluginLog.Information("Not mimicking {Leader}: blocked by the follow lists.", leader.Name);
            return;
        }
        MimicMode.Start(leader);
    }

    // Players we could follow: everyone around, plus party members elsewhere in the zone (by their map position).
    private static List<(PlayerName Name, IPlayerCharacter? Character, Vector3 Position)> Candidates(IPlayerCharacter local)
    {
        var found = new List<(PlayerName Name, IPlayerCharacter? Character, Vector3 Position)>();
        foreach (var gameObject in Service.ObjectTable)
        {
            if (gameObject is not IPlayerCharacter player || player.GameObjectId == local.GameObjectId)
                continue;
            found.Add((new PlayerName(player.Name.TextValue, player.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty),
                       player, player.Position));
        }
        var territory = Service.ClientState.TerritoryType;
        var localName = local.Name.TextValue;
        foreach (var member in Service.PartyList)
        {
            if (member == null || member.Territory.RowId != territory)
                continue;
            var name = new PlayerName(member.Name.TextValue, member.World.ValueNullable?.Name.ExtractText() ?? string.Empty);
            if (name.Name.Length == 0 || name.Name == localName)
                continue;
            if (found.Exists(entry => entry.Name.Name.Equals(name.Name, StringComparison.OrdinalIgnoreCase) &&
                                      entry.Name.World.Equals(name.World, StringComparison.OrdinalIgnoreCase)))
                continue;
            found.Add((name, null, member.Position));
        }
        return found;
    }

    // Where that player is now (and their character, once they're close enough to see), or null when they've left.
    public static (Vector3 Position, IPlayerCharacter? Character)? Locate(PlayerName who, IPlayerCharacter local)
    {
        foreach (var candidate in Candidates(local))
        {
            if (candidate.Name.Name.Equals(who.Name, StringComparison.OrdinalIgnoreCase) &&
                (who.World.Length == 0 || candidate.Name.World.Equals(who.World, StringComparison.OrdinalIgnoreCase)))
                return (candidate.Position, candidate.Character);
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
        // "/follow <t>" (the game's own "my current target"), and only if the target is still that player.
        var targetId = character.GameObjectId;
        Service.TargetManager.Target = character;
        _ = Service.Framework.RunOnTick(() => SendFollow(targetId, name), TimeSpan.FromMilliseconds(150));
    }

    private static void SendFollow(ulong targetId, string name)
    {
        try
        {
            if (Service.TargetManager.Target?.GameObjectId != targetId)
            {
                Service.PluginLog.Information("Not following {Target}: the target changed before /follow.", name);
                return;
            }
            Chat.SendMessage("/follow <t>");
            Following = name;
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "Couldn't follow {Target}.", name);
        }
    }

    private static void ReplyNotNearby(SenderInfo who, string shownName, FollowSettings settings)
    {
        if (!settings.ReplyWhenNotNearby || who.IsSelf || who.Name.Length == 0 || who.World.Length == 0)
            return;
        var now = Stopwatch.GetTimestamp();
        var key = $"{who.Name}@{who.World}";
        if (NextReply.TryGetValue(key, out var allowedAt) && now < allowedAt)
            return;
        var reply = FollowParser.FormatReply(settings.NotNearbyMessage, shownName);
        if (reply.Length == 0 || !CommandRateLimiter.Shared.TryAcquire(now))
            return;
        NextReply[key] = now + ReplyCooldown;
        Prune(NextReply, now);
        try
        {
            Chat.SendMessage($"/tell {who.Name}@{who.World} {reply}");
        }
        catch (Exception ex)
        {
            // Too long or an invalid character in the custom message.
            Service.PluginLog.Warning(ex, "Couldn't send the not-nearby reply.");
        }
    }

    private static void Stop(SenderInfo who, FollowSettings settings)
    {
        var now = Stopwatch.GetTimestamp();
        var key = $"{who.Name}@{who.World}";
        if (NextStop.TryGetValue(key, out var allowedAt) && now < allowedAt)
            return;
        NextStop[key] = now + StopCooldown;
        Prune(NextStop, now);

        // Every trigger: running ones stop, waiting ones are dropped.
        foreach (var reaction in Service.configuration!.Reactions)
            ChatHandler.CancelReaction(reaction);
        FollowNavigator.Cancel();
        MimicMode.Stop();
        Following = null;

        // Stop is never rate limited: it's how you get your character back.
        if (settings.StopMoves)
        {
            Chat.SendMessage("/automove on");
            _ = Service.Framework.RunOnTick(StopMoving, TimeSpan.FromMilliseconds(150));
        }
        foreach (var command in settings.StopCommands)
        {
            var parsed = ReactionCommandMatcher.FormatCommand(command);
            if (parsed.Main.Length == 0 || !parsed.Main.StartsWith('/') ||
                Service.Commands.IsAlwaysBlocked(Service.Commands.Canonicalize(parsed.Main)))
                continue;
            try
            {
                Chat.SendMessage(parsed.ToString());
            }
            catch (Exception ex)
            {
                Service.PluginLog.Warning(ex, "Stop command {Command} failed.", command);
            }
        }
    }

    private static void StopMoving()
    {
        try
        {
            Chat.SendMessage("/automove off");
        }
        catch (Exception ex)
        {
            Service.PluginLog.Warning(ex, "Couldn't end the stop step.");
        }
    }

    private static void Prune(Dictionary<string, long> times, long now)
    {
        if (times.Count < 128)
            return;
        var expired = new List<string>();
        foreach (var (key, until) in times)
        {
            if (until <= now)
                expired.Add(key);
        }
        foreach (var key in expired)
            times.Remove(key);
    }
}
