using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
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

        using (W.Card("follow", "Follow mode", settings.Enabled ? "On" : "Off"))
        {
            W.TextWrapped("When someone you trust says your call name and the follow word, you follow them, or the player they name. The stop word stops everything.", Theme.Dim);
            Gap();
            var enabled = settings.Enabled;
            if (W.Toggle("Answer follow requests##followOn", ref enabled))
            {
                settings.Enabled = enabled;
                Changed();
            }

            Gap();
            Label("Call name");
            var callNames = settings.CallNames;
            if (W.TextInput("##callNames", ref callNames, "Ami", 0f, 200, error: settings.Enabled && string.IsNullOrWhiteSpace(callNames)))
            {
                settings.CallNames = callNames;
                Changed();
            }
            Hint("The name people use to get your attention. Separate multiple names with |, e.g. Ami|Kitty.");

            Gap();
            Label("Follow word");
            var followWords = settings.FollowWords;
            if (W.TextInput("##followWords", ref followWords, "follow", 0f, 200, error: string.IsNullOrWhiteSpace(followWords)))
            {
                settings.FollowWords = followWords;
                Changed();
            }
            Label("Come word");
            var comeWords = settings.ComeWords;
            if (W.TextInput("##comeWords", ref comeWords, "come", 0f, 200))
            {
                settings.ComeWords = comeWords;
                Changed();
            }
            Label("Stop word");
            var stopWords = settings.StopWords;
            if (W.TextInput("##stopWords", ref stopWords, "stop", 0f, 200, error: string.IsNullOrWhiteSpace(stopWords)))
            {
                settings.StopWords = stopWords;
                Changed();
            }

            var call = FirstAlternative(settings.CallNames, "Ami");
            var follow = FirstAlternative(settings.FollowWords, "follow");
            var stop = FirstAlternative(settings.StopWords, "stop");
            var come = FirstAlternative(settings.ComeWords, "come");
            Gap(2f);
            Hint($"\"{call} {come}\" comes to whoever said it and follows them. \"{call} {follow} me\" does the same, and " +
                 $"\"{call} {follow} Nova Ral'veth@Exodus\" (or just \"Nova\", if only one Nova is nearby) follows that player. " +
                 $"\"{call} {follow}\" on its own names nobody, so it's ignored. \"{call} {stop}\" stops everything.");

            Gap();
            var walk = settings.WalkWithVnavmesh;
            if (W.Toggle("Walk to them with vnavmesh##walkVnavmesh", ref walk))
            {
                settings.WalkWithVnavmesh = walk;
                Changed();
            }
            ImGui.SameLine();
            var vnavmesh = FollowNavigator.IsLoaded();
            W.Chip(vnavmesh ? "vnavmesh detected" : "vnavmesh not detected", vnavmesh ? Theme.Accent : Theme.Faint);
            Hint("When the player is in your zone but more than 20 yalms away, walk to them first, then target and follow them. " +
                 "Party members can be anywhere in the zone; anyone else has to be close enough to see. Gives up in combat, " +
                 "after 90 seconds, if they leave the zone, or if it keeps getting stuck. The stop word stops the walk too.");
        }

        var count = settings.Channels.Count;
        using (W.Card("followChannels", "Channels", count == 1 ? "1 picked" : $"{count} picked"))
        {
            DrawChannelChips(settings.Channels, "Follow mode isn't listening to any channels yet.");
            Gap(2f);
            if (W.SecondaryButton("Pick channels##pickFollowChannels"))
                OpenChannelPicker("Channels for Follow mode", settings.Channels, null);
        }

        DrawSendersCard("followSenders", settings.Senders, Changed,
                        settings.Senders.Anyone ? "Anyone who can talk in the picked channels can make you follow someone." : null);
        using (W.Card("neverFrom", "Never take requests from", settings.NeverFrom.Count > 0 ? $"{settings.NeverFrom.Count}" : null))
        {
            if (StringListEditor("neverFrom", settings.NeverFrom, ref neverFromInput, "Name@World (or just Name)", "Nobody.",
                                 input => AddName(settings.NeverFrom, input)))
                Changed();
        }

        using (W.Card("followTargets", "Who you'll follow"))
        {
            W.Heading("Only follow");
            if (StringListEditor("onlyFollow", settings.OnlyFollow, ref onlyFollowInput, "Name@World (or just Name)",
                                 "Anyone (when this list is empty).", input => AddName(settings.OnlyFollow, input)))
                Changed();
            Gap();
            W.Heading("Never follow");
            if (StringListEditor("neverFollow", settings.NeverFollow, ref neverFollowInput, "Name@World (or just Name)",
                                 "Nobody.", input => AddName(settings.NeverFollow, input)))
                Changed();
        }

        var mimicCall = $"{FirstAlternative(settings.CallNames, "Ami")} {FirstAlternative(settings.MimicWords, "mimic")}";
        using (W.Card("mimic", "Mimic", MimicMode.Leader is { } leader ? $"Mimicking {leader.Name}" : $"\"{mimicCall} me\""))
        {
            W.TextWrapped($"\"{mimicCall} me\" (or \"{mimicCall} Nova\") makes you copy that player's emotes until the stop word. " +
                          "When they emote at someone, you emote at the same person; when they emote at you, you emote back " +
                          "at them; when they emote at nobody, so do you.", Theme.Dim);
            Gap();
            Label("Mimic word");
            var mimicWords = settings.MimicWords;
            if (W.TextInput("##mimicWords", ref mimicWords, "mimic", 0f, 200))
            {
                settings.MimicWords = mimicWords;
                Changed();
            }
            Hint("Leave it empty to turn mimicking off.");
            Gap(2f);
            var mimicMotion = settings.MimicMotionOnly;
            if (W.Toggle("Hide emote text##mimicMotionOnly", ref mimicMotion,
                         tooltip: "The animation still plays, but the emote message isn't posted in chat"))
            {
                settings.MimicMotionOnly = mimicMotion;
                Changed();
            }
            Hint("Only emotes are copied, and only from a player close enough to see. Paused during combat. " +
                 "Uses the same channels, senders and follow lists as following.");
            if (MimicMode.IsActive)
            {
                Gap(2f);
                if (W.SecondaryButton("Stop mimicking##stopMimic"))
                    MimicMode.Stop();
            }
        }

        using (W.Card("notNearby", "When the player isn't nearby"))
        {
            var reply = settings.ReplyWhenNotNearby;
            if (W.Toggle("Reply by tell##replyNotNearby", ref reply))
            {
                settings.ReplyWhenNotNearby = reply;
                Changed();
            }
            if (settings.ReplyWhenNotNearby)
            {
                Gap(2f);
                var message = settings.NotNearbyMessage;
                if (W.TextInput("##notNearbyMessage", ref message, "Sorry, I don't see <target> near me.", 0f, 400))
                {
                    settings.NotNearbyMessage = message;
                    Changed();
                }
                Hint("<target> is replaced with the name they asked for. Replies to the same person at most once every 10 seconds.");
            }
        }

        using (W.Card("stop", "Stop", $"\"{FirstAlternative(settings.CallNames, "Ami")} {FirstAlternative(settings.StopWords, "stop")}\""))
        {
            W.TextWrapped("Stops every trigger, running or waiting.", Theme.Dim);
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
            if (StringListEditor("stopCommands", settings.StopCommands, ref stopCommandInput, "/command", "No extra commands.",
                                 input => PluginUiLogic.AddCommandRule(settings.StopCommands, [], input)))
                Changed();
        }

        if (FollowMode.Following is { } following)
        {
            using (W.Card("following", "Now"))
                W.TextWrapped($"Last asked to follow {following}.", Theme.Ink);
        }
    }

    private static string FirstAlternative(string text, string fallback)
    {
        foreach (var part in text.Split('|'))
        {
            if (!string.IsNullOrWhiteSpace(part))
                return part.Trim();
        }
        return fallback;
    }

    private static bool AddName(System.Collections.Generic.List<string> names, string input)
    {
        var name = input.Trim();
        if (name.Length == 0)
            return false;
        foreach (var existing in names)
        {
            if (existing.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                return false;
        }
        names.Add(name);
        return true;
    }
}
