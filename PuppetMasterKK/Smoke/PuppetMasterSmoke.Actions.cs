using System;
using System.Collections.Generic;
using System.Diagnostics;
using Dalamud.Game.ClientState.Conditions;
using phys1ksTest;

namespace PuppetMasterKK.Smoke;

// Tests that do something small in game (a wave, a walk toggle, a jump). They run with the auto tests after login,
// and skip (not fail) when you are in combat, mounted or otherwise busy.
internal static partial class PuppetMasterSmoke
{
    private static void RegisterActions(SmokeSuite suite)
    {
        suite.Add(new SmokeTest
        {
            Id = "emote.self",
            Group = "Emotes",
            Title = "Emote hook sees your /wave",
            Kind = TestKind.Auto,
            Description = "Clears your target, then sends /wave through GameChat.Send exactly as a typed line, so you " +
                          "wave and everyone nearby sees 'You wave.' in the emote log (like /say range). Passes when the " +
                          "emote hook is called with you as the instigator, the Wave emote id, and no target. Puts your " +
                          "previous target back afterwards. Uses one slot of the plugin's shared command rate limit.",
            Precondition = OnFoot,
            TimeoutMs = 30_000,
            Body = t => SendWave(t, targetSelf: false),
        });
        suite.Add(new SmokeTest
        {
            Id = "emote.target",
            Group = "Emotes",
            Title = "Emote hook target id",
            Kind = TestKind.Auto,
            Description = "Targets yourself, then sends /wave (visible to everyone nearby, like emote.self). Emote replies " +
                          "only answer an emote whose target id equals your GameObjectId, so this checks the hook's " +
                          "target argument is still in that form. Skipped if the game sends a self-targeted emote with " +
                          "no target (then there's nothing to compare). Puts your previous target back afterwards.",
            Precondition = OnFoot,
            TimeoutMs = 30_000,
            Body = t => SendWave(t, targetSelf: true),
        });
        suite.Add(new SmokeTest
        {
            Id = "walk.toggle",
            Group = "Follow",
            Title = "Walk toggle can be flipped",
            Kind = TestKind.Auto,
            Description = "Pace matching writes Control.IsWalking. Flips your walk/run toggle, checks it reads back " +
                          "flipped on the next frame, then sets it back to what it was and checks that too. Nothing " +
                          "else changes; you don't move. Not while Follow or Mimic pace matching is running (it would " +
                          "fight over the toggle).",
            Precondition = WalkToggleFree,
            TimeoutMs = 15_000,
            Body = WalkToggle,
        });
        suite.Add(new SmokeTest
        {
            Id = "jump",
            Group = "Follow",
            Title = "Jump, as Mimic copies it",
            Kind = TestKind.Auto,
            Description = "Mimic copies a leader's jump with ActionManager.UseAction(GeneralAction, 2) and spots a " +
                          "leader's jump with Character.IsJumping(). Makes you jump once in place with that same call " +
                          "and checks IsJumping() turns true on your own character within a second. Stand somewhere " +
                          "a jump is harmless (not at a ledge). Needs you on foot, on the ground, out of combat.",
            Precondition = OnFoot,
            TimeoutMs = 15_000,
            Body = Jump,
        });
    }

    // The game ignores an emote sent while the previous one is still playing, so test waves are spaced out (they run
    // back to back as auto tests). A wave's animation is about 2.5 s.
    private const int EmoteSpacingMs = 4000;
    private static long lastWaveSent = long.MinValue / 2;

