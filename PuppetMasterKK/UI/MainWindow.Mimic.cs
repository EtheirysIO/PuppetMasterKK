using System;
using Dalamud.Bindings.ImGui;
using phys1ksUI;

namespace PuppetMasterKK.UI;

internal sealed partial class MainWindow
{
    private string mimicNeverFromInput = string.Empty;
    private string onlyMimicInput = string.Empty;
    private string neverMimicInput = string.Empty;

    private void DrawMimicPage()
    {
        var settings = Config.Mimic;
        var call = FirstAlternative(settings.CallNames, "Ami");
        var mimic = FirstAlternative(settings.MimicWords, "mimic");
        var stop = FirstAlternative(settings.StopWords, "stop");

        using (W.Card("mimic", "Mimic", MimicMode.Leader is { } leader ? $"Mimicking {leader.Name}" : settings.Enabled ? "On" : "Off"))
        {
            W.TextWrapped("When someone you trust says your call name and the mimic word, you copy that player's emotes until " +
                          "the stop word. When they emote at someone, you emote at the same person; when they emote at you, you " +
                          "emote back at them; when they emote at nobody, so do you.", Theme.Dim);
            Gap();
            var enabled = settings.Enabled;
            if (W.Toggle("Answer mimic requests##mimicOn", ref enabled))
            {
                settings.Enabled = enabled;
                if (!enabled)
                    MimicMode.Stop();
                Changed();
            }

            Gap();
            Label("Call name");
            var callNames = settings.CallNames;
            if (W.TextInput("##mimicCallNames", ref callNames, "Ami", 0f, 200, error: settings.Enabled && string.IsNullOrWhiteSpace(callNames)))
            {
                settings.CallNames = callNames;
                Changed();
            }
            Hint("The name people use to get your attention. Separate multiple names with |, e.g. Ami|Kitty.");

            Gap();
            Label("Mimic word");
            var mimicWords = settings.MimicWords;
            if (W.TextInput("##mimicWords", ref mimicWords, "mimic", 0f, 200, error: string.IsNullOrWhiteSpace(mimicWords)))
            {
                settings.MimicWords = mimicWords;
                Changed();
            }
            Label("Stop word");
            var stopWords = settings.StopWords;
            if (W.TextInput("##mimicStopWords", ref stopWords, "stop", 0f, 200, error: string.IsNullOrWhiteSpace(stopWords)))
            {
                settings.StopWords = stopWords;
                Changed();
            }
            Gap(2f);
            Hint($"\"{call} {mimic} me\" copies whoever said it. \"{call} {mimic} Nova Ral'veth@Exodus\" (or just \"Nova\", if " +
                 $"only one Nova is nearby) copies that player. \"{call} {mimic}\" on its own names nobody, so it's ignored. " +
                 $"\"{call} {stop}\" stops mimicking. If Follow mode uses the same call name and stop word, its stop stops everything.");

            if (MimicMode.IsActive)
            {
                Gap(2f);
                if (W.SecondaryButton("Stop mimicking##stopMimic"))
                    MimicMode.Stop();
            }
        }

        using (W.Card("mimicCopying", "Copying"))
        {
            var motionOnly = settings.MotionOnly;
            if (W.Toggle("Hide emote text##mimicMotionOnly", ref motionOnly,
                         tooltip: "The animation still plays, but the emote message isn't posted in chat"))
            {
                settings.MotionOnly = motionOnly;
                Changed();
            }

            Gap();
            var delayed = settings.DelaySeconds > 0f;
            if (W.Toggle("Wait before copying##mimicDelayOn", ref delayed))
            {
                settings.DelaySeconds = delayed ? 1f : 0f;
                Changed();
            }
            if (delayed)
            {
                Gap(2f);
                var milliseconds = (int)MathF.Round(settings.DelaySeconds * 1000f);
                if (W.NumberInput("##mimicDelay", ref milliseconds, 100, (int)(MimicSettings.MaxDelaySeconds * 1000f), 100, Theme.S(180f), "ms"))
                {
                    settings.DelaySeconds = milliseconds / 1000f;
                    Changed();
                }
            }
            Hint("Off: you copy the emote right away. On: you copy it this long after they do it.");

            Gap();
            var guarded = settings.RepeatGuardSeconds > 0f;
            if (W.Toggle("Skip the same emote repeated quickly##mimicGuardOn", ref guarded))
            {
                settings.RepeatGuardSeconds = guarded ? 3f : 0f;
                Changed();
            }
            if (guarded)
            {
                Gap(2f);
                var seconds = Math.Max(1, (int)MathF.Round(settings.RepeatGuardSeconds));
                if (W.NumberInput("##mimicGuard", ref seconds, 1, 30, 1, Theme.S(180f), "seconds"))
                {
                    settings.RepeatGuardSeconds = seconds;
                    Changed();
                }
            }
            Hint("If you copy an emote and the same emote comes from them again within this time, it isn't copied. This " +
                 "stops two players who mimic each other from looping forever. Turn it off to copy every repeat.");
            Gap(2f);
            Hint("Only emotes are copied, and only from a player close enough to see. Paused during combat.");
        }

        var count = settings.Channels.Count;
        using (W.Card("mimicChannels", "Channels", count == 1 ? "1 picked" : $"{count} picked"))
        {
            DrawChannelChips(settings.Channels, "Mimic isn't listening to any channels yet.");
            Gap(2f);
            if (W.SecondaryButton("Pick channels##pickMimicChannels"))
                OpenChannelPicker("Channels for Mimic", settings.Channels, null);
        }

        DrawSendersCard("mimicSenders", settings.Senders, Changed,
                        settings.Senders.Anyone ? "Anyone who can talk in the picked channels can make you mimic someone." : null);
        using (W.Card("mimicNeverFrom", "Never take requests from", settings.NeverFrom.Count > 0 ? $"{settings.NeverFrom.Count}" : null))
        {
            if (StringListEditor("mimicNeverFrom", settings.NeverFrom, ref mimicNeverFromInput, "Name@World (or just Name)", "Nobody.",
                                 input => AddName(settings.NeverFrom, input)))
                Changed();
        }

        using (W.Card("mimicTargets", "Who you'll mimic"))
        {
            W.Heading("Only mimic");
            if (StringListEditor("onlyMimic", settings.OnlyMimic, ref onlyMimicInput, "Name@World (or just Name)",
                                 "Anyone (when this list is empty).", input => AddName(settings.OnlyMimic, input)))
                Changed();
            Gap();
            W.Heading("Never mimic");
            if (StringListEditor("neverMimic", settings.NeverMimic, ref neverMimicInput, "Name@World (or just Name)",
                                 "Nobody.", input => AddName(settings.NeverMimic, input)))
                Changed();
        }

        using (W.Card("mimicNotNearby", "When the player isn't nearby"))
        {
            var reply = settings.ReplyWhenNotNearby;
            if (W.Toggle("Reply by tell##mimicReplyNotNearby", ref reply))
            {
                settings.ReplyWhenNotNearby = reply;
                Changed();
            }
            if (settings.ReplyWhenNotNearby)
            {
                Gap(2f);
                var message = settings.NotNearbyMessage;
                if (W.TextInput("##mimicNotNearbyMessage", ref message, "Sorry, I don't see <target> near me.", 0f, 400))
                {
                    settings.NotNearbyMessage = message;
                    Changed();
                }
                Hint("<target> is replaced with the name they asked for. Replies to the same person at most once every 10 seconds.");
            }
        }
    }
}
