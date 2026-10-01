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

    // The trigger phrase is plain text: "please.do" means a literal dot. "|" separates alternative phrases; each is
    // trimmed and empty ones are dropped (an empty alternative would match almost any message).
    public static string EscapeTriggerPhrase(string phrase)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (var part in phrase.Split('|'))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                parts.Add(Regex.Escape(trimmed));
        }
        return string.Join("|", parts);
    }

    // The phrase-mode pattern: the phrase, then "(command with arguments)" or one word. Empty when the phrase has no
    // usable text. The bracketed part never spans a line.
    public static string BuildPhrasePattern(string phrase)
    {
        var escaped = string.IsNullOrWhiteSpace(phrase) ? string.Empty : EscapeTriggerPhrase(phrase);
        return escaped.Length == 0 ? string.Empty : @"(?i)\b(?:" + escaped + @")\s+(?:\(([^)\r\n]*)\)|(\w+))";
    }

    public const string PhraseReplacement = "/$1$2";

    // Incoming chat is someone else's text: control characters (line breaks that would split it into several
    // commands) become spaces, and angle brackets become look-alikes so "<pos>" or "<flag>" in it can never be
    // expanded by the game. "[t]"-style placeholders still work: they're converted later, from a safe list.
    public static string SanitizeIncoming(string message)
    {
        var chars = message.ToCharArray();
        var changed = false;
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (char.IsControl(c))
            {
                chars[i] = ' ';
                changed = true;
            }
            else if (c == '<')
            {
                chars[i] = '\uFF1C';
                changed = true;
            }
            else if (c == '>')
            {
                chars[i] = '\uFF1E';
                changed = true;
            }
        }
        return changed ? new string(chars) : message;
    }

    // Splits one generated command line into its name ("/ac", lower-cased) and arguments. The name ends at the first
    // whitespace of any kind (a full-width space must not hide the name). "[t]"-style placeholders in the arguments
    // become "<t>" (safe ones only).
    public static ParsedTextCommand FormatCommand(string command)
    {
        ParsedTextCommand textCommand = new();
        command = command.Trim();
        if (command.Length == 0)
            return textCommand;
        if (!command.StartsWith('/'))
        {
            textCommand.Main = command;
            return textCommand;
        }

        var end = 0;
        while (end < command.Length && !char.IsWhiteSpace(command[end]))
            end++;
        textCommand.Main = command[..end].ToLowerInvariant();
        textCommand.Args = CommandPolicy.ConvertPlaceholders(command[end..].Trim());
        return textCommand;
    }

    // "/wait" from the reaction's own commands is a pause; from a sender's text (the phrase mode's "/$1$2") it has to
    // be allowed like any command, or a stranger could keep a reaction busy for minutes.
    public static bool TemplateHasWait(string replacement)
        => replacement.Contains(CommandPolicy.WaitCommand, StringComparison.OrdinalIgnoreCase);
}

public struct ParsedTextCommand
{
    public ParsedTextCommand() { }
    public string Main = string.Empty;
    public string Args = string.Empty;

    public override readonly string ToString()
    {
        return (Main + " " + Args).Trim();
    }
}
