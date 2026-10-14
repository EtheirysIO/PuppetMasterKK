using System;
using System.Collections.Generic;
using Lumina.Excel.Sheets;
using phys1ksTest;

namespace PuppetMasterKK.Smoke;

// Read-only tests: they run by themselves after login.
internal static partial class PuppetMasterSmoke
{
    private static void RegisterAuto(SmokeSuite suite)
    {
        suite.Add(new SmokeTest
        {
            Id = "emote.hook",
            Group = "Emotes",
            Title = "Emote hook is in place",
            Kind = TestKind.Auto,
            Description = "Emote replies and Mimic hang off one hook on the game's emote function, found by signature. " +
                          "Checks the signature matched, the hook is created and enabled, and no 'unavailable' reason " +
                          "is shown. Records the hooked address and how many emotes it has seen so far. If this fails " +
                          "after a patch, the signature in Emotes/EmoteReplies.cs needs updating.",
            Body = EmoteHook,
        });
        suite.Add(new SmokeTest
        {
            Id = "chat.validate",
            Group = "Chat",
            Title = "Chat line checks",
            Kind = TestKind.Auto,
            Description = "Runs GameChat.Send's checks without sending anything: a normal '/wave' passes the game's own " +
                          "sanitizer; an empty line, one over 500 bytes, and lines with characters the chat box strips " +
                          "are refused. Uses the game's Utf8String.SanitizeString, so a change there shows up here.",
            Body = ChatValidate,
        });
        suite.Add(new SmokeTest
        {
            Id = "commands.catalog",
            Group = "Chat",
            Title = "Command catalog",
            Kind = TestKind.Auto,
            Description = "The command list PuppetMasterKK builds from the TextCommand and Emote sheets: /wave is an " +
                          "emote, /say a chat command, /teleport sensitive, /follow Follow-mode only, /logout blocked. " +
                          "Also looks the Wave emote up by row id the way the emote hook does and expects its command.",
            Body = CommandsCatalog,
        });
        suite.Add(new SmokeTest
        {
            Id = "control",
            Group = "Follow",
            Title = "Walk toggle is readable",
            Kind = TestKind.Auto,
            Description = "Pace matching flips Control.IsWalking (the walk/run toggle). Checks ClientStructs' " +
                          "Control.Instance() resolves and records the toggle's current value. Changes nothing.",
            Body = ControlReadable,
        });
        suite.Add(new SmokeTest
        {
            Id = "party.sender",
            Group = "Chat",
            Title = "Sender resolves to you",
            Kind = TestKind.Auto,
            Description = "SenderResolver.FromCharacter on your own character (what the emote hook does for whoever " +
                          "emoted): expects your name and home world from IPlayerState, marked as you. Also reads the " +
                          "friend, FC and party lists on the way, so a broken ClientStructs offset there throws here.",
            Body = SenderSelf,
        });
        suite.Add(new SmokeTest
        {
            Id = "vnavmesh",
            Group = "Follow",
            Title = "vnavmesh is usable",
            Kind = TestKind.Auto,
            Description = "Follow mode walks to far-away players with vnavmesh. Checks every vnavmesh IPC it calls is " +
                          "registered, and that FollowNavigator.IsAvailable sees this zone's mesh. Skipped when vnavmesh " +
                          "isn't installed, or when the mesh isn't ready in this zone after 20 s (some zones have none).",
            Body = Vnavmesh,
        });
    }

    private static IEnumerable<Wait> EmoteHook(TestContext t)
    {
        var replies = t.Require(Replies, "the plugin created its EmoteReplies");
        t.Fact("signature", replies.SmokeSignature);
        t.Fact("unavailable reason", replies.UnavailableReason);
        t.Require(replies.Available, "the emote signature matched and the hook was created");
        t.Check(replies.SmokeHookEnabled, "the hook is enabled");
        t.Check(replies.UnavailableReason == null, "no 'unavailable' reason is set");
        t.RequireAddress(replies.SmokeHookAddress, "the hook has an address");
        t.Fact("hook address", replies.SmokeHookAddress);
        t.Fact("emotes seen so far", replies.SmokeCallCount);
        yield break;
    }

    private static IEnumerable<Wait> ChatValidate(TestContext t)
    {
        var ok = GameChat.SmokeValidate("/wave");
        t.Check(ok == null, $"'/wave' would be sent ({ok ?? "accepted"})");

        var atLimit = "/echo " + new string('a', GameChat.MaxBytes - 6);
        t.Check(GameChat.SmokeValidate(atLimit) == null, $"a line of exactly {GameChat.MaxBytes} bytes is accepted");

        var tooLong = "/echo " + new string('a', GameChat.MaxBytes);
        var longReason = GameChat.SmokeValidate(tooLong);
        t.Check(longReason != null, $"a {tooLong.Length}-byte line is refused ({longReason ?? "accepted"})");

        var emptyReason = GameChat.SmokeValidate("");
        t.Check(emptyReason != null, $"an empty line is refused ({emptyReason ?? "accepted"})");

        // Which of these the sanitizer strips depends on the game; at least one must be refused, or the sanitizer
        // check is doing nothing.
        var candidates = new (string Name, string Text)[]
        {
            ("bell U+0007", "/wave \u0007"),
            ("escape U+001B", "/wave \u001B"),
            ("newline", "/wave\nhello"),
            ("tab", "/wave\thello"),
            ("emoji", "/wave \U0001F600"),
        };
        var refused = 0;
        foreach (var (name, text) in candidates)
        {
            var reason = GameChat.SmokeValidate(text);
            t.Fact($"sanitizer: {name}", reason == null ? "accepted" : "refused");
            if (reason != null)
                refused++;
        }
        t.Check(refused > 0, "the game's sanitizer refuses at least one line with a control character or emoji");
        yield break;
    }

