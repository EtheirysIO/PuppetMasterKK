using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using phys1ksUI;

namespace PuppetMasterKK.UI;

internal sealed partial class MainWindow
{
    private readonly record struct ChannelEntry(int Id, string Name);

    // Built once: the official channels never change while the game runs. Custom channels are read live.
    private static readonly ChannelEntry[][] OfficialCategories = BuildOfficialCategories();
    private static readonly Dictionary<int, string> OfficialNames = BuildOfficialNames();
    private static readonly string[] CategoryNames = PluginUiLogic.ChannelCategoryLabels; // ..., "Custom" last

    private static readonly Dictionary<XivChatType, string> FriendlyNames = new()
    {
        [XivChatType.TellIncoming] = "Tell",
        [XivChatType.FreeCompany] = "Free Company",
        [XivChatType.NoviceNetwork] = "Novice Network",
        [XivChatType.CrossParty] = "Cross-world party",
        [XivChatType.PvPTeam] = "PvP team",
        [XivChatType.CustomEmote] = "Custom emote",
        [XivChatType.StandardEmote] = "Standard emote",
        [XivChatType.Ls1] = "LS1", [XivChatType.Ls2] = "LS2", [XivChatType.Ls3] = "LS3", [XivChatType.Ls4] = "LS4",
        [XivChatType.Ls5] = "LS5", [XivChatType.Ls6] = "LS6", [XivChatType.Ls7] = "LS7", [XivChatType.Ls8] = "LS8",
        [XivChatType.CrossLinkShell1] = "CWLS1", [XivChatType.CrossLinkShell2] = "CWLS2", [XivChatType.CrossLinkShell3] = "CWLS3",
        [XivChatType.CrossLinkShell4] = "CWLS4", [XivChatType.CrossLinkShell5] = "CWLS5", [XivChatType.CrossLinkShell6] = "CWLS6",
        [XivChatType.CrossLinkShell7] = "CWLS7", [XivChatType.CrossLinkShell8] = "CWLS8",
    };

    private static readonly XivChatType[] CommonChannels =
    [
        XivChatType.Say, XivChatType.Yell, XivChatType.Shout, XivChatType.TellIncoming, XivChatType.Party,
        XivChatType.CrossParty, XivChatType.Alliance, XivChatType.FreeCompany, XivChatType.NoviceNetwork,
        XivChatType.PvPTeam, XivChatType.StandardEmote, XivChatType.CustomEmote,
    ];

    private static readonly XivChatType[] CrossWorldLinkshells =
    [
        XivChatType.CrossLinkShell1, XivChatType.CrossLinkShell2, XivChatType.CrossLinkShell3, XivChatType.CrossLinkShell4,
        XivChatType.CrossLinkShell5, XivChatType.CrossLinkShell6, XivChatType.CrossLinkShell7, XivChatType.CrossLinkShell8,
    ];

    private static readonly XivChatType[] Linkshells =
    [
        XivChatType.Ls1, XivChatType.Ls2, XivChatType.Ls3, XivChatType.Ls4,
        XivChatType.Ls5, XivChatType.Ls6, XivChatType.Ls7, XivChatType.Ls8,
    ];

    private static ChannelEntry[][] BuildOfficialCategories()
    {
        var used = new HashSet<int>();
        ChannelEntry[] From(XivChatType[] types)
        {
            var list = new ChannelEntry[types.Length];
            for (var i = 0; i < types.Length; i++)
            {
                used.Add((int)types[i]);
                list[i] = new ChannelEntry((int)types[i], OfficialName(types[i]));
            }
            return list;
        }

        var categories = new List<ChannelEntry[]> { From(CommonChannels), From(CrossWorldLinkshells), From(Linkshells) };
        foreach (var label in PluginUiLogic.AdditionalChannelCategoryLabels)
        {
            var list = new List<ChannelEntry>();
            foreach (var type in Enum.GetValues<XivChatType>())
            {
                var id = (int)type;
                if (type == XivChatType.None || type == XivChatType.TellOutgoing || used.Contains(id))
                    continue;
                if (PluginUiLogic.GetAdvancedChannelCategory(type.ToString()) != label)
                    continue;
                used.Add(id);
                list.Add(new ChannelEntry(id, OfficialName(type)));
            }
            categories.Add(list.ToArray());
        }
        return categories.ToArray();
    }

