using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Hooking;
using ECommons.Automation;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PuppetMaster;

// Answers an emote aimed at you with the same emote (formerly the separate "Right Back At You" plugin).
//
// The hook only notes what happened and always calls the original: everything else (looking the player up, the
// sender filter, targeting, sending the emote) runs on the next framework tick, where an exception can't crash the
// game and the game has finished handling its own packet.
internal sealed class EmoteReplies : IDisposable
{
    private const string EmoteSignature = "E8 ?? ?? ?? ?? 48 8D 8B ?? ?? ?? ?? 4C 89 74 24";

    private delegate void OnEmoteDelegate(ulong unk, ulong instigatorAddress, ushort emoteId, ulong targetId, ulong unk2);

    private readonly Hook<OnEmoteDelegate>? hook;
    // Framework thread only. Player "Name@World" -> earliest timestamp we'll answer them again.
    private readonly Dictionary<string, long> nextReply = new(StringComparer.OrdinalIgnoreCase);
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
            UnavailableReason = "The game's emote function wasn't found. A game update probably moved it; emote replies are off until Puppet Master is updated.";
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
            if (!disposed && Service.configuration?.EmoteReplies.Enabled == true && instigatorAddress != 0)
            {
                var local = Service.ObjectTable.LocalPlayer;
                if (local != null && targetId == local.GameObjectId)
                {
                    var address = (nint)instigatorAddress;
                    _ = Service.Framework.RunOnTick(() => Reply(address, emoteId));
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

    private void Reply(nint instigatorAddress, ushort emoteId)
    {
        try
        {
            var settings = Service.configuration?.EmoteReplies;
            if (disposed || settings == null || !settings.Enabled)
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
            if (!settings.Senders.Allows(sender))
                return;

            // Per-player cooldown: two players who both reply to emotes stop after one round.
            var key = $"{sender.Name}@{sender.World}";
            var now = Stopwatch.GetTimestamp();
            if (nextReply.TryGetValue(key, out var allowedAt) && now < allowedAt)
                return;

            var command = Service.DataManager.GetExcelSheet<Emote>()
                .GetRowOrDefault(emoteId)?.TextCommand.ValueNullable?.Command.ExtractText();
            // An emote with no text command has nothing to send (sending "" or " motion" would post plain chat).
            if (string.IsNullOrWhiteSpace(command) || !command.StartsWith('/'))
                return;

            if (!CommandRateLimiter.Shared.TryAcquire(now))
                return;

            nextReply[key] = now + Math.Max(0, settings.PerPlayerCooldownSeconds) * Stopwatch.Frequency;
            PruneCooldowns(now);

            if (settings.TargetBack)
                Service.TargetManager.Target = instigator;
            Chat.SendMessage(settings.MotionOnly ? $"{command} motion" : command);
            RepliesSent++;
        }
        catch (Exception ex)
        {
            Service.PluginLog.Error(ex, "Emote reply failed.");
        }
    }

    private void PruneCooldowns(long now)
    {
        if (nextReply.Count < 128)
            return;
        var expired = new List<string>();
        foreach (var (key, allowedAt) in nextReply)
        {
            if (allowedAt <= now)
                expired.Add(key);
        }
        foreach (var key in expired)
            nextReply.Remove(key);
    }
}
