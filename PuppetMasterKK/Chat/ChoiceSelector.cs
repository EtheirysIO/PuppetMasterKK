using System;
using System.Collections.Generic;
using System.Threading;

namespace PuppetMasterKK;

/// <summary>
/// "Choices" (regex triggers): which block of commands runs for a match. Random picks any block, In turn goes round
/// them, By word runs the block whose word is the pattern's first capture ($1); any other word is no match.
/// </summary>
internal static class ChoiceSelector
{
    public static readonly string[] ModeLabels = ["Off", "Random", "In turn", "By word"];

    public static bool IsActive(Reaction reaction)
        => reaction.UseRegex && reaction.ChoiceMode != ChoiceMode.Off && reaction.Choices is { Count: > 0 };

    /// <summary>The block to run, or -1 (By word, and $1 isn't one of the words). <paramref name="random"/> gives 0..n-1.</summary>
    public static int Select(ChoiceMode mode, IReadOnlyList<string> words, string firstGroup, int turn, Func<int, int> random)
    {
        var count = words.Count;
        if (count == 0)
            return -1;
        switch (mode)
        {
            case ChoiceMode.Random:
                return Math.Clamp(random(count), 0, count - 1);
            case ChoiceMode.InTurn:
                return (int)((uint)turn % (uint)count);
            case ChoiceMode.ByWord:
                var word = firstGroup.Trim();
                if (word.Length == 0)
                    return -1;
                for (var i = 0; i < count; i++)
                {
                    if (words[i].Trim().Equals(word, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
                return -1;
            default:
                return -1;
        }
    }

    /// <summary>"Choice 2 of 3 (picked at random)", for Try it and Test all triggers.</summary>
    public static string Describe(ChoiceMode mode, IReadOnlyList<string> words, int index)
    {
        var position = $"Choice {index + 1} of {words.Count}";
        return mode switch
        {
            ChoiceMode.Random => position + " (picked at random)",
            ChoiceMode.InTurn => position + " (next in turn)",
            _ => position + $" (word \"{words[index].Trim()}\")",
        };
    }
}

/// <summary>A trigger's choices, copied when a message comes in (later edits don't change a match in flight).</summary>
internal sealed record ChoiceSet(ChoiceMode Mode, string[] Words, string[] Commands)
{
    public static ChoiceSet? From(Reaction reaction)
    {
        if (!ChoiceSelector.IsActive(reaction))
            return null;
        var count = Math.Min(reaction.Choices.Count, Reaction.MaxChoices);
        var words = new string[count];
        var commands = new string[count];
        for (var i = 0; i < count; i++)
        {
            words[i] = reaction.Choices[i]?.Word ?? string.Empty;
            commands[i] = reaction.Choices[i]?.Commands ?? string.Empty;
        }
        return new ChoiceSet(reaction.ChoiceMode, words, commands);
    }
}

/// <summary>In turn: which choice is next. It moves on only when a request runs or waits, not when it's ignored.</summary>
internal sealed class ChoiceTurn
{
    private int next;

    public int Current => Volatile.Read(ref next);

    public void Advance() => Interlocked.Increment(ref next);
}
