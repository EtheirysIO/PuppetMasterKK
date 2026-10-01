using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using phys1ksUI;

namespace PuppetMasterKK.UI;

internal sealed partial class MainWindow
{
    private static readonly string[] SettingsTabs = ["General", "New triggers", "Custom channels", "Appearance", "About"];
    private int settingsTab;
    private string defaultAllowInput = string.Empty;
    private string defaultBlockInput = string.Empty;
    private int newChannelId;
    private string newChannelName = string.Empty;
    private string? newChannelError;
    // Custom channel numbers being typed, applied when the field is left (an invalid one is put back).
    private readonly Dictionary<ChannelSetting, int> channelIdDrafts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ChannelSetting, string> channelIdErrors = new(ReferenceEqualityComparer.Instance);

    private void DrawSettingsTabs()
    {
        W.Segmented("##settingsTabs", SettingsTabs, ref settingsTab, 0f);
        Gap(8f);
    }

    private void DrawSettingsPage()
    {
        switch (settingsTab)
        {
            case 0:
                DrawGeneralSettings();
                break;
            case 1:
                DrawNewReactionSettings();
                break;
            case 2:
                DrawCustomChannelSettings();
                break;
            case 3:
                DrawAppearanceSettings();
                break;
            default:
                DrawAbout();
                break;
        }
    }

    private void DrawGeneralSettings()
    {
        using (W.Card("chat", "Chat"))
        {
            var ignoreOwn = Config.IgnoreOwnMessages;
            if (W.Toggle("Ignore my own messages##ignoreOwn", ref ignoreOwn))
            {
                Config.IgnoreOwnMessages = ignoreOwn;
                Changed();
            }
            Hint("Your own chat messages never trigger anything. This stops a trigger that posts in chat from triggering itself over and over. Turn this off only to test triggers on yourself.");
        }

        using (W.Card("notificationDefaults", "Notifications", "Triggers set to Default use these"))
        {
            var progress = Config.ShowReactionNotifications;
            if (W.Toggle("While a trigger runs and when it ends##showProgress", ref progress))
            {
                Config.ShowReactionNotifications = progress;
                Changed();
            }
            Gap(2f);
            var suppressed = Config.ShowSuppressedReactionNotifications;
            if (W.Toggle("When a trigger is ignored##showSuppressed", ref suppressed,
                         tooltip: "When a trigger is busy or on cooldown. Shown at most once every few seconds."))
            {
                Config.ShowSuppressedReactionNotifications = suppressed;
                Changed();
            }
        }

        using (W.Card("help", "Commands"))
        {
            Hint("/pmkk (or /puppetmasterkk) opens this window. /pmkk on|off [name] turns every trigger (or the named ones) " +
                 "on or off. /pmkk logging on|off|clear|save controls the message log, and /pmkk viz opens the Activity page.");
        }
    }

    private void DrawNewReactionSettings()
    {
        W.TextWrapped("New triggers start with these. Existing triggers keep their own settings.", Theme.Dim);
        Gap(4f);

        using (W.Card("defaultCommands", "Commands"))
        {
            var motionOnly = Config.DefaultMotionOnly;
            if (W.Toggle("Hide emote text##defaultMotionOnly", ref motionOnly))
            {
                Config.DefaultMotionOnly = motionOnly;
                Changed();
            }
            Gap();
            Label("Which commands can run?");
            var mode = Config.DefaultAllowAllCommands ? 1 : 0;
            if (W.Segmented("##defaultCommandMode", CommandModes, ref mode, W.SegmentedWidth(CommandModes)))
            {
                if (mode == 1)
                {
                    allowAllTarget = null; // null: the defaults for new reactions
                    confirmAllowAll = true;
                }
                else
                {
                    Config.DefaultAllowAllCommands = false;
                    Changed();
                }
            }
            Gap();
            W.Heading(Config.DefaultAllowAllCommands ? "Also allowed (chat and plugin commands)" : "Allowed");
            if (StringListEditor("defaultAllow", Config.DefaultCommandWhitelist, ref defaultAllowInput, "/command",
                                 Config.DefaultAllowAllCommands ? "None." : "None. Emotes still run.",
                                 input => PluginUiLogic.AddCommandRule(Config.DefaultCommandWhitelist, Config.DefaultCommandBlacklist, input)))
                Changed();
            Gap();
            W.Heading("Blocked");
            if (StringListEditor("defaultBlock", Config.DefaultCommandBlacklist, ref defaultBlockInput, "/command", "Nothing blocked.",
                                 input => PluginUiLogic.AddCommandRule(Config.DefaultCommandBlacklist, Config.DefaultCommandWhitelist, input)))
                Changed();
        }

        var count = Config.DefaultEnabledChannels.Count;
        using (W.Card("defaultChannels", "Channels", count == 1 ? "1 picked" : $"{count} picked"))
        {
            DrawChannelChips(Config.DefaultEnabledChannels, "New triggers start without channels.");
            Gap(2f);
            if (W.SecondaryButton("Pick channels##pickDefaultChannels"))
                OpenChannelPicker("Channels for new triggers", Config.DefaultEnabledChannels, null);
            Hint("A trigger made from the message log listens only to the channel that message came in on.");
        }
    }

