using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using System;
using System.Text;

namespace PuppetMaster;

// Works out who sent a chat message, or who emoted at you, from the game's own lists. Framework thread only: it reads
// the friend list, FC member list, party list and object table.
internal static unsafe class SenderResolver
{
    public static SenderInfo FromChat(XivChatType type, SeString sender)
    {
        PlayerPayload? player = null;
        foreach (var payload in sender.Payloads)
        {
            if (payload is PlayerPayload found)
            {
                player = found;
                break;
            }
        }

        string name;
        uint worldId;
        string world;
        if (player != null)
        {
            name = player.PlayerName;
            worldId = player.World.RowId;
            world = player.World.ValueNullable?.Name.ExtractText() ?? string.Empty;
        }
        else
        {
            // Your own lines (and some channels) carry a plain name, sometimes after a party-slot glyph.
            name = StripGlyphs(sender.TextValue);
            worldId = 0;
            world = string.Empty;
        }

        if (name.Length == 0)
            return SenderInfo.Unknown;

        var isSelf = IsLocalPlayer(name, worldId);
        if (isSelf && worldId == 0)
        {
            worldId = Service.PlayerState.HomeWorld.RowId;
            world = Service.PlayerState.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty;
        }

        var nearby = worldId != 0 ? FindNearbyPlayer(name, worldId) : null;
        var isParty = type is XivChatType.Party or XivChatType.CrossParty or XivChatType.Alliance ||
                      IsInPartyList(name, worldId) ||
                      (nearby != null && (nearby.StatusFlags & (StatusFlags.PartyMember | StatusFlags.AllianceMember)) != 0);
        var isFreeCompany = type == XivChatType.FreeCompany || IsFreeCompanyMember(name, worldId, nearby);
        var isFriend = IsFriend(name, worldId) || (nearby != null && (nearby.StatusFlags & StatusFlags.Friend) != 0);
        return new SenderInfo(name, world, isSelf, isFriend, isFreeCompany, isParty);
    }

    public static SenderInfo FromCharacter(IPlayerCharacter character)
    {
        var name = character.Name.TextValue;
        var worldId = character.HomeWorld.RowId;
        var world = character.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty;
        var flags = character.StatusFlags;
        return new SenderInfo(
            name,
            world,
            IsLocalPlayer(name, worldId),
            (flags & StatusFlags.Friend) != 0 || IsFriend(name, worldId),
            IsFreeCompanyMember(name, worldId, character),
            (flags & (StatusFlags.PartyMember | StatusFlags.AllianceMember)) != 0 || IsInPartyList(name, worldId));
    }

    private static bool IsLocalPlayer(string name, uint worldId)
    {
        var state = Service.PlayerState;
        if (!state.IsLoaded || !name.Equals(state.CharacterName, StringComparison.Ordinal))
            return false;
        return worldId == 0 || worldId == state.HomeWorld.RowId;
    }

    private static bool IsFriend(string name, uint worldId)
    {
        if (worldId == 0)
            return false;
        var friends = InfoProxyFriendList.Instance();
        if (friends == null)
            return false;
        var entry = friends->GetEntryByName(name, (ushort)worldId);
        return entry != null && !entry->WaitingForFriendListApproval;
    }

    private static bool IsFreeCompanyMember(string name, uint worldId, IPlayerCharacter? nearby)
    {
        if (worldId == 0)
            return false;
        // The member list is only filled once the FC window has been opened this session.
        var members = InfoProxyFreeCompanyMember.Instance();
        if (members != null && members->GetEntryByName(name, (ushort)worldId) != null)
            return true;
        // Otherwise a nearby player wearing your FC tag on your home world counts.
        var local = Service.ObjectTable.LocalPlayer;
        if (nearby == null || local == null)
            return false;
        var tag = local.CompanyTag.TextValue;
        return tag.Length > 0 &&
               nearby.HomeWorld.RowId == local.HomeWorld.RowId &&
               tag.Equals(nearby.CompanyTag.TextValue, StringComparison.Ordinal);
    }

    private static bool IsInPartyList(string name, uint worldId)
    {
        foreach (var member in Service.PartyList)
        {
            if (member.Name.TextValue.Equals(name, StringComparison.Ordinal) &&
                (worldId == 0 || member.World.RowId == worldId))
                return true;
        }
        return false;
    }

    private static IPlayerCharacter? FindNearbyPlayer(string name, uint worldId)
    {
        foreach (var gameObject in Service.ObjectTable)
        {
            if (gameObject is IPlayerCharacter player &&
                player.HomeWorld.RowId == worldId &&
                player.Name.TextValue.Equals(name, StringComparison.Ordinal))
                return player;
        }
        return null;
    }

    // Drops the game's private-use glyphs (party slot numbers, cross-world icon) around a plain sender name.
    private static string StripGlyphs(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is >= '' and <= '')
                continue;
            builder.Append(c);
        }
        return builder.ToString().Trim();
    }
}
