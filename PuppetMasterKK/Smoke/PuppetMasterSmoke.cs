using System;
using System.Collections.Generic;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Lumina.Excel.Sheets;
using phys1ksTest;

namespace PuppetMasterKK.Smoke;

/// <summary>PuppetMasterKK's in-game smoke tests (SmokeTest build only).</summary>
internal static partial class PuppetMasterSmoke
{
    public static void Register(SmokeSuite suite)
    {
        RegisterAuto(suite);
        RegisterActions(suite);
    }

    // The emote hook's "no target" (MimicMode.NoTarget).
    private const ulong NoTarget = 0xE0000000;

    private static EmoteReplies? Replies => Service.plugin?.emoteReplies;

    // The Wave emote's row id, found by its English command so it's the same row on every client language.
    private static uint? WaveEmoteId()
    {
        foreach (var emote in Service.DataManager.GetExcelSheet<Emote>(ClientLanguage.English))
        {
            if (emote.TextCommand.ValueNullable is { } command &&
                command.Command.ExtractText().Equals("/wave", StringComparison.OrdinalIgnoreCase))
                return emote.RowId;
        }
        return null;
    }

    // Null when nothing in the way of an action test; otherwise what to do first.
    private static string? NotBusy()
    {
        var c = Service.Condition;
        if (Service.ObjectTable.LocalPlayer == null)
            return "Log in";
        if (c[ConditionFlag.InCombat])
            return "Leave combat";
        if (c[ConditionFlag.Casting])
            return "Wait for your cast to finish";
        if (c[ConditionFlag.Occupied] || c[ConditionFlag.OccupiedInEvent] || c[ConditionFlag.OccupiedInQuestEvent] ||
            c[ConditionFlag.OccupiedSummoningBell] || c[ConditionFlag.OccupiedInCutSceneEvent] ||
            c[ConditionFlag.BetweenAreas] || c[ConditionFlag.BetweenAreas51])
            return "Finish what you're doing (talking to an NPC, a bell, a cutscene, zoning)";
        if (c[ConditionFlag.Crafting] || c[ConditionFlag.Gathering])
            return "Stop crafting or gathering";
        return null;
    }

    // NotBusy, plus standing on the ground: not mounted, flying, swimming or mid-jump.
    private static string? OnFoot()
    {
        if (NotBusy() is { } busy)
            return busy;
        var c = Service.Condition;
        if (c[ConditionFlag.Mounted] || c[ConditionFlag.RidingPillion])
            return "Dismount";
        if (c[ConditionFlag.InFlight] || c[ConditionFlag.Diving] || c[ConditionFlag.Swimming])
            return "Stand on dry ground";
        if (c[ConditionFlag.Jumping])
            return "Land first";
        return null;
    }

    // Control.IsWalking, or null when Control isn't there. Kept out of the iterators (no pointers across a yield).
    private static unsafe bool? ReadWalking()
    {
        var control = Control.Instance();
        return control == null ? null : control->IsWalking;
    }

    private static unsafe bool WriteWalking(bool walk)
    {
        var control = Control.Instance();
        if (control == null)
            return false;
        control->IsWalking = walk;
        return true;
    }

    private static unsafe nint ControlAddress() => (nint)Control.Instance();

    private static string Hex(ulong value) => $"0x{value:X}";
}