    private static Dictionary<int, string> BuildOfficialNames()
    {
        var names = new Dictionary<int, string>();
        foreach (var type in Enum.GetValues<XivChatType>())
            names.TryAdd((int)type, OfficialName(type));
        return names;
    }

    private static string OfficialName(XivChatType type) => FriendlyNames.TryGetValue(type, out var name) ? name : type.ToString();

    private static bool IsOfficialChannel(int id) => OfficialNames.ContainsKey(id);

    private static string ChannelName(int id)
    {
        if (OfficialNames.TryGetValue(id, out var name))
            return name;
        foreach (var custom in Config.CustomChannels)
        {
            if (custom.ChatType == id)
                return string.IsNullOrWhiteSpace(custom.Name) ? $"Custom {id}" : custom.Name;
        }
        return $"Channel {id}";
    }

    private static IReadOnlyList<ChannelEntry> CategoryChannels(int category)
    {
        if (category < OfficialCategories.Length)
            return OfficialCategories[category];
        var custom = new List<ChannelEntry>();
        foreach (var channel in Config.CustomChannels)
        {
            if (channel.ChatType is >= 0 and <= ushort.MaxValue && !IsOfficialChannel(channel.ChatType))
                custom.Add(new ChannelEntry(channel.ChatType, ChannelName(channel.ChatType)));
        }
        return custom;
    }

    // ───────────────────────── Chips and the card ─────────────────────────

    private void DrawChannelsCard(Reaction reaction)
    {
        var count = reaction.EnabledChannels.Count;
        using (W.Card("channels", "Channels", count == 1 ? "1 picked" : $"{count} picked"))
        {
            DrawChannelChips(reaction.EnabledChannels, "This reaction isn't listening anywhere yet.");
            Gap(2f);
            if (W.SecondaryButton("Pick channels##pickChannels"))
                OpenChannelPicker($"Channels for {DisplayName(reaction)}", reaction.EnabledChannels,
                                  () => ChatHandler.InvalidateReaction(reaction, false));
        }
    }

    /// <summary>The picked channels as chips, wrapping to the card's width; public channels are tinted as a warning.</summary>
    private static void DrawChannelChips(List<int> channels, string emptyText)
    {
        if (channels.Count == 0)
        {
            W.Banner(emptyText, Theme.Warning);
            return;
        }
        var right = ImGui.GetCursorScreenPos().X + W.Avail();
        var spacing = Theme.S(6f);
        for (var i = 0; i < channels.Count; i++)
        {
            var name = ChannelName(channels[i]);
            var color = PluginUiLogic.IsPublicChannel(channels[i]) ? Theme.Warning : Theme.Dim;
            var width = W.ChipWidth(name, color);
            if (i > 0)
            {
                ImGui.SameLine(0f, spacing);
                if (ImGui.GetCursorScreenPos().X + width > right)
                    ImGui.NewLine();
            }
            W.Chip(name, color);
            if (ImGui.IsItemHovered())
                W.Tooltip($"Log type {channels[i]}");
        }
    }

    // ───────────────────────── Picker dialog ─────────────────────────

    private bool pickerOpen;
    private string pickerTitle = string.Empty;
    private List<int>? pickerTarget;
    private Action? pickerChanged;
    private int pickerCategory;
    private string pickerSearch = string.Empty;
    private readonly List<string> pickerCategoryLabels = [];

    private void OpenChannelPicker(string title, List<int> target, Action? onChanged)
    {
        pickerTitle = $"{title}###channelPicker";
        pickerTarget = target;
        pickerChanged = onChanged;
        pickerSearch = string.Empty;
        pickerOpen = true;
    }

