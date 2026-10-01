using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PuppetMasterKK;

internal enum FollowRequestKind
{
    None,
    Follow,
    // Come to the sender, wherever they are in the zone.
    Come,
    // Copy a player's emotes (Target empty: the sender).
    Mimic,
    Stop,
}

// "Ami follow me" (Target empty: the sender), "Ami follow Nova Ral'veth@Exodus", "Ami come", "Ami stop".
// A bare "Ami follow" names nobody, so it isn't a request.
internal readonly record struct FollowRequest(FollowRequestKind Kind, string Target)
{
    public static FollowRequest None { get; } = new(FollowRequestKind.None, string.Empty);
}

// A player as Follow mode sees one: a name and a home world (empty when unknown).
internal readonly record struct PlayerName(string Name, string World);

// The pure part of Follow mode (no game state), so it can be tested: reading a request out of a chat line, splitting
// "Name@World", and finding the requested player among the ones nearby.
internal static class FollowParser
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly string[] SelfWords = ["me", "myself"];

    // Compiled patterns for the current words, rebuilt only when the words change.
    private static string cachedKey = "\u0000";
    private static Regex? followPattern;
    private static Regex? comePattern;
    private static Regex? mimicPattern;
    private static Regex? stopPattern;

    public static FollowRequest Parse(string message, string callNames, string followWords, string stopWords, string comeWords = "",
                                      string mimicWords = "")
    {
        var key = callNames + "\u0001" + followWords + "\u0001" + stopWords + "\u0001" + comeWords + "\u0001" + mimicWords;
        if (key != cachedKey)
        {
            followPattern = BuildPattern(callNames, followWords, withTarget: true);
            comePattern = BuildPattern(callNames, comeWords, withTarget: false);
            mimicPattern = BuildPattern(callNames, mimicWords, withTarget: true);
            stopPattern = BuildPattern(callNames, stopWords, withTarget: false);
            cachedKey = key;
        }

        try
        {
            if (stopPattern?.IsMatch(message) == true)
                return new FollowRequest(FollowRequestKind.Stop, string.Empty);
            if (comePattern?.IsMatch(message) == true)
                return new FollowRequest(FollowRequestKind.Come, string.Empty);
            if (Named(mimicPattern, message) is { } mimicked)
                return new FollowRequest(FollowRequestKind.Mimic, mimicked);
            if (Named(followPattern, message) is { } followed)
                return new FollowRequest(FollowRequestKind.Follow, followed);
        }
        catch (RegexMatchTimeoutException)
        {
        }
        return FollowRequest.None;
    }

    // The player a "<call> <word> <player|me>" line names ("" for me), or null when it names nobody.
    private static string? Named(Regex? pattern, string message)
    {
        var match = pattern?.Match(message);
        if (match is not { Success: true })
            return null;
        var named = match.Groups["target"].Value.Trim().TrimEnd('.', '!', '?', '~', ',').Trim();
        return named.Length > 0 ? CleanTarget(named) : null;
    }

    // "<call> <word>" anywhere in the line, the call and the word as whole words, a little punctuation allowed between
    // ("Ami, follow"). For follow, everything after the word up to the end of the line is the target.
    internal static Regex? BuildPattern(string callNames, string words, bool withTarget)
    {
        var call = Alternatives(callNames);
        var word = Alternatives(words);
        if (call.Length == 0 || word.Length == 0)
            return null;
        var pattern = @"(?i)(?<![\p{L}\p{N}])(?:" + call + @")[\s,:;!.~-]*(?:" + word + @")(?![\p{L}\p{N}])";
        if (withTarget)
            pattern += @"(?:\s+(?<target>.+?))?[\s.!?~]*$";
        return new Regex(pattern, RegexOptions.CultureInvariant, MatchTimeout);
    }

    private static string Alternatives(string text)
    {
        var parts = new List<string>();
        foreach (var part in text.Split('|'))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                parts.Add(Regex.Escape(trimmed));
        }
        return string.Join("|", parts);
    }

    private static string CleanTarget(string target)
    {
        target = target.Trim().TrimEnd('.', '!', '?', '~', ',').Trim();
        foreach (var self in SelfWords)
        {
            if (target.Equals(self, StringComparison.OrdinalIgnoreCase))
                return string.Empty;
        }
        return target;
    }

    // "Nova Ral'veth@Exodus" -> ("Nova Ral'veth", "Exodus"); "Nova" -> ("Nova", "").
    public static PlayerName SplitName(string text)
    {
        var at = text.IndexOf('@');
        return at < 0
            ? new PlayerName(text.Trim(), string.Empty)
            : new PlayerName(text[..at].Trim(), text[(at + 1)..].Trim());
    }

    // Finds the requested player among those nearby: the full name (and world, when given) exactly, ignoring case;
    // or, for a single word, the one nearby player whose first name it is. -1 when there's no match or it's ambiguous.
    public static int FindNearby(PlayerName requested, IReadOnlyList<PlayerName> nearby)
    {
        if (requested.Name.Length == 0)
            return -1;
        for (var i = 0; i < nearby.Count; i++)
        {
            if (nearby[i].Name.Equals(requested.Name, StringComparison.OrdinalIgnoreCase) &&
                (requested.World.Length == 0 || nearby[i].World.Equals(requested.World, StringComparison.OrdinalIgnoreCase)))
                return i;
        }
        if (requested.Name.Contains(' ') || requested.World.Length > 0)
            return -1;

        var found = -1;
        for (var i = 0; i < nearby.Count; i++)
        {
            var space = nearby[i].Name.IndexOf(' ');
            var first = space < 0 ? nearby[i].Name : nearby[i].Name[..space];
            if (!first.Equals(requested.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (found >= 0)
                return -1; // two people share that first name: don't guess
            found = i;
        }
        return found;
    }

    // Whether Follow mode may follow this player: never someone on the never list, and when the only list has anyone
    // on it, only them. Entries are "Name@World" or "Name" (any world).
    public static bool MayFollow(PlayerName player, IReadOnlyList<string> onlyFollow, IReadOnlyList<string> neverFollow)
    {
        var who = new SenderInfo(player.Name, player.World, false, false, false, false);
        if (SenderFilter.MatchesNamed(neverFollow, who))
            return false;
        return onlyFollow.Count == 0 || SenderFilter.MatchesNamed(onlyFollow, who);
    }

    // The not-nearby reply: <target> becomes the name they asked for. Angle brackets in that name (someone else's
    // text) are dropped so it can't become a game placeholder.
    public static string FormatReply(string template, string target)
    {
        var safe = target.Replace("<", string.Empty).Replace(">", string.Empty)
                         .Replace("＜", string.Empty).Replace("＞", string.Empty);
        return template.Replace(FollowSettings.TargetPlaceholder, safe, StringComparison.OrdinalIgnoreCase).Trim();
    }
}
