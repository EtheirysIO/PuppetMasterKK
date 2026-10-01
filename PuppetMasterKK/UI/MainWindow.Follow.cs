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
            W.TextWrapped("Someone you trust says your call name and the follow word, and you follow them, or the player they " +
                          "name. The stop word stops everything.", Theme.Dim);
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
            Hint("What people call you. Separate several with |, e.g. Ami|Kitty.");

            Gap();
            Label("Follow word");
            var followWords = settings.FollowWords;
            if (W.TextInput("##followWords", ref followWords, "follow", 0f, 200, error: string.IsNullOrWhiteSpace(followWords)))
            {
                settings.FollowWords = followWords;
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
            Gap(2f);
            Hint($"\"{call} {follow}\" follows whoever said it. \"{call} {follow} Nova Ral'veth@Exodus\" (or just \"Nova\", if " +
                 $"only one Nova is nearby) follows that player. \"{call} {stop}\" stops.");
        }

        var count = settings.Channels.Count;
        using (W.Card("followChannels", "Channels", count == 1 ? "1 picked" : $"{count} picked"))
        {
            DrawChannelChips(settings.Channels, "Follow mode isn't listening anywhere yet.");
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

        using (W.Card("notNearby", "When they're not nearby"))
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
                Hint("<target> becomes the name they asked for. At most one reply to the same person every 10 seconds.");
            }
        }

        using (W.Card("stop", "Stop", $"\"{FirstAlternative(settings.CallNames, "Ami")} {FirstAlternative(settings.StopWords, "stop")}\""))
        {
            W.TextWrapped("Stops every trigger, running or waiting.", Theme.Dim);
            Gap(2f);
            var moves = settings.StopMoves;
            if (W.Toggle("Also stand still##stopMoves", ref moves,
                         tooltip: "Takes one tiny automove step: moving ends following, emotes, sitting and lying down"))
            {
                settings.StopMoves = moves;
                Changed();
            }
            Gap();
            W.Heading("Then run");
            if (StringListEditor("stopCommands", settings.StopCommands, ref stopCommandInput, "/command", "Nothing else.",
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
