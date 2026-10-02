using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Hooking;
using Lumina.Excel.Sheets;
using System;
using System.Diagnostics;

namespace PuppetMasterKK;

// Answers an emote aimed at you with the same emote, or the one set to replace it (formerly the separate "Right Back
// At You" plugin). The same hook feeds Mimic mode: the leader's emotes, whoever they're aimed at.
//
// The hook only notes what happened and always calls the original: everything else (looking the player up, the
// sender filter, targeting, sending the emote) runs on the next framework tick, where an exception can't crash the
// game and the game has finished handling its own packet.
internal sealed class EmoteReplies : IDisposable
{
    private const string EmoteSignature = "E8 ?? ?? ?? ?? 48 8D 8B ?? ?? ?? ?? 4C 89 74 24";

    private delegate void OnEmoteDelegate(ulong unk, ulong instigatorAddress, ushort emoteId, ulong targetId, ulong unk2);

    private readonly Hook<OnEmoteDelegate>? hook;
    // Framework thread only. Player "Name@World" -> when we'll answer them again.
    private readonly Cooldowns nextReply = new();
    private volatile bool disposed;

    public string? UnavailableReason { get; }
    public bool Available => hook != null;
    public long RepliesSent { get; private set; }

    public EmoteReplies()
    {
        try
        {
            hook = Service.GameInterop.HookFromSignature<OnEmoteDelegate>(EmoteSignature, OnEmoteDetour);
            hook.Enable();
        }
        catch (Exception ex)
        {
            hook?.Dispose();
            hook = null;
            UnavailableReason = "The game's emote function wasn't found. A game update probably moved it; emote replies are off until PuppetMasterKK is updated.";
            Service.PluginLog.Warning(ex, "Emote replies unavailable: emote signature not found.");
        }
    }

    public void Dispose()
    {
        disposed = true;
        hook?.Dispose();
    }

    private void OnEmoteDetour(ulong unk, ulong instigatorAddress, ushort emoteId, ulong targetId, ulong unk2)
    {
        try
        {
            var replying = Service.configuration?.EmoteReplies.Enabled == true;
            var mimicking = MimicMode.IsActive;
            if (!disposed && (replying || mimicking) && instigatorAddress != 0)
            {
                var local = Service.ObjectTable.LocalPlayer;
                if (local != null && (mimicking || targetId == local.GameObjectId))
                {
                    var address = (nint)instigatorAddress;
                    _ = Service.Framework.RunOnTick(() => Handle(address, emoteId, targetId));
                }
            }
        }
        catch (Exception ex)
        {
            // Nothing may escape a native detour.
            Service.PluginLog.Error(ex, "Emote reply detour failed.");
        }
        finally
        {
            hook!.Original(unk, instigatorAddress, emoteId, targetId, unk2);
        }
    }

    // The emote to answer `command` with: the override's reply (or the same emote), or null when it isn't answered
    // (an empty override, not an emote, or on the Never reply with list).
    private static string? ReplyTo(EmoteReplySettings settings, string command)
    {
        var reply = EmoteReplySettings.ReplyFor(settings.Overrides, command, Service.Commands.Canonicalize);
        if (reply.Length == 0 || !reply.StartsWith('/') || !Service.Commands.IsEmote(reply))
            return null;
        var canonical = Service.Commands.Canonicalize(reply);
        foreach (var blockedEmote in settings.BlockedEmotes)
        {
            if (!string.IsNullOrWhiteSpace(blockedEmote) && Service.Commands.Canonicalize(blockedEmote) == canonical)
                return null;
        }
        return reply;
    }

    private void Handle(nint instigatorAddress, ushort emoteId, ulong targetId)
    {
        try
        {
            var settings = Service.configuration?.EmoteReplies;
            if (disposed || settings == null)
                return;
            // Never mid-fight: answering would change your target.
            if (Service.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat])
                return;
            var local = Service.ObjectTable.LocalPlayer;
            if (local == null)
                return;

            IPlayerCharacter? instigator = null;
            foreach (var gameObject in Service.ObjectTable)
            {
                if (gameObject.Address == instigatorAddress)
                {
                    instigator = gameObject as IPlayerCharacter;
                    break;
                }
            }
            if (instigator == null || instigator.ObjectKind != ObjectKind.Pc || instigator.GameObjectId == local.GameObjectId)
                return;

            var sender = SenderResolver.FromCharacter(instigator);
            var command = EmoteCommand(emoteId);
            // An emote with no text command has nothing to send (sending "" or " motion" would post plain chat).
            if (command == null)
                return;

            // The player we're mimicking: copy it. One aimed at us is answered by our emote reply rules (a different
            // emote, or none), then sent back at them by Mimic.
            if (MimicMode.IsLeader(sender))
            {
                if (settings.Enabled && targetId == local.GameObjectId)
                {
                    if (ReplyTo(settings, command) is not { } answer)
                        return;
                    command = answer;
                }
                MimicMode.Copy(instigator, command, targetId);
                return;
            }
            if (!settings.Enabled || targetId != local.GameObjectId || !settings.Senders.Allows(sender))
                return;

            // Per-player cooldown: two players who both reply to emotes stop after one round.
            var key = sender.Key;
            var now = Stopwatch.GetTimestamp();
            if (nextReply.IsWaiting(key, now))
                return;

            if (ReplyTo(settings, command) is not { } replyCommand)
                return;
            command = replyCommand;

            if (!CommandRateLimiter.Shared.TryAcquire(now))
                return;

            nextReply.Start(key, now, Math.Max(EmoteReplySettings.MinimumCooldownSeconds, settings.PerPlayerCooldownSeconds) * Stopwatch.Frequency);

            if (settings.TargetBack)
                Service.TargetManager.Target = instigator;
            GameChat.Send(CommandPolicy.EmoteLine(command, settings.MotionOnly));
            RepliesSent++;
        }
        catch (Exception ex)
        {
            Service.PluginLog.Error(ex, "Emote reply failed.");
        }
    }

    private static string? EmoteCommand(ushort emoteId)
    {
        var command = Service.DataManager.GetExcelSheet<Emote>()
            .GetRowOrDefault(emoteId)?.TextCommand.ValueNullable?.Command.ExtractText();
        return string.IsNullOrWhiteSpace(command) || !command.StartsWith('/') ? null : command;
    }
}
