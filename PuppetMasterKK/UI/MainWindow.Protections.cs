using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using phys1ksUI;

namespace PuppetMasterKK.UI;

internal sealed partial class MainWindow
{
    // Protections cards whose "other plugins" list is open, and their plugin search.
    private readonly HashSet<string> showAllPlugins = [];
    private string pluginSearch = string.Empty;

    // The three protection switches (chat, risky game commands, plugins), each with its own ticks. Shared by a
    // trigger's Protections card and Settings > New triggers. Returns true when something changed.
    private bool DrawProtectionGroups(string id, ProtectionSettings protections)
    {
        var changed = false;
        Hint("Protected commands only run if you add them to the Allowed list. Untick one to let anyone who can trigger this use it.");
        Gap(4f);

        changed |= DrawGroup(id + "chat", "Protect chat commands", protections.Chat, value => protections.Chat = value,
                             Items(ProtectionGroups.Chat), protections.OpenChat);
        Gap(4f);
        changed |= DrawGroup(id + "risky", "Protect risky game commands", protections.Risky, value => protections.Risky = value,
                             Items(ProtectionGroups.Risky), protections.OpenRisky);
        Gap(4f);
        changed |= DrawPluginGroup(id, protections);
        return changed;
    }

    private static List<(string Key, string Label, string? Tooltip)> Items(ProtectionGroup[] groups)
    {
        var items = new List<(string, string, string?)>(groups.Length);
        foreach (var group in groups)
            items.Add((group.Key, group.Label, group.Description));
        return items;
    }

    private static bool DrawGroup(string id, string label, bool master, Action<bool> setMaster,
                                  IReadOnlyList<(string Key, string Label, string? Tooltip)> items, List<string> open)
    {
        var changed = false;
        var on = master;
        if (W.Toggle($"{label}##{id}", ref on))
        {
            setMaster(on);
            master = on;
            changed = true;
        }
        if (items.Count == 0)
            return changed;
        Gap(2f);
        ImGui.Indent(Theme.S(12f));
        changed |= CheckboxGrid(id, items, open, master);
        ImGui.Unindent(Theme.S(12f));
        return changed;
    }

    // Ticked = protected. Greyed out while the group's switch is off (everything in it runs then).
    private static bool CheckboxGrid(string id, IReadOnlyList<(string Key, string Label, string? Tooltip)> items, List<string> open,
                                     bool enabled)
    {
        var changed = false;
        var column = 0f;
        foreach (var item in items)
            column = MathF.Max(column, ImGui.CalcTextSize(item.Label).X);
        column += Theme.S(16f) + Theme.S(28f);
        var columns = Math.Max(1, (int)(W.Avail() / column));
        var startX = ImGui.GetCursorPosX();
        for (var i = 0; i < items.Count; i++)
        {
            if (i % columns != 0)
                ImGui.SameLine(startX + i % columns * column);
            var item = items[i];
            var isProtected = ProtectionSettings.IsProtected(open, item.Key);
            if (W.Checkbox($"{item.Label}##{id}{item.Key}", ref isProtected, tooltip: item.Tooltip, enabled: enabled))
            {
                ProtectionSettings.SetProtected(open, item.Key, isProtected);
                changed = true;
            }
        }
        return changed;
    }

    private bool DrawPluginGroup(string id, ProtectionSettings protections)
    {
        var plugins = Service.CommandPlugins();
        var risky = new List<(string Key, string Label, string? Tooltip)>();
        var others = new List<(string Key, string Label, string? Tooltip)>();
        foreach (var plugin in plugins)
        {
            if (plugin.Risk != null)
                risky.Add((plugin.InternalName, plugin.Name, plugin.Risk));
            else
                others.Add((plugin.InternalName, plugin.Name, null));
        }

        var changed = DrawGroup(id + "plugins", "Protect plugin commands", protections.Plugins,
                                value => protections.Plugins = value, risky, protections.OpenPlugins);
        if (plugins.Count == 0)
        {
            Hint("No other plugins with commands are loaded.");
            return changed;
        }
        if (others.Count == 0)
            return changed;

        ImGui.Indent(Theme.S(12f));
        Gap(2f);
        var expanded = showAllPlugins.Contains(id);
        if (W.SecondaryButton(expanded ? $"Hide other plugins##{id}morePlugins" : $"Show {others.Count} other plugins##{id}morePlugins"))
        {
            if (expanded)
                showAllPlugins.Remove(id);
            else
                showAllPlugins.Add(id);
            expanded = !expanded;
        }
        if (expanded)
        {
            Gap(2f);
            W.SearchBox($"##{id}pluginSearch", ref pluginSearch, "Find a plugin", MathF.Min(W.Avail(), Theme.S(240f)));
            Gap(2f);
            var shown = others;
            if (!string.IsNullOrWhiteSpace(pluginSearch))
                shown = others.FindAll(plugin => plugin.Label.Contains(pluginSearch.Trim(), StringComparison.OrdinalIgnoreCase) ||
                                                 plugin.Key.Contains(pluginSearch.Trim(), StringComparison.OrdinalIgnoreCase));
            if (shown.Count == 0)
                Hint("No plugins match.");
            else
                changed |= CheckboxGrid(id + "otherPlugins", shown, protections.OpenPlugins, protections.Plugins);
        }
        ImGui.Unindent(Theme.S(12f));
        return changed;
    }
}
