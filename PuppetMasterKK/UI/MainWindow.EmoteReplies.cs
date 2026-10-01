using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using phys1ksUI;

namespace PuppetMasterKK.UI;

internal sealed partial class MainWindow
{
    private string blockedEmoteInput = string.Empty;
    private string overrideWhenInput = string.Empty;
    private string overrideReplyInput = string.Empty;

    private void DrawEmoteRepliesPage()
    {
        var settings = Config.EmoteReplies;
        var replies = Service.plugin?.emoteReplies;

        if (replies is { Available: false })
            W.Banner(replies.UnavailableReason ?? "Emote replies aren't available.", Theme.Warning, icon: FontAwesomeIcon.ExclamationTriangle);

        using (W.Card("emoteReplies", "Emote replies", settings.Enabled ? "On" : "Off"))
        {
            W.TextWrapped("When someone uses an emote on you, do the same emote back. (This used to be the Right Back At You plugin.)", Theme.Dim);
            Gap();
            var enabled = settings.Enabled;
            // Can always be turned off, even while unavailable.
            if (W.Toggle("Reply to emotes##emoteRepliesOn", ref enabled, enabled: replies?.Available != false || settings.Enabled))
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
                         tooltip: "The animation still plays, but the emote message isn't posted in chat"))
            {
                settings.MotionOnly = motionOnly;
                Changed();
            }

            Gap();
            Label("Wait before answering the same player again");
            var cooldown = settings.PerPlayerCooldownSeconds;
            if (W.NumberInput("##replyCooldown", ref cooldown, EmoteReplySettings.MinimumCooldownSeconds, 3600, 1, Theme.S(160f), "seconds"))
            {
                settings.PerPlayerCooldownSeconds = cooldown;
                Changed();
            }
            Hint("Keeps two players who both auto-reply from emoting at each other forever. Replies are paused during combat.");

            Gap();
            W.Heading("Never reply with");
            if (StringListEditor("blockedEmotes", settings.BlockedEmotes, ref blockedEmoteInput, "/emote", "Nothing blocked.",
                                 input => PluginUiLogic.AddCommandRule(settings.BlockedEmotes, [], input)))
                Changed();
        }

        DrawOverridesCard(settings);

        DrawSendersCard("emoteSenders", settings.Senders, Changed,
                        settings.Senders.Anyone ? "Anyone near you can make you emote back." : null);

        if (replies != null)
        {
            using (W.Card("emoteStats", "This session"))
                W.Stat(replies.RepliesSent.ToString(), "Replies sent");
        }
    }

    private void DrawOverridesCard(EmoteReplySettings settings)
    {
        var count = settings.Overrides.Count;
        using (W.Card("emoteOverrides", "Reply with a different emote", count == 0 ? null : count == 1 ? "1 set" : $"{count} set"))
        {
            W.TextWrapped("Answer one emote with another: when someone uses /dote on you, reply with /joy. Leave the reply " +
                          "empty to not answer that emote at all.", Theme.Dim);
            Gap(4f);

            var remove = -1;
            if (count == 0)
                ImGui.TextColored(Theme.Faint, "Every emote is answered with the same emote.");
            for (var i = 0; i < count; i++)
            {
                var entry = settings.Overrides[i];
                ImGui.PushID(i);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(entry.When);
                ImGui.SameLine();
                ImGui.TextColored(Theme.Faint, "→");
                ImGui.SameLine();
                if (string.IsNullOrWhiteSpace(entry.Reply))
                    ImGui.TextColored(Theme.Dim, "don't reply");
                else
                    ImGui.TextUnformatted(entry.Reply);
                W.RightAlign(ImGui.GetFrameHeight());
                if (W.IconButton(FontAwesomeIcon.Times, "##removeOverride", "Remove", danger: true))
                    remove = i;
                ImGui.PopID();
            }
            if (remove >= 0)
            {
                settings.Overrides.RemoveAt(remove);
                Changed();
            }

            Gap(2f);
            var addWidth = W.ButtonWidth("Add");
            var field = (W.Avail() - addWidth - Theme.Space.Tight * 2f) * 0.5f;
            W.TextInput("##overrideWhen", ref overrideWhenInput, "When they use /dote", field, 60);
            ImGui.SameLine(0f, Theme.Space.Tight);
            W.TextInput("##overrideReply", ref overrideReplyInput, "Reply with /joy", field, 60);
            ImGui.SameLine(0f, Theme.Space.Tight);
            var error = OverrideError(settings, overrideWhenInput, overrideReplyInput);
            if (W.SecondaryButton("Add##addOverride", new Vector2(addWidth, ImGui.GetFrameHeight()),
                                  enabled: !string.IsNullOrWhiteSpace(overrideWhenInput) && error == null))
            {
                settings.Overrides.Add(new EmoteOverride
                {
                    When = Slash(overrideWhenInput),
                    Reply = string.IsNullOrWhiteSpace(overrideReplyInput) ? string.Empty : Slash(overrideReplyInput),
                });
                overrideWhenInput = string.Empty;
                overrideReplyInput = string.Empty;
                Changed();
            }
            if (error != null && !string.IsNullOrWhiteSpace(overrideWhenInput))
                ImGui.TextColored(Theme.Warning, error);
            Hint("The \"Never reply with\" list still applies to the reply.");
        }
    }

    private static string Slash(string command)
    {
        var trimmed = command.Trim();
        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }

    private static string? OverrideError(EmoteReplySettings settings, string when, string reply)
    {
        if (string.IsNullOrWhiteSpace(when))
            return null;
        var whenCommand = Slash(when);
        if (!Service.Commands.IsEmote(whenCommand))
            return $"{whenCommand} isn't an emote.";
        if (!string.IsNullOrWhiteSpace(reply) && !Service.Commands.IsEmote(Slash(reply)))
            return $"{Slash(reply)} isn't an emote.";
        var canonical = Service.Commands.Canonicalize(whenCommand);
        foreach (var entry in settings.Overrides)
        {
            if (Service.Commands.Canonicalize(entry.When) == canonical)
                return $"{whenCommand} already has a reply set.";
        }
        return null;
    }
}
