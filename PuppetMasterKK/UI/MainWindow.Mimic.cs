using System;
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
        var call = PluginUiLogic.FirstAlternative(settings.CallNames, "Ami");
        var mimic = PluginUiLogic.FirstAlternative(settings.MimicWords, "mimic");
        var stop = PluginUiLogic.FirstAlternative(settings.StopWords, "stop");

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
            var callNames = settings.CallNames;
            if (CallNameInput("##mimicCallNames", ref callNames, settings.Enabled))
            {
                settings.CallNames = callNames;
                Changed();
            }

            Gap();
            var mimicWords = settings.MimicWords;
            if (WordInput("Mimic word", "##mimicWords", ref mimicWords, "mimic", required: true))
            {
                settings.MimicWords = mimicWords;
                Changed();
            }
            var stopWords = settings.StopWords;
            if (WordInput("Stop word", "##mimicStopWords", ref stopWords, "stop", required: true))
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
            var followLeader = settings.FollowLeader;
            if (W.Toggle("Follow them too##mimicFollow", ref followLeader))
            {
                settings.FollowLeader = followLeader;
                Changed();
            }
            Hint("Targets and follows the player you mimic, walking there first with vnavmesh when Follow mode's walking " +
                 "is on, and follows them again after each copied emote (an emote stops following). Works even with " +
                 "Follow mode off. The stop word ends both.");
            Gap();
            var matchPace = settings.MatchPace;
            if (W.Toggle("Walk when they walk##mimicPace", ref matchPace))
            {
                settings.MatchPace = matchPace;
                Changed();
            }
            Gap();
            var copyJumps = settings.CopyJumps;
            if (W.Toggle("Jump when they jump##mimicJumps", ref copyJumps))
            {
                settings.CopyJumps = copyJumps;
                Changed();
            }
            Gap();
            var motionOnly = settings.MotionOnly;
            if (HideEmoteTextToggle("mimicMotionOnly", ref motionOnly))
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
                if (W.NumberInput("##mimicGuard", ref seconds, 1, (int)MimicSettings.MaxRepeatGuardSeconds, 1, Theme.S(180f), "seconds"))
                {
                    settings.RepeatGuardSeconds = seconds;
                    Changed();
                }
            }
            Hint("After you copy an emote, the same emote isn't copied again for this long (plus the wait before copying, " +
                 "if that's on). This stops two players who mimic each other from looping forever. Turn it off to copy every repeat.");
            Gap(2f);
            Hint("Only emotes are copied, and only from a player close enough to see. Paused during combat.");
        }

        DrawChannelsCard("mimicChannels", settings.Channels, "Mimic isn't listening to any channels yet.", "Channels for Mimic");
        DrawSendersCard("mimicSenders", settings.Senders, Changed,
                        settings.Senders.Anyone ? "Anyone who can talk in the picked channels can make you mimic someone." : null);
        if (NeverFromCard("mimicNeverFrom", settings.NeverFrom, ref mimicNeverFromInput))
            Changed();
        if (PlayerListsCard("mimicTargets", "Who you'll mimic", "Only mimic", settings.OnlyMimic, ref onlyMimicInput,
                            "Never mimic", settings.NeverMimic, ref neverMimicInput))
            Changed();
        var reply = settings.ReplyWhenNotNearby;
        var message = settings.NotNearbyMessage;
        if (NotNearbyCard("mimicNotNearby", ref reply, ref message))
        {
            settings.ReplyWhenNotNearby = reply;
            settings.NotNearbyMessage = message;
            Changed();
        }
    }
}
