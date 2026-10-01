using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using ECommons.Automation;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PuppetMasterKK;

// Follow mode (framework thread only): "Ami follow [player]" targets the player (or the sender) and sends /follow;
// "Ami stop" stops every trigger and takes one tiny automove step, which ends following, emote loops, sitting and
// lying down.
internal static class FollowMode
{
    // A sender can be answered by tell (or have a stop honored) at most this often.
    private static readonly long ReplyCooldown = 10 * Stopwatch.Frequency;
    private static readonly long StopCooldown = 2 * Stopwatch.Frequency;
    private static readonly Dictionary<string, long> NextReply = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, long> NextStop = new(StringComparer.OrdinalIgnoreCase);

    // Who we were last told to follow (shown in the sidebar until a stop).
    public static string? Following { get; private set; }

    public static void Reset()
    {
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
        var request = FollowParser.Parse(text, settings.CallNames, settings.FollowWords, settings.StopWords);
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

        // Who's around (other players only).
        var nearby = new List<PlayerName>();
        var characters = new List<IPlayerCharacter>();
        foreach (var gameObject in Service.ObjectTable)
        {
            if (gameObject is not IPlayerCharacter player || player.GameObjectId == local.GameObjectId)
                continue;
            nearby.Add(new PlayerName(player.Name.TextValue, player.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty));
            characters.Add(player);
        }

        var index = FollowParser.FindNearby(requested, nearby);
        if (index < 0)
        {
            ReplyNotNearby(who, shownName, settings);
            return;
        }

        var target = nearby[index];
        if (!FollowParser.MayFollow(target, settings.OnlyFollow, settings.NeverFollow))
        {
            Service.PluginLog.Information("Not following {Target}: blocked by the follow lists.", target.Name);
            return;
        }

        // Targeting and /follow go together on this tick: /follow follows the current target.
        if (!CommandRateLimiter.Shared.TryAcquire(Stopwatch.GetTimestamp()))
        {
            Service.PluginLog.Debug("Follow request for {Target} dropped: sending too fast.", target.Name);
            return;
        }
        Service.TargetManager.Target = characters[index];
        Chat.SendMessage("/follow");
        Following = target.Name;
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
