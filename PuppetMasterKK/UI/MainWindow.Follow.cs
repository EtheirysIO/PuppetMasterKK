using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using phys1ksUI;

namespace PuppetMasterKK.UI;

internal sealed partial class MainWindow
{
    private string neverFromInput = string.Empty;
    private string onlyFollowInput = string.Empty;
    private string neverFollowInput = string.Empty;
    private string stopCommandInput = string.Empty;

    private void DrawFollowPage()
    {
        var settings = Config.Follow;
        var call = PluginUiLogic.FirstAlternative(settings.CallNames, "Ami");
        var follow = PluginUiLogic.FirstAlternative(settings.FollowWords, "follow");
        var stop = PluginUiLogic.FirstAlternative(settings.StopWords, "stop");
        // No come word: "Ami come" does nothing, so the help doesn't mention it.
        var come = PluginUiLogic.FirstAlternative(settings.ComeWords, string.Empty);

        using (W.Card("follow", "Follow mode", settings.Enabled ? "On" : "Off"))
        {
            W.TextWrapped("When someone you trust says your call name and the follow word, you follow them, or the player they name. The stop word stops everything.", Theme.Dim);
            Gap();
            var enabled = settings.Enabled;
            if (W.Toggle("Answer follow requests##followOn", ref enabled))
            {
                settings.Enabled = enabled;
                if (!enabled)
                    StopWalking();
                Changed();
            }

            Gap();
            var callNames = settings.CallNames;
            if (CallNameInput("##callNames", ref callNames, settings.Enabled))
            {
                settings.CallNames = callNames;
                Changed();
            }

            Gap();
            var followWords = settings.FollowWords;
            if (WordInput("Follow word", "##followWords", ref followWords, "follow", required: true))
            {
                settings.FollowWords = followWords;
                Changed();
            }
            var comeWords = settings.ComeWords;
            if (WordInput("Come word", "##comeWords", ref comeWords, "come", required: false))
            {
                settings.ComeWords = comeWords;
                Changed();
            }
            var stopWords = settings.StopWords;
            if (WordInput("Stop word", "##stopWords", ref stopWords, "stop", required: true))
            {
                settings.StopWords = stopWords;
                Changed();
            }

            Gap(2f);
            Hint((come.Length > 0 ? $"\"{call} {come}\" comes to whoever said it and follows them. " : string.Empty) +
                 $"\"{call} {follow} me\" follows whoever said it, and " +
                 $"\"{call} {follow} Nova Ral'veth@Exodus\" (or just \"Nova\", if only one Nova is nearby) follows that player. " +
                 $"\"{call} {follow}\" on its own names nobody, so it's ignored. \"{call} {stop}\" stops everything.");

            Gap();
            var walk = settings.WalkWithVnavmesh;
            if (W.Toggle("Walk to them with vnavmesh##walkVnavmesh", ref walk))
            {
                settings.WalkWithVnavmesh = walk;
                if (!walk)
                    StopWalking();
                Changed();
            }
            ImGui.SameLine();
            var vnavmesh = FollowNavigator.IsLoaded();
            W.Chip(vnavmesh ? "vnavmesh detected" : "vnavmesh not detected", vnavmesh ? Theme.Accent : Theme.Faint);
            Hint("When the player is in your zone but more than 20 yalms away, walk to them first, then target and follow them. " +
                 "Party members can be anywhere in the zone; anyone else has to be close enough to see. Gives up in combat, " +
                 "after 90 seconds, if they leave the zone, or if it keeps getting stuck. The stop word stops the walk too.");
        }

        DrawChannelsCard("followChannels", settings.Channels, "Follow mode isn't listening to any channels yet.",
                         "Channels for Follow mode");
        DrawSendersCard("followSenders", settings.Senders, Changed,
                        settings.Senders.Anyone ? "Anyone who can talk in the picked channels can make you follow someone." : null);
        if (NeverFromCard("neverFrom", settings.NeverFrom, ref neverFromInput))
            Changed();
        if (PlayerListsCard("followTargets", "Who you'll follow", "Only follow", settings.OnlyFollow, ref onlyFollowInput,
                            "Never follow", settings.NeverFollow, ref neverFollowInput))
            Changed();
        var reply = settings.ReplyWhenNotNearby;
        var message = settings.NotNearbyMessage;
        if (NotNearbyCard("notNearby", ref reply, ref message))
        {
            settings.ReplyWhenNotNearby = reply;
            settings.NotNearbyMessage = message;
            Changed();
        }

        using (W.Card("stop", "Stop", $"\"{call} {stop}\""))
        {
            W.TextWrapped("Stops every trigger (running or waiting), walking and mimicking.", Theme.Dim);
            Gap(2f);
            var moves = settings.StopMoves;
            if (W.Toggle("Also stand still##stopMoves", ref moves,
                         tooltip: "Takes one tiny step forward with automove. Moving cancels following, emotes, sitting and lying down."))
            {
                settings.StopMoves = moves;
                Changed();
            }
            Gap();
            W.Heading("Then also run");
            var stopCommands = settings.StopCommands;
            if (StringListEditor("stopCommands", stopCommands, ref stopCommandInput, "/command", "No extra commands.",
                                 text => PluginUiLogic.AddCommandLine(stopCommands, text)))
                Changed();
            Hint("Commands that never run from a trigger, like /logout, are skipped here too.");
        }

        if (FollowMode.Following is { } following)
        {
            using (W.Card("following", "Now"))
                W.TextWrapped(FollowNavigator.IsWalking ? $"Walking to {following}." : $"Last asked to follow {following}.", Theme.Ink);
        }
    }