    private static IEnumerable<Wait> CommandsCatalog(TestContext t)
    {
        var catalog = Service.Commands;
        t.Fact("emote count", catalog.EmoteCount);
        t.Require(catalog.EmoteCount > 50, "the catalog read the emote list");
        t.Check(catalog.IsEmote("/wave"), "/wave is an emote");
        t.Check(catalog.Classify("/wave") == CommandKind.Emote, $"/wave classifies as Emote ({catalog.Classify("/wave")})");
        t.Check(catalog.Classify("/say") == CommandKind.Chat, $"/say classifies as Chat ({catalog.Classify("/say")})");
        t.Check(catalog.Classify("/teleport") == CommandKind.Sensitive, $"/teleport classifies as Sensitive ({catalog.Classify("/teleport")})");
        t.Check(catalog.Classify("/follow") == CommandKind.FollowOnly, $"/follow classifies as FollowOnly ({catalog.Classify("/follow")})");
        t.Check(catalog.Classify("/logout") == CommandKind.Blocked, $"/logout classifies as Blocked ({catalog.Classify("/logout")})");
        t.Check(catalog.Classify("/sit") == CommandKind.Emote, $"/sit classifies as Emote ({catalog.Classify("/sit")})");

        var waveId = WaveEmoteId();
        t.Fact("wave emote row", waveId);
        t.Require(waveId != null, "the Emote sheet has a row whose English command is /wave");
        var command = EmoteReplies.SmokeEmoteCommand((ushort)waveId!.Value);
        t.Fact("wave command (client language)", command);
        t.Check(command != null && catalog.Canonicalize(command) == catalog.Canonicalize("/wave"),
                "the emote hook's id -> command lookup gives /wave (or its local form) for that row");
        var row = Service.DataManager.GetExcelSheet<Emote>().GetRowOrDefault(waveId.Value);
        t.Fact("wave name", row?.Name.ExtractText());
        yield break;
    }

    private static IEnumerable<Wait> ControlReadable(TestContext t)
    {
        t.RequireAddress(ControlAddress(), "Control.Instance() resolves");
        t.Fact("Control", ControlAddress());
        var walking = ReadWalking();
        t.Require(walking != null, "Control.IsWalking can be read");
        t.Fact("IsWalking", walking);
        t.Fact("pace matching active", PaceMatcher.SmokeWatching);
        yield break;
    }

    private static IEnumerable<Wait> SenderSelf(TestContext t)
    {
        var local = t.Require(Service.ObjectTable.LocalPlayer, "your character is loaded");
        var state = Service.PlayerState;
        t.Require(state.IsLoaded, "IPlayerState is loaded");
        var homeWorld = state.HomeWorld.ValueNullable?.Name.ExtractText() ?? "";
        t.Fact("PlayerState name", state.CharacterName);
        t.Fact("PlayerState home world", homeWorld);

        var info = SenderResolver.FromCharacter(local);
        t.Fact("resolved", info.Key);
        t.Check(info.IsSelf, "you resolve as yourself (IsSelf)");
        t.Check(info.Name == state.CharacterName, "the name matches IPlayerState");
        t.Check(homeWorld.Length > 0 && info.World == homeWorld, "the world matches your home world");
        t.Fact("friend/FC/party", $"{info.IsFriend}/{info.IsFreeCompany}/{info.IsParty}");
        t.Fact("party list size", Service.PartyList.Length);
        yield break;
    }

    // The IPCs FollowNavigator calls, and whether each is a function (else an action).
    private static readonly (string Name, bool Function)[] VnavIpcs =
    [
        ("vnavmesh.Nav.IsReady", true),
        ("vnavmesh.Nav.Pathfind", true),
        ("vnavmesh.Path.IsRunning", true),
        ("vnavmesh.Path.GetTolerance", true),
        ("vnavmesh.Path.MoveTo", false),
        ("vnavmesh.Path.Stop", false),
        ("vnavmesh.Path.SetTolerance", false),
    ];

    private static IEnumerable<Wait> Vnavmesh(TestContext t)
    {
        var loaded = FollowNavigator.IsLoaded();
        t.Fact("vnavmesh loaded", loaded);
        if (!loaded)
            t.Skip("vnavmesh isn't installed or loaded; Follow mode just doesn't walk to far-away players without it");

        var pi = Service.PluginInterface;
        var problems = 0;
        foreach (var (name, function) in VnavIpcs)
        {
            bool registered;
            try
            {
                registered = function ? pi.GetIpcSubscriber<object>(name).HasFunction : pi.GetIpcSubscriber<object>(name).HasAction;
            }
            catch (Exception ex)
            {
                t.Info($"{name}: {ex.Message}");
                registered = false;
            }
            if (!t.Check(registered, $"{name} is registered as {(function ? "a function" : "an action")}"))
                problems++;
        }

        bool? ready = null;
        try
        {
            ready = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady").InvokeFunc();
        }
        catch (Exception ex)
        {
            problems++;
            t.Check(false, $"vnavmesh.Nav.IsReady answers as Func<bool> ({ex.GetType().Name}: {ex.Message})");
        }
        t.Fact("Nav.IsReady now", ready);
        yield return Wait.Upto(FollowNavigator.IsAvailable, 20_000);
        var available = FollowNavigator.IsAvailable();
        t.Fact("FollowNavigator.IsAvailable", available);
        // A missing IPC already failed the test; only a clean vnavmesh without a mesh here is a skip.
        if (!available && problems == 0)
            t.Skip("vnavmesh has no mesh for this zone yet (still building, or none here); try again elsewhere");
    }
}
