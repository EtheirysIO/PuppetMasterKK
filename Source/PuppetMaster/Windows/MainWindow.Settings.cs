using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using phys1ksUI;

namespace PuppetMaster.Windows;

internal sealed partial class MainWindow
{
    private static readonly string[] SettingsTabs = ["General", "New reactions", "Custom channels", "Appearance"];
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
            default:
                DrawAppearanceSettings();
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
            Hint("Your own chat lines never trigger a reaction. Keeps a reaction that posts to chat from setting itself off " +
                 "over and over. Turn off only to test reactions on yourself.");
        }

        using (W.Card("notificationDefaults", "Notifications", "Reactions set to Default use these"))
        {
            var progress = Config.ShowReactionNotifications;
            if (W.Toggle("While a reaction runs and when it ends##showProgress", ref progress))
            {
                Config.ShowReactionNotifications = progress;
                Changed();
            }
            Gap(2f);
            var suppressed = Config.ShowSuppressedReactionNotifications;
            if (W.Toggle("When a trigger is ignored##showSuppressed", ref suppressed,
                         tooltip: "Shown at most every few seconds, when a reaction is busy or cooling down"))
            {
                Config.ShowSuppressedReactionNotifications = suppressed;
                Changed();
            }
        }

        using (W.Card("help", "Commands"))
        {
            Hint("/puppetmaster opens this window. /puppetmaster on|off [name] turns every reaction (or the named ones) on or " +
                 "off. /puppetmaster logging on|off|clear|save controls the message log, and /puppetmaster viz opens Activity.");
        }
    }

    private void DrawNewReactionSettings()
    {
        W.TextWrapped("New reactions start with these. Existing reactions keep their own settings.", Theme.Dim);
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
            DrawChannelChips(Config.DefaultEnabledChannels, "New reactions start without channels.");
            Gap(2f);
            if (W.SecondaryButton("Pick channels##pickDefaultChannels"))
                OpenChannelPicker("Channels for new reactions", Config.DefaultEnabledChannels, null);
            Hint("A reaction made from the message log listens only to the channel that message came in on.");
        }
    }

    private void DrawCustomChannelSettings()
    {
        using (W.Card("customChannels", "Custom channels"))
        {
            W.TextWrapped("Chat log types the game uses but Dalamud doesn't name. Find their numbers with the message log " +
                          "(the # button on a row adds one here).", Theme.Dim);
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