    private void DrawCustomChannelSettings()
    {
        using (W.Card("customChannels", "Custom channels"))
        {
            W.TextWrapped("Chat channels the game uses that Dalamud doesn't have a name for. Find their numbers in the message log; the # button on a message adds its channel here.", Theme.Dim);
            Gap();

            var remove = -1;
            var channels = Config.CustomChannels;
            var shown = 0;
            for (var i = 0; i < channels.Count; i++)
            {
                var channel = channels[i];
                if (IsOfficialChannel(channel.ChatType) && channel.ChatType >= 0)
                    continue; // an official channel in the old custom list: it's in the picker already
                shown++;
                ImGui.PushID(i);

                if (!channelIdDrafts.TryGetValue(channel, out var draft))
                    draft = channel.ChatType;
                ImGui.SetNextItemWidth(Theme.S(120f));
                if (ImGui.InputInt("##channelId", ref draft, 0, 0))
                    channelIdDrafts[channel] = draft;
                if (ImGui.IsItemHovered())
                    W.Tooltip("The log type number");
                if (ImGui.IsItemDeactivatedAfterEdit())
                    ApplyChannelId(channel, draft);

                ImGui.SameLine(0f, Theme.Space.Tight);
                var name = channel.Name;
                if (W.TextInput("##channelName", ref name, "Name", -(ImGui.GetFrameHeight() + Theme.Space.Tight), 64))
                {
                    channel.Name = name;
                    Changed();
                }
                ImGui.SameLine(0f, Theme.Space.Tight);
                if (W.IconButton(FontAwesomeIcon.Trash, "##deleteChannel", "Delete this channel", danger: true))
                    remove = i;

                if (channelIdErrors.TryGetValue(channel, out var error))
                    W.TextWrapped(error, Theme.Negative);
                ImGui.PopID();
            }
            if (shown == 0)
                ImGui.TextColored(Theme.Faint, "No custom channels.");

            if (remove >= 0)
            {
                var channel = channels[remove];
                foreach (var reaction in Config.Reactions)
                {
                    if (reaction.EnabledChannels.Remove(channel.ChatType))
                        ChatHandler.InvalidateReaction(reaction, false);
                }
                Config.DefaultEnabledChannels.Remove(channel.ChatType);
                channels.RemoveAt(remove);
                channelIdDrafts.Remove(channel);
                channelIdErrors.Remove(channel);
                Changed();
            }

            W.Divider(Theme.S(6f));
            Label("Add a channel");
            ImGui.SetNextItemWidth(Theme.S(120f));
            ImGui.InputInt("##newChannelId", ref newChannelId, 0, 0);
            if (ImGui.IsItemHovered())
                W.Tooltip("The log type number");
            ImGui.SameLine(0f, Theme.Space.Tight);
            var addW = W.ButtonWidth("Add", FontAwesomeIcon.Plus);
            W.TextInput("##newChannelName", ref newChannelName, "Name", -(addW + Theme.Space.Tight), 64);
            ImGui.SameLine(0f, Theme.Space.Tight);
            if (W.IconTextButton(FontAwesomeIcon.Plus, "Add##addChannel", ButtonKind.Secondary, new Vector2(addW, ImGui.GetFrameHeight())))
            {
                var channel = new ChannelSetting
                {
                    ChatType = newChannelId,
                    Name = string.IsNullOrWhiteSpace(newChannelName) ? $"Custom {newChannelId}" : newChannelName.Trim(),
                };
                newChannelError = PluginUiLogic.ValidateCustomChannelId(channel, newChannelId, Config.CustomChannels, IsOfficialChannel);
                if (newChannelError == null)
                {
                    channels.Add(channel);
                    newChannelId = 0;
                    newChannelName = string.Empty;
                    Changed();
                }
            }
            if (newChannelError != null)
                W.TextWrapped(newChannelError, Theme.Negative);
        }
    }