    private static IEnumerable<Wait> SendWave(TestContext t, bool targetSelf)
    {
        var replies = t.Require(Replies, "the plugin created its EmoteReplies");
        t.Require(replies.Available && replies.SmokeHookEnabled, "the emote hook is created and enabled");
        var waveId = WaveEmoteId();
        t.Require(waveId != null, "found the Wave emote's row");
        var line = EmoteReplies.SmokeEmoteCommand((ushort)waveId!.Value);
        t.Require(line != null, "the Wave emote has a text command");
        t.Fact("line", line);
        var invalid = GameChat.SmokeValidate(line!);
        t.Require(invalid == null, $"GameChat would send '{line}' ({invalid ?? "ok"})");

        var local = t.Require(Service.ObjectTable.LocalPlayer, "your character is loaded");
        var localAddress = local.Address;
        var localId = local.GameObjectId;
        t.Fact("your address", localAddress);
        t.Fact("your GameObjectId", Hex(localId));

        // Undo first: the target goes back to whatever it was (or nothing, if that's gone now).
        var previousId = Service.TargetManager.Target?.GameObjectId ?? 0;
        t.Fact("previous target", Hex(previousId));
        t.OnCleanup("put your target back", () => RestoreTarget(previousId));

        Service.TargetManager.Target = targetSelf ? local : null;
        yield return Wait.Frame;
        var aimed = Service.TargetManager.Target?.GameObjectId ?? 0;
        t.Require(targetSelf ? aimed == localId : aimed == 0, targetSelf ? "you're targeting yourself" : "your target is cleared");

        var sinceLast = Environment.TickCount64 - lastWaveSent;
        if (sinceLast < EmoteSpacingMs)
        {
            t.Info($"waiting {EmoteSpacingMs - sinceLast} ms for the previous wave to finish");
            yield return Wait.Ms((int)(EmoteSpacingMs - sinceLast));
        }

        yield return Wait.For(() => CommandRateLimiter.Shared.TryAcquire(Stopwatch.GetTimestamp()),
                              "a free slot in the shared command rate limit", 5_000);
        var before = replies.SmokeCallCount;
        t.Fact("hook calls before", before);
        GameChat.Send(line!);
        lastWaveSent = Environment.TickCount64;
        t.Info($"sent {line}");

        EmoteReplies.SmokeEmoteCall? seen = null;
        yield return Wait.For(() => (seen = FindCall(replies, before, (ulong)localAddress, (ushort)waveId.Value)) != null,
                              "the emote hook to be called for your wave", 5_000);
        var call = seen!.Value;
        t.Fact("hook calls after", replies.SmokeCallCount);
        t.Fact("instigator", Hex(call.Instigator));
        t.Fact("emote id", call.EmoteId);
        t.Fact("target id", Hex(call.TargetId));
        t.Check(true, "the hook saw your wave (instigator = you, emote = Wave)");

        if (targetSelf)
        {
            if (call.TargetId == NoTarget || call.TargetId == 0)
                t.Skip($"the game sent your self-targeted wave with no target ({Hex(call.TargetId)}), so there's no id to compare");
            t.Check(call.TargetId == localId,
                    $"the hook's target id ({Hex(call.TargetId)}) is your GameObjectId ({Hex(localId)}), as Emote replies expect");
        }
        else
        {
            t.Check(call.TargetId == NoTarget || call.TargetId == 0,
                    $"an untargeted emote reaches the hook with no target ({Hex(call.TargetId)}; Mimic expects 0 or {Hex(NoTarget)})");
        }
    }

    private static EmoteReplies.SmokeEmoteCall? FindCall(EmoteReplies replies, long after, ulong instigator, ushort emoteId)
    {
        foreach (var call in replies.SmokeRecentCalls())
        {
            if (call.Seq > after && call.Instigator == instigator && call.EmoteId == emoteId)
                return call;
        }
        return null;
    }

    private static void RestoreTarget(ulong previousId)
    {
        var previous = previousId == 0 ? null : Service.ObjectTable.SearchById(previousId);
        Service.TargetManager.Target = previous;
        var now = Service.TargetManager.Target?.GameObjectId ?? 0;
        if (now != (previous?.GameObjectId ?? 0))
            throw new InvalidOperationException($"Target is {Hex(now)}, expected {Hex(previousId)}.");
    }

    private static string? WalkToggleFree()
    {
        if (NotBusy() is { } busy)
            return busy;
        if (PaceMatcher.SmokeWatching || FollowMode.Following != null)
            return "Stop following first (say the stop word, or turn Follow and Mimic off)";
        return null;
    }

    private static IEnumerable<Wait> WalkToggle(TestContext t)
    {
        var original = ReadWalking();
        t.Require(original != null, "Control.IsWalking can be read");
        var was = original!.Value;
        t.Fact("IsWalking before", was);

        // Undo first: set it back, and check it stuck.
        t.OnCleanup("set the walk toggle back", () => RestoreWalking(was));

        t.Require(WriteWalking(!was), "Control.IsWalking can be written");
        yield return Wait.Frame;
        var now = ReadWalking();
        t.Fact("IsWalking after flip", now);
        t.Check(now == !was, "the toggle reads back flipped next frame");
        yield return Wait.Ms(500);
        t.Check(ReadWalking() == !was, "and is still flipped half a second later (nothing else set it back)");
    }

    private static IEnumerable<Wait> RestoreWalking(bool was)
    {
        if (!WriteWalking(was))
            throw new InvalidOperationException("Control is gone; set walk/run back by hand.");
        yield return Wait.Frame;
        if (ReadWalking() != was)
            throw new InvalidOperationException($"The walk toggle reads {ReadWalking()}, expected {was}.");
    }

    private static IEnumerable<Wait> Jump(TestContext t)
    {
        var local = t.Require(Service.ObjectTable.LocalPlayer, "your character is loaded");
        var address = local.Address;
        t.Require(!MimicMode.SmokeIsJumping(address), "you're not already jumping");

        yield return Wait.For(() => CommandRateLimiter.Shared.TryAcquire(Stopwatch.GetTimestamp()),
                              "a free slot in the shared command rate limit", 5_000);
        t.Require(MimicMode.SmokeUseJumpAction(), "UseAction(GeneralAction, 2) was accepted");
        yield return Wait.For(() => Service.ObjectTable.LocalPlayer?.Address == address && MimicMode.SmokeIsJumping(address),
                              "Character.IsJumping() to turn true", 1_000);
        t.Check(true, "IsJumping() is true while you jump (what Mimic watches on the leader)");
        t.Fact("Jumping condition", Service.Condition[ConditionFlag.Jumping]);
        yield return Wait.Upto(() => Service.ObjectTable.LocalPlayer?.Address != address || !MimicMode.SmokeIsJumping(address), 3_000);
        t.Check(Service.ObjectTable.LocalPlayer?.Address == address && !MimicMode.SmokeIsJumping(address),
                "and false again once you land");
    }
}
