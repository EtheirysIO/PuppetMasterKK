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
    // "First Last@World" is never longer than this.
    private const int MaxShownName = 40;

    // Patterns for the words in use. Follow and Mimic parse every line with their own words, so both sets stay cached.
    private sealed record Patterns(Regex? Follow, Regex? Come, Regex? Mimic, Regex? Stop);
    private const int MaxCachedPatterns = 8;
    private static readonly Dictionary<string, Patterns> Cache = new(StringComparer.Ordinal);

    public static FollowRequest Parse(string message, string callNames, string followWords, string stopWords, string comeWords = "",
                                      string mimicWords = "")
    {
        var patterns = PatternsFor(callNames, followWords, stopWords, comeWords, mimicWords);
        try
        {
            if (patterns.Stop?.IsMatch(message) == true)
                return new FollowRequest(FollowRequestKind.Stop, string.Empty);
            if (patterns.Come?.IsMatch(message) == true)
                return new FollowRequest(FollowRequestKind.Come, string.Empty);
            if (Named(patterns.Mimic, message) is { } mimicked)
                return new FollowRequest(FollowRequestKind.Mimic, mimicked);
            if (Named(patterns.Follow, message) is { } followed)
                return new FollowRequest(FollowRequestKind.Follow, followed);
        }
        catch (RegexMatchTimeoutException)
        {
        }
        return FollowRequest.None;
    }

    private static Patterns PatternsFor(string callNames, string followWords, string stopWords, string comeWords, string mimicWords)
    {
        var key = callNames + "\u0001" + followWords + "\u0001" + stopWords + "\u0001" + comeWords + "\u0001" + mimicWords;
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached))
                return cached;
            if (Cache.Count >= MaxCachedPatterns)
                Cache.Clear();
            var built = new Patterns(BuildPattern(callNames, followWords, withTarget: true),
                                     BuildPattern(callNames, comeWords, withTarget: false),
                                     BuildPattern(callNames, mimicWords, withTarget: true),
                                     BuildPattern(callNames, stopWords, withTarget: false));
            Cache[key] = built;
            return built;
        }
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
    public static int FindNearby(PlayerName requested, IReadOnlyList<PlayerName> nearby) => FindNearby(requested, nearby, out _);

    // As above; ambiguous is true when -1 is because several players match.
    public static int FindNearby(PlayerName requested, IReadOnlyList<PlayerName> nearby, out bool ambiguous)
    {
        ambiguous = false;
        if (requested.Name.Length == 0)
            return -1;
        var exact = -1;
        for (var i = 0; i < nearby.Count; i++)
        {
            if (!nearby[i].Name.Equals(requested.Name, StringComparison.OrdinalIgnoreCase) ||
                (requested.World.Length > 0 && !nearby[i].World.Equals(requested.World, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (exact >= 0)
            {
                ambiguous = true; // the same name on two worlds: don't guess
                return -1;
            }
            exact = i;
        }
        if (exact >= 0 || requested.Name.Contains(' ') || requested.World.Length > 0)
            return exact;

        var found = -1;
        for (var i = 0; i < nearby.Count; i++)
        {
            var space = nearby[i].Name.IndexOf(' ');
            var first = space < 0 ? nearby[i].Name : nearby[i].Name[..space];
            if (!first.Equals(requested.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (found >= 0)
            {
                ambiguous = true; // two people share that first name: don't guess
                return -1;
            }
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
    // text) are dropped so it can't become a game placeholder, and it's cut to a name's length so a long one can't
    // push the tell over the game's limit.
    public static string FormatReply(string template, string target)
    {
        var safe = target.Replace("<", string.Empty).Replace(">", string.Empty)
                         .Replace("＜", string.Empty).Replace("＞", string.Empty).Trim();
        if (safe.Length > MaxShownName)
            safe = safe[..MaxShownName].TrimEnd();
        return template.Replace(FollowSettings.TargetPlaceholder, safe, StringComparison.OrdinalIgnoreCase).Trim();
    }
}

// "Not again before then", per key (a player's "Name@World"): emote replies, not-nearby tells. Framework thread only.
// Expired entries are dropped once there are many, so a crowd can't grow it without end.
internal sealed class Cooldowns
{
    private const int PruneAt = 128;
    private readonly Dictionary<string, long> until = new(StringComparer.OrdinalIgnoreCase);

    public bool IsWaiting(string key, long now) => until.TryGetValue(key, out var end) && now < end;

    public void Start(string key, long now, long duration)
    {
        until[key] = now + duration;
        if (until.Count < PruneAt)
            return;
        var expired = new List<string>();
        foreach (var (entry, end) in until)
        {
            if (end <= now)
                expired.Add(entry);
        }
        foreach (var entry in expired)
            until.Remove(entry);
    }

    public void Clear() => until.Clear();
}
