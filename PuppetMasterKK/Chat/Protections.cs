using System;
using System.Collections.Generic;

namespace PuppetMasterKK;

// One protection that can be switched off on its own: a chat channel, a kind of risky game command.
internal sealed record ProtectionGroup(string Key, string Label, string Description, string[] Forms);

internal static class ProtectionGroups
{
    // Commands that post text other players can read.
    public static readonly ProtectionGroup[] Chat =
    [
        new("say", "Say", "/say", ["/say", "/s"]),
        new("yell", "Yell", "/yell", ["/yell", "/y"]),
        new("shout", "Shout", "/shout", ["/shout", "/sh"]),
        new("tell", "Tell", "/tell and /reply", ["/tell", "/t", "/reply", "/r"]),
        new("party", "Party", "/party", ["/party", "/p"]),
        new("alliance", "Alliance", "/alliance", ["/alliance", "/a"]),
        new("fc", "Free Company", "/freecompany", ["/freecompany", "/fc"]),
        new("linkshell", "Linkshells", "/linkshell, /linkshell1 to 8",
            ["/linkshell", "/l", "/linkshell1", "/linkshell2", "/linkshell3", "/linkshell4", "/linkshell5", "/linkshell6",
             "/linkshell7", "/linkshell8", "/l1", "/l2", "/l3", "/l4", "/l5", "/l6", "/l7", "/l8"]),
        new("cwls", "CWLS", "/cwlinkshell, /cwlinkshell1 to 8",
            ["/cwlinkshell", "/cwl", "/cwlinkshell1", "/cwlinkshell2", "/cwlinkshell3", "/cwlinkshell4", "/cwlinkshell5",
             "/cwlinkshell6", "/cwlinkshell7", "/cwlinkshell8", "/cwl1", "/cwl2", "/cwl3", "/cwl4", "/cwl5", "/cwl6", "/cwl7", "/cwl8"]),
        new("novice", "Novice Network", "/novice", ["/novice", "/beginner", "/n", "/b"]),
        new("pvpteam", "PvP Team", "/pvpteam", ["/pvpteam", "/pt"]),
        new("emote", "Emote text", "/emote: custom emote text", ["/emote", "/em"]),
    ];

    // Game commands with real consequences. "Any game command" never covers these.
    public static readonly ProtectionGroup[] Risky =
    [
        new("teleport", "Teleport and return", "/teleport, /return (costs gil, moves you)", ["/teleport", "/tp", "/return"]),
        new("party", "Party", "/join, /leave, /kick, /invite, /leader, /partycmd",
            ["/partycmd", "/pcmd", "/join", "/leave", "/kick", "/invite", "/leader"]),
        new("gear", "Gear and glamour", "/gearset, /glamourplate", ["/gearset", "/gs", "/glamourplate"]),
        new("trade", "Trade", "/trade", ["/trade"]),
        new("social", "Friends and blacklist", "/friendlist, /blacklist, /novicenetworkleave",
            ["/blacklist", "/blist", "/friendlist", "/flist", "/beginnerchannelleave", "/novicenetworkleave"]),
        new("comment", "Search comment", "/searchcomment (others can read it)", ["/searchcomment", "/scomment"]),
        new("hotbar", "Hotbars", "/hotbar, /crosshotbar (can overwrite your hotbars)", ["/hotbar", "/crosshotbar", "/chotbar"]),
        new("reset", "Resets", "/uireset, /hudreset, /clearlog, /cleartellhistory (can't be undone)",
            ["/uireset", "/hudreset", "/clearlog", "/cl", "/cleartellhistory", "/cth"]),
    ];

