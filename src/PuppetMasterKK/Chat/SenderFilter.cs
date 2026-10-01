using System;
using System.Collections.Generic;

namespace PuppetMasterKK;

// Who a chat message (or an emote) came from, as far as the game can tell. Name and World are empty when the sender
// isn't a player (system messages, custom channels).
internal readonly record struct SenderInfo(
    string Name,
    string World,
    bool IsSelf,
    bool IsFriend,
    bool IsFreeCompany,
    bool IsParty)
{
    public static SenderInfo Unknown { get; } = new(string.Empty, string.Empty, false, false, false, false);
}

// Who may trigger a reaction (or get an emote reply). The defaults are what a new reaction gets; reactions from
// before sender filters existed are migrated to Anyone so nothing that worked stops working.
public class SenderFilter
{
    public bool Anyone { get; set; } = false;
    public bool Friends { get; set; } = true;
    public bool FreeCompany { get; set; } = true;
    public bool Party { get; set; } = true;
    // "Name@World", or just "Name" for any world. Case-insensitive.
    public List<string> Named { get; set; } = [];

    public static SenderFilter AnyoneFilter()
    {
        return new SenderFilter { Anyone = true, Friends = false, FreeCompany = false, Party = false };
    }

    public SenderFilter Clone()
    {
        return new SenderFilter
        {
            Anyone = Anyone,
            Friends = Friends,
            FreeCompany = FreeCompany,
            Party = Party,
            Named = Named != null ? new List<string>(Named) : [],
        };
    }

    // True when the filter needs to know who sent a message (Anyone needs nothing).
    public bool NeedsSender => !Anyone;

    internal bool Allows(in SenderInfo sender)
    {
        if (Anyone)
            return true;
        if (Friends && sender.IsFriend)
            return true;
        if (FreeCompany && sender.IsFreeCompany)
            return true;
        if (Party && sender.IsParty)
            return true;
        return MatchesNamed(Named, sender);
    }

    internal static bool MatchesNamed(IReadOnlyList<string>? named, in SenderInfo sender)
    {
        if (named == null || sender.Name.Length == 0)
            return false;
        foreach (var entry in named)
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;
            var at = entry.IndexOf('@');
            var name = (at < 0 ? entry : entry[..at]).Trim();
            var world = at < 0 ? string.Empty : entry[(at + 1)..].Trim();
            if (!name.Equals(sender.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (world.Length == 0 || world.Equals(sender.World, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public string Describe()
    {
        if (Anyone)
            return "Anyone";
        var parts = new List<string>(4);
        if (Friends) parts.Add("Friends");
        if (FreeCompany) parts.Add("Free Company");
        if (Party) parts.Add("Party");
        var namedCount = Named?.Count ?? 0;
        if (namedCount > 0) parts.Add(namedCount == 1 ? "1 player" : $"{namedCount} players");
        return parts.Count == 0 ? "Nobody" : string.Join(", ", parts);
    }
}
