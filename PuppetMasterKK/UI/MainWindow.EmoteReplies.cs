using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using phys1ksUI;

namespace PuppetMasterKK.UI;

internal sealed partial class MainWindow
{
    private void DrawEmoteRepliesPage()
    {
        var settings = Config.EmoteReplies;
        var replies = Service.plugin?.emoteReplies;

        if (replies is { Available: false })
            W.Banner(replies.UnavailableReason ?? "Emote replies aren't available.", Theme.Warning, icon: FontAwesomeIcon.ExclamationTriangle);

        using (W.Card("emoteReplies", "Emote replies", settings.Enabled ? "On" : "Off"))
        {
            W.TextWrapped("When someone uses an emote on you, answer with the same emote. (This was the Right Back At You plugin.)", Theme.Dim);
            Gap();
            var enabled = settings.Enabled;
            if (W.Toggle("Reply to emotes##emoteRepliesOn", ref enabled, enabled: replies?.Available != false))
            {
                settings.Enabled = enabled;
                Changed();
            }
            Gap(2f);
            var targetBack = settings.TargetBack;
            if (W.Toggle("Target them first##targetBack", ref targetBack, tooltip: "Target the player so the emote is aimed back at them"))
            {
                settings.TargetBack = targetBack;
                Changed();
            }
            Gap(2f);
            var motionOnly = settings.MotionOnly;
            if (W.Toggle("Hide emote text##replyMotionOnly", ref motionOnly,
                         tooltip: "The animation plays, but the emote's chat line isn't posted"))
            {
                settings.MotionOnly = motionOnly;
                Changed();
            }

            Gap();
            Label("Wait before answering the same player again");
            var cooldown = settings.PerPlayerCooldownSeconds;
            if (W.NumberInput("##replyCooldown", ref cooldown, 0, 3600, 1, Theme.S(160f), "seconds"))
            {
                settings.PerPlayerCooldownSeconds = cooldown;
                Changed();
            }
            Hint("Stops two players who both answer emotes from emoting at each other forever.");
        }

        DrawSendersCard("emoteSenders", settings.Senders, Changed, settings.Senders.Anyone);

        if (replies != null)
        {
            using (W.Card("emoteStats", "This session"))
                W.Stat(replies.RepliesSent.ToString(), "Replies sent");
        }
    }
}