    // Plugins people could cause trouble with. Shown first, with the reason; every other plugin with commands is
    // listed after them.
    public static readonly Dictionary<string, string> RiskyPlugins = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Lifestream"] = "Teleports you, and travels between worlds and data centers",
        ["Glamourer"] = "Changes how your character looks",
        ["Penumbra"] = "Changes your mods and collections",
        ["CustomizePlus"] = "Changes your character's body",
        ["Brio"] = "Poses and changes characters",
        ["Moodles"] = "Puts statuses on you that others can see",
        ["Honorific"] = "Changes your title",
        ["SimpleHeels"] = "Changes your character's position and pose",
        ["Dropbox"] = "Trades items and gil away",
        ["AutoRetainer"] = "Runs retainer ventures and submarines",
        ["AutoDuty"] = "Queues for and runs duties",
        ["Questionable"] = "Runs quests and moves you",
        ["vnavmesh"] = "Moves you anywhere on the map",
        ["visland"] = "Moves you along routes",
        ["Artisan"] = "Crafts and uses your materials",
        ["PandorasBox"] = "Automates many game actions",
        ["YesAlready"] = "Confirms dialogs for you",
        ["TextAdvance"] = "Accepts quests and advances dialog",
        ["Messenger"] = "Sends tells",
        ["ChatTwo"] = "Sends chat messages",
        ["ExtraChat"] = "Sends chat to extra channels",
        ["AutoLogin"] = "Logs in and out",
        ["RotationSolver"] = "Fights for you",
        ["WrathCombo"] = "Changes your combat settings",
        ["BossMod"] = "Moves and fights for you",
        ["BossModReborn"] = "Moves and fights for you",
        ["Splatoon"] = "Changes what you see",
        ["MareSempiterne"] = "Pairs and syncs with other players",
        ["LightlessSync"] = "Pairs and syncs with other players",
        ["Snowcloak"] = "Pairs and syncs with other players",
        ["Gearsetter"] = "Changes your gear",
        ["Stylist"] = "Changes your gear",
        ["Glamaholic"] = "Changes your glamour plates",
        ["SimpleTweaksPlugin"] = "Many game tweaks",
        ["HaselTweaks"] = "Many game tweaks",
        ["Henchman"] = "Automates many game actions",
        ["ICE"] = "Automates cosmic exploration",
        ["GatherBuddyReborn"] = "Gathers and moves you",
        ["Saucy"] = "Plays minigames",
        ["Automaton"] = "Automates many game actions",
        ["AutoFateGrind"] = "Moves you and fights FATEs",
        ["AutoHook"] = "Fishes for you",
        ["ARControl"] = "Plans retainer ventures",
        ["ChilledLeves"] = "Runs levequests",
        ["LazyLoot"] = "Rolls on loot for you",
        ["FCCH"] = "Moves items in and out of your FC chest",
        ["Workshoppa"] = "Turns in FC workshop materials",
        ["wahventory"] = "Discards items",
        ["MakeShopper"] = "Buys furniture with your gil",
        ["BDTHPlugin"] = "Moves your housing furniture",
        ["ReMakePlacePlugin"] = "Moves your housing furniture",
        ["vfallguy"] = "Moves you",
        ["WaymarkStudio"] = "Places waymarks others can see",
        ["Visibility"] = "Changes what you see",
        ["Browsingway"] = "Opens web pages in your game",
        ["heliosphere-plugin"] = "Downloads and installs mods",
        ["Dalamud.CharacterSync"] = "Copies character settings between characters",
        ["StandalonePluginManager"] = "Installs and updates plugins",
    };
}

// What a trigger protects. Each master switch on means its group's commands only run when listed under Allowed,
// except the ones switched off below it ("Open..."): those run without being listed.
public sealed class ProtectionSettings
{
    public bool Chat { get; set; } = true;
    public List<string> OpenChat { get; set; } = [];
    public bool Risky { get; set; } = true;
    public List<string> OpenRisky { get; set; } = [];
    public bool Plugins { get; set; } = true;
    // Plugins by internal name.
    public List<string> OpenPlugins { get; set; } = [];

    public ProtectionSettings Clone() => new()
    {
        Chat = Chat,
        OpenChat = [.. OpenChat],
        Risky = Risky,
        OpenRisky = [.. OpenRisky],
        Plugins = Plugins,
        OpenPlugins = [.. OpenPlugins],
    };

    // True when a command of this kind (group: its chat or risky group, or the plugin that owns it) runs without being
    // listed under Allowed.
    internal bool IsOpen(CommandKind kind, string? group) => kind switch
    {
        CommandKind.Chat => !Protects(Chat, OpenChat, group),
        CommandKind.Sensitive => !Protects(Risky, OpenRisky, group),
        CommandKind.Plugin => !Protects(Plugins, OpenPlugins, group),
        _ => false,
    };

    private static bool Protects(bool master, List<string> open, string? group)
    {
        if (!master)
            return false;
        // Commands outside every group stay protected while the master switch is on.
        return group == null || !open.Exists(key => key.Equals(group, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsProtected(List<string> open, string key) =>
        !open.Exists(entry => entry.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static void SetProtected(List<string> open, string key, bool isProtected)
    {
        open.RemoveAll(entry => entry.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (!isProtected)
            open.Add(key);
    }
}