    /// <summary>Stops a vnavmesh walk in progress (Follow mode or its walking was just turned off).</summary>
    private static void StopWalking()
    {
        if (!FollowNavigator.IsWalking)
            return;
        FollowNavigator.Cancel();
        FollowMode.ClearFollowing();
    }

    // ───────────────────────── Shared by Follow mode and Mimic ─────────────────────────

    /// <summary>The call name field. <paramref name="required"/> marks it red when empty.</summary>
    private static bool CallNameInput(string id, ref string callNames, bool required)
    {
        Label("Call name");
        var changed = W.TextInput(id, ref callNames, "Ami", 0f, 200, error: required && string.IsNullOrWhiteSpace(callNames));
        Hint("The name people use to get your attention. Separate multiple names with |, e.g. Ami|Kitty.");
        return changed;
    }

    /// <summary>A labeled word field (follow, come, mimic or stop words).</summary>
    private static bool WordInput(string label, string id, ref string words, string placeholder, bool required)
    {
        Label(label);
        return W.TextInput(id, ref words, placeholder, 0f, 200, error: required && string.IsNullOrWhiteSpace(words));
    }

    /// <summary>"Never take requests from": these players are ignored, whatever "Who can trigger it" allows.</summary>
    private static bool NeverFromCard(string id, List<string> names, ref string input)
    {
        using (W.Card(id, "Never take requests from", names.Count > 0 ? $"{names.Count}" : null))
            return StringListEditor("names", names, ref input, NameHint, "Nobody.", text => PluginUiLogic.AddPlayerName(names, text));
    }

    /// <summary>Who a mode acts on: an "only" list (empty: anyone) and a "never" list.</summary>
    private static bool PlayerListsCard(string id, string title, string onlyHeading, List<string> only, ref string onlyInput,
                                        string neverHeading, List<string> never, ref string neverInput)
    {
        var changed = false;
        using (W.Card(id, title))
        {
            W.Heading(onlyHeading);
            changed |= StringListEditor("only", only, ref onlyInput, NameHint, "Anyone (when this list is empty).",
                                        text => PluginUiLogic.AddPlayerName(only, text));
            Gap();
            W.Heading(neverHeading);
            changed |= StringListEditor("never", never, ref neverInput, NameHint, "Nobody.",
                                        text => PluginUiLogic.AddPlayerName(never, text));
        }
        return changed;
    }

    /// <summary>"When the player isn't nearby": the optional tell back.</summary>
    private static bool NotNearbyCard(string id, ref bool reply, ref string message)
    {
        var changed = false;
        using (W.Card(id, "When the player isn't nearby"))
        {
            changed |= W.Toggle("Reply by tell##reply", ref reply);
            if (reply)
            {
                Gap(2f);
                changed |= W.TextInput("##message", ref message, "Sorry, I don't see <target> near me.", 0f, 400);
                Hint("<target> is replaced with the name they asked for. Replies to the same person at most once every 10 seconds.");
            }
        }
        return changed;
    }
}
