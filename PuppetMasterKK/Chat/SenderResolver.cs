using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System;
using System.Text;

namespace PuppetMasterKK;

// Works out who sent a chat message, or who emoted at you, from the game's own lists. Framework thread only: it reads
// the friend list, FC member list, party list and object table.
internal static unsafe class SenderResolver
{
    public static SenderInfo FromChat(XivChatType type, SeString sender, SeString message)
    {
        if (type == XivChatType.TellOutgoing)
            return SenderInfo.Unknown with { IsSelf = true };

        var player = FirstPlayer(sender);
        var isEmoteLine = type == XivChatType.StandardEmote;
        if (player == null && isEmoteLine)
        {
            // Standard emote lines have no sender: the player is a link at the start of the message ("Bob waves.").
            // Your own lines start with text instead ("You wave to Bob." links Bob, the target, not you).
            if (!StartsWithPlayer(message, out player))
                return SenderInfo.Unknown with { IsSelf = true };
        }
        else if (player == null && PluginUiLogic.IsPlayerChatChannel((int)type) &&
                 !string.IsNullOrWhiteSpace(StripGlyphs(sender.TextValue)))
        {
            // On player channels everyone else's name is a player link. A plain name is yours, even when the chat
            // log shortens it ("J. Doe").
            return new SenderInfo(Service.PlayerState.CharacterName, HomeWorldName(), true, false, false, false);
        }

        string name;
        uint worldId;
        string world;
        if (player != null)
        {
            name = player.PlayerName;
            worldId = player.World.RowId;
            world = WorldName(player.World);
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
            world = HomeWorldName();
        }

        var nearby = worldId != 0 ? FindNearbyPlayer(name, worldId) : null;
        var isParty = type is XivChatType.Party or XivChatType.CrossParty or XivChatType.Alliance ||
                      IsInPartyList(name, worldId) ||
                      (nearby != null && (nearby.StatusFlags & (StatusFlags.PartyMember | StatusFlags.AllianceMember)) != 0);
        var isFreeCompany = type == XivChatType.FreeCompany || IsFreeCompanyMember(name, worldId);
        var isFriend = IsFriend(name, worldId) || (nearby != null && (nearby.StatusFlags & StatusFlags.Friend) != 0);
        return new SenderInfo(name, world, isSelf, isFriend, isFreeCompany, isParty);
    }

    private static bool StartsWithPlayer(SeString text, out PlayerPayload? player)
    {
        player = null;
        foreach (var payload in text.Payloads)
        {
            if (payload is PlayerPayload found)
            {
                player = found;
                return true;
            }
            if (payload is TextPayload { Text: { } textValue } && !string.IsNullOrWhiteSpace(StripGlyphs(textValue)))
                return false;
        }
        return false;
    }

    private static string HomeWorldName() => WorldName(Service.PlayerState.HomeWorld);

    private static string WorldName(RowRef<World> world) => world.ValueNullable?.Name.ExtractText() ?? string.Empty;

    private static PlayerPayload? FirstPlayer(SeString text)
    {
        foreach (var payload in text.Payloads)
        {
            if (payload is PlayerPayload found)
                return found;
        }
        return null;
    }

    public static SenderInfo FromCharacter(IPlayerCharacter character)
    {
        var name = character.Name.TextValue;
        var worldId = character.HomeWorld.RowId;
        var world = WorldName(character.HomeWorld);
        var flags = character.StatusFlags;
        return new SenderInfo(
            name,
            world,
            IsLocalPlayer(name, worldId),
            (flags & StatusFlags.Friend) != 0 || IsFriend(name, worldId),
            IsFreeCompanyMember(name, worldId),
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

    // Only the game's member list counts (it's filled once the FC member list has been opened this session). An FC tag
    // isn't proof: another FC can use the same tag.
    private static bool IsFreeCompanyMember(string name, uint worldId)
    {
        if (worldId == 0)
            return false;
        var members = InfoProxyFreeCompanyMember.Instance();
        return members != null && members->GetEntryByName(name, (ushort)worldId) != null;
    }

    private static bool IsInPartyList(string name, uint worldId)
    {
        // A name without a world could be anyone's.
        if (worldId == 0)
            return false;
        foreach (var member in Service.PartyList)
        {
            if (member.Name.TextValue.Equals(name, StringComparison.Ordinal) &&
                member.World.RowId == worldId)
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
            if (c is >= '\uE000' and <= '\uF8FF')
                continue;
            builder.Append(c);
        }
        return builder.ToString().Trim();
    }
}
