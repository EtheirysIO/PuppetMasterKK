using System;
using System.Collections.Generic;
using System.Linq;

namespace PuppetMasterKK;

/// <summary>
/// Turn groups: triggers with the same <see cref="Reaction.TurnGroup"/> take turns, so they never run at the same time.
/// Names are trimmed and case doesn't matter; an empty name means the trigger runs on its own.
/// </summary>
internal static class TurnGroups
{
    public const int MaxNameLength = 40;

    /// <summary>A saved or typed name as it's stored: trimmed, one line, at most <see cref="MaxNameLength"/>.</summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;
        var oneLine = string.Join(' ', name.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return oneLine.Length > MaxNameLength ? oneLine[..MaxNameLength].TrimEnd() : oneLine;
    }

    /// <summary>The lane a trigger takes turns in: its group's name, or empty when it runs on its own.</summary>
    public static string LaneOf(Reaction reaction) => Normalize(reaction.TurnGroup);

    /// <summary>The groups in use, each once (the first spelling), in order.</summary>
    public static List<string> Names(IEnumerable<Reaction> reactions)
        => reactions.Select(LaneOf)
                    .Where(name => name.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToList();

    /// <summary>How many triggers are in <paramref name="group"/>.</summary>
    public static int MemberCount(IEnumerable<Reaction> reactions, string group)
    {
        var lane = Normalize(group);
        return lane.Length == 0 ? 0 : reactions.Count(reaction => LaneOf(reaction).Equals(lane, StringComparison.OrdinalIgnoreCase));
    }
}
