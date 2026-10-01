using System;
using System.Text.RegularExpressions;

namespace PuppetMasterKK;

internal enum ReactionMatchStatus
{
    NoMatch,
    Success,
    TimedOut,
    InvalidReplacement,
}

internal static class ReactionCommandMatcher
{
    public static Regex? SelectPattern(Reaction reaction)
    {
        return reaction.UseRegex ? reaction.CustomRx : reaction.Rx;
    }

    public static ReactionMatchStatus TryGenerateCommand(
        Regex? pattern,
        string message,
        string replacement,
        out string command,
        out string? error)
    {
        return TryGenerateCommand(pattern, message, replacement, out command, out _, out error);
    }

    // The one matching path: live runs and the editor preview both go through here, so the preview can't disagree
    // with what actually runs.
    public static ReactionMatchStatus TryGenerateCommand(
        Regex? pattern,
        string message,
        string replacement,
        out string command,
        out string matchedText,
        out string? error)
    {
        command = string.Empty;
        matchedText = string.Empty;
        error = null;
        if (pattern == null)
            return ReactionMatchStatus.NoMatch;
        try
        {
            var match = pattern.Match(message);
            if (!match.Success)
                return ReactionMatchStatus.NoMatch;
            matchedText = match.Value;
            command = match.Result(replacement);
            return ReactionMatchStatus.Success;
        }
        catch (RegexMatchTimeoutException)
        {
            return ReactionMatchStatus.TimedOut;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return ReactionMatchStatus.InvalidReplacement;
        }
    }

    // "/wait <seconds>": invariant culture (so "1.5" means the same on a German client), finite only, 0-60 s.
    public static bool TryParseWaitSeconds(string args, out double seconds)
    {
        seconds = 0;
        if (!double.TryParse(args, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ||
            !double.IsFinite(parsed))
            return false;
        seconds = Math.Clamp(parsed, 0.0, 60.0);
        return true;
    }

    // The trigger phrase is plain text: "please.do" means a literal dot. "|" still separates alternative phrases.
    public static string EscapeTriggerPhrase(string phrase)
    {
        var parts = phrase.Split('|');
        for (var i = 0; i < parts.Length; i++)
            parts[i] = Regex.Escape(parts[i]);
        return string.Join("|", parts);
    }
}
