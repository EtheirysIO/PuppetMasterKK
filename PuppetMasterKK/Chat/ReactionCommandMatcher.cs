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

    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Builds the trigger's pattern (Rx or CustomRx, the other cleared) with the match timeout. An invalid or too-long
    /// pattern leaves both null, so a stale compiled one can't be reused.
    /// </summary>
    public static void CompilePatterns(Reaction reaction, int maxLength)
    {
        reaction.Rx = null;
        reaction.CustomRx = null;
        try
        {
            // Nothing but the text box enforces the length limit, and a hand-edited config can hold anything.
            if (reaction.UseRegex)
            {
                if (!string.IsNullOrWhiteSpace(reaction.CustomPhrase) && reaction.CustomPhrase.Length <= maxLength)
                    reaction.CustomRx = new Regex(reaction.CustomPhrase, RegexOptions.None, MatchTimeout);
            }
            else
            {
                var pattern = BuildPhrasePattern(reaction.TriggerPhrase);
                if (!string.IsNullOrWhiteSpace(pattern) && reaction.TriggerPhrase.Length <= maxLength)
                    reaction.Rx = new Regex(pattern, RegexOptions.None, MatchTimeout);
            }
        }
        catch (ArgumentException)
        {
            // Invalid patterns stay null.
        }
    }

    // The command template that goes with SelectPattern.
    public static string SelectReplacement(Reaction reaction)
    {
        return reaction.UseRegex ? reaction.ReplaceMatch : PhraseReplacement;
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
        return TryGenerateCommand(pattern, message, replacement, null, 0, null, out command, out matchedText, out _, out error);
    }

    /// <summary>
    /// As above, with Choices: when <paramref name="choices"/> is set, the chosen block (<paramref name="choice"/>) is
    /// the replacement, and By word with an unknown word is no match. <paramref name="turn"/> is In turn's position;
    /// <paramref name="random"/> (0..n-1) defaults to Random.Shared.
    /// </summary>
    public static ReactionMatchStatus TryGenerateCommand(
        Regex? pattern,
        string message,
        string replacement,
        ChoiceSet? choices,
        int turn,
        Func<int, int>? random,
        out string command,
        out string matchedText,
        out int choice,
        out string? error)
    {
        command = string.Empty;
        matchedText = string.Empty;
        choice = -1;
        error = null;
        if (pattern == null)
            return ReactionMatchStatus.NoMatch;
        try
        {
            var match = pattern.Match(message);
            if (!match.Success)
                return ReactionMatchStatus.NoMatch;
            if (choices != null)
            {
                choice = ChoiceSelector.Select(choices.Mode, choices.Words, match.Groups[1].Value, turn, random ?? Random.Shared.Next);
                if (choice < 0)
                    return ReactionMatchStatus.NoMatch;
                replacement = choices.Commands[choice];
            }
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
    // Sender text can't add lines (SanitizeIncoming turns line breaks into spaces), so line i of the generated
    // commands always comes from line i of the template: only a template line that is itself "/wait" is the trigger's
    // own pause. A "/wait 60" built from a capture has to be allowed like any other command.
    public static bool[] TemplateWaitLines(string replacement)
    {
        var lines = SplitLines(replacement);
        var waits = new bool[lines.Length];
        for (var i = 0; i < lines.Length; i++)
            waits[i] = CommandCatalog.Normalize(FormatCommand(lines[i]).Main) == CommandPolicy.WaitCommand;
        return waits;
    }

    public static bool IsTemplateWait(bool[] waitLines, int lineIndex)
        => lineIndex >= 0 && lineIndex < waitLines.Length && waitLines[lineIndex];

    // The one way commands (and templates) are split into lines, so line numbers always agree.
    public static string[] SplitLines(string text) => text.Split(LineBreaks, StringSplitOptions.None);

    private static readonly string[] LineBreaks = ["\r\n", "\r", "\n"];
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