    private void ApplyChannelId(ChannelSetting channel, int id)
    {
        var error = PluginUiLogic.ValidateCustomChannelId(channel, id, Config.CustomChannels, IsOfficialChannel);
        if (error != null)
        {
            channelIdDrafts[channel] = channel.ChatType;
            channelIdErrors[channel] = error;
            return;
        }
        channelIdErrors.Remove(channel);
        channelIdDrafts.Remove(channel);
        var previous = channel.ChatType;
        if (previous == id)
            return;
        channel.ChatType = id;
        // Reactions listening to the old number follow it to the new one.
        foreach (var reaction in Config.Reactions)
        {
            if (!reaction.EnabledChannels.Remove(previous))
                continue;
            if (!reaction.EnabledChannels.Contains(id))
                reaction.EnabledChannels.Add(id);
            ChatHandler.InvalidateReaction(reaction, false);
        }
        if (Config.DefaultEnabledChannels.Remove(previous) && !Config.DefaultEnabledChannels.Contains(id))
            Config.DefaultEnabledChannels.Add(id);
        Changed();
    }

    // images\icon.png next to the dll.
    private static readonly string IconPath = System.IO.Path.Combine(
        Service.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty, "images", "icon.png");

    private void DrawAbout()
    {
        using (W.Card("about", null))
        {
            // The icon as it is: drawn on nothing, so its transparent corners stay transparent.
            // Big enough to see the details: the card's width, up to 360 design px.
            var side = System.MathF.Min(Theme.S(360f), W.Avail());
            if (Service.TextureProvider.GetFromFile(IconPath).TryGetWrap(out var icon, out _) && icon != null)
            {
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + System.MathF.Max(0f, (W.Avail() - side) * 0.5f));
                ImGui.Image(icon.Handle, new System.Numerics.Vector2(side, side));
            }

            Gap(4f);
            using (Fonts.Display.Push())
                Centered("PuppetMasterKK", Theme.Ink);
            Centered(Version, Theme.Dim);
            Gap(8f);
            Centered("Let others tell you what to do.", Theme.Dim);
            Gap(8f);
            W.TextWrapped("\"I've always liked the plugin but I wanted it to be a bit more robust and secure....so I just did it. " +
                          "I have no idea who the original original original author is, but fuck it, add me to the list now I guess.\"",
                          Theme.Ink);
            Centered("- phys1ks", Theme.Dim);
            Gap(8f);
            Centered("Based on DodingDaga's Puppet Master. Emote replies come from Right Back At You.", Theme.Faint);
            const string originalRepo = "https://github.com/dodingdaga/DalamudPlugins";
            var linkWidth = ImGui.CalcTextSize(originalRepo).X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + System.MathF.Max(0f, (W.Avail() - linkWidth) * 0.5f));
            if (W.Link(originalRepo, "Open the original repository in your browser"))
                Dalamud.Utility.Util.OpenLink(originalRepo);
        }
    }

    private static void Centered(string text, System.Numerics.Vector4 color)
    {
        var width = ImGui.CalcTextSize(text).X;
        var avail = W.Avail();
        if (width < avail)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail - width) * 0.5f);
            ImGui.TextColored(color, text);
        }
        else
        {
            W.TextWrapped(text, color);
        }
    }

    private static void DrawAppearanceSettings()
    {
        var accent = Config.Accent;
        var textScale = Config.TextScale;
        var colorblind = Config.Colorblind;
        if (Appearance.DrawCard(ref accent, ref textScale, ref colorblind))
        {
            Config.Accent = accent;
            Config.TextScale = textScale;
            Config.Colorblind = colorblind;
            Changed();
        }
    }
}