    private void DrawChannelPicker()
    {
        if (!pickerOpen || pickerTarget == null)
            return;
        var target = pickerTarget;
        Modal.Draw(pickerTitle, ref pickerOpen, () => DrawChannelPickerBody(target));
        if (!pickerOpen)
        {
            pickerTarget = null;
            pickerChanged = null;
        }
    }

    private void DrawChannelPickerBody(List<int> target)
    {
        pickerCategoryLabels.Clear();
        for (var i = 0; i < CategoryNames.Length; i++)
        {
            var channels = CategoryChannels(i);
            var picked = 0;
            foreach (var channel in channels)
            {
                if (target.Contains(channel.Id))
                    picked++;
            }
            pickerCategoryLabels.Add(picked > 0 ? $"{CategoryNames[i]}  ({picked}/{channels.Count})" : CategoryNames[i]);
        }

        var width = Theme.S(560f);
        ImGui.BeginGroup();
        W.Combo("##pickerCategory", pickerCategoryLabels, ref pickerCategory, Theme.S(220f));
        ImGui.SameLine(0f, Theme.Space.Tight);
        W.SearchBox("##pickerSearch", ref pickerSearch, "Search by name or number", width - Theme.S(220f) - Theme.Space.Tight, 64);
        ImGui.EndGroup();

        var visible = new List<ChannelEntry>();
        if (string.IsNullOrWhiteSpace(pickerSearch))
        {
            visible.AddRange(CategoryChannels(Math.Clamp(pickerCategory, 0, CategoryNames.Length - 1)));
        }
        else
        {
            for (var i = 0; i < CategoryNames.Length; i++)
            {
                foreach (var channel in CategoryChannels(i))
                {
                    if (channel.Name.Contains(pickerSearch, StringComparison.OrdinalIgnoreCase) ||
                        channel.Id.ToString().Contains(pickerSearch, StringComparison.Ordinal))
                        visible.Add(channel);
                }
            }
        }

        Gap(4f);
        if (ImGui.BeginChild("##pickerChannels", new Vector2(width, Theme.S(300f)), false))
        {
            if (visible.Count == 0)
                ImGui.TextColored(Theme.Faint, pickerCategory == CategoryNames.Length - 1 && string.IsNullOrWhiteSpace(pickerSearch)
                    ? "No custom channels yet. Add them in Settings > Custom channels, or from the message log."
                    : "No channels match.");
            using var table = W.Table("##pickerGrid", 3, ImGuiTableFlags.SizingStretchSame);
            if (table.Open)
            {
                foreach (var channel in visible)
                {
                    ImGui.TableNextColumn();
                    var on = target.Contains(channel.Id);
                    ImGui.PushID(channel.Id);
                    if (W.Checkbox($"{channel.Name}##channel", ref on, tooltip: $"Log type {channel.Id}"))
                    {
                        PluginUiLogic.SetChannel(target, channel.Id, on);
                        pickerChanged?.Invoke();
                        Changed();
                    }
                    ImGui.PopID();
                }
            }
        }
        ImGui.EndChild();

        Gap(4f);
        if (W.CompactButton("Select all shown", enabled: visible.Count > 0))
        {
            foreach (var channel in visible)
                PluginUiLogic.SetChannel(target, channel.Id, true);
            pickerChanged?.Invoke();
            Changed();
        }
        ImGui.SameLine(0f, Theme.Space.Tight);
        if (W.CompactButton("Clear shown", enabled: visible.Count > 0))
        {
            foreach (var channel in visible)
                PluginUiLogic.SetChannel(target, channel.Id, false);
            pickerChanged?.Invoke();
            Changed();
        }
        ImGui.SameLine(0f, Theme.Space.Tight);
        ImGui.TextColored(Theme.Dim, target.Count == 1 ? "1 channel picked" : $"{target.Count} channels picked");

        W.Divider(Theme.S(6f));
        if (W.PrimaryButton("Done##pickerDone"))
            Modal.Close(ref pickerOpen);
    }
}
