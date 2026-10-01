using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PuppetMasterKK;

internal enum CommandKind
{
    Unknown,
    Emote,
    Game,
    Chat,
    Plugin,
    // Never runs, whatever the reaction allows (logout, shutdown, PuppetMasterKK's and Dalamud's own commands).
    Blocked,
}

// Every form the game accepts for a text command (command, short form, aliases) mapped to one canonical name, plus
// what kind of command it is. Built once from the TextCommand and Emote sheets; plugin commands are passed in at
// check time because plugins come and go.
internal sealed class CommandCatalog
{
    // Commands that post text other players can read. "Any command except those blocked" never covers these: a stranger
    // could otherwise make you say anything in shout, tell or FC chat.
    private static readonly string[] ChatCommandForms =
    [
        "/say", "/s", "/yell", "/y", "/shout", "/sh", "/tell", "/t", "/reply", "/r",
        "/party", "/p", "/alliance", "/a", "/freecompany", "/fc", "/novice", "/beginner", "/n", "/b",
        "/emote", "/em", "/pvpteam", "/pt",
        "/linkshell1", "/linkshell2", "/linkshell3", "/linkshell4", "/linkshell5", "/linkshell6", "/linkshell7", "/linkshell8",
        "/l1", "/l2", "/l3", "/l4", "/l5", "/l6", "/l7", "/l8",
        "/cwlinkshell1", "/cwlinkshell2", "/cwlinkshell3", "/cwlinkshell4", "/cwlinkshell5", "/cwlinkshell6", "/cwlinkshell7", "/cwlinkshell8",
        "/cwl1", "/cwl2", "/cwl3", "/cwl4", "/cwl5", "/cwl6", "/cwl7", "/cwl8",
    ];

    // English names of the commands that never run. The catalog maps them to the client's own names (the game
    // knows each command by its English name as well), so the block holds on every client language.
    private static readonly string[] AlwaysBlockedForms = ["/logout", "/shutdown", "/puppetmaster", "/puppetmasterkk", "/pmkk"];

    private readonly Dictionary<string, string> canonical = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> emotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> chat = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> blocked = new(StringComparer.OrdinalIgnoreCase);

    public static CommandCatalog Empty { get; } = new([], []);

    // gameCommands: each entry is every non-empty form of one command (the client's names and the English ones), the
    // canonical form first.
    public CommandCatalog(IEnumerable<string[]> gameCommands, IEnumerable<string[]> emoteCommands)
    {
        foreach (var forms in gameCommands)
            AddForms(forms);
        foreach (var forms in emoteCommands)
        {
            AddForms(forms);
            if (forms.Length > 0)
                emotes.Add(Canonicalize(forms[0]));
        }
        foreach (var form in ChatCommandForms)
            chat.Add(Canonicalize(form));
        foreach (var form in AlwaysBlockedForms)
            blocked.Add(Canonicalize(form));
    }

    // canonicalCommand must come from Canonicalize.
    public bool IsAlwaysBlocked(string canonicalCommand)
    {
        return blocked.Contains(canonicalCommand) || CommandPolicy.IsAlwaysBlocked(canonicalCommand);
    }

    public int EmoteCount => emotes.Count;

    private void AddForms(string[] forms)
    {
        if (forms.Length == 0)
            return;
        var main = Normalize(forms[0]);
        if (main.Length == 0)
            return;
        foreach (var form in forms)
        {
            var key = Normalize(form);
            if (key.Length > 0)
                canonical.TryAdd(key, main);
        }
    }

    public static string Normalize(string command)
    {
        return command.Trim().ToLowerInvariant();
    }

    public string Canonicalize(string command)
    {
        var key = Normalize(command);
        return canonical.TryGetValue(key, out var main) ? main : key;
    }

    public bool IsKnown(string command)
    {
        return canonical.ContainsKey(Normalize(command));
    }

    public bool IsEmote(string command)
    {
        return emotes.Contains(Canonicalize(command));
    }

    public CommandKind Classify(string command, Func<string, bool>? isPluginCommand = null)
    {
        var main = Canonicalize(command);
        if (IsAlwaysBlocked(main) || CommandPolicy.IsAlwaysBlocked(Normalize(command)))
            return CommandKind.Blocked;
        if (emotes.Contains(main))
            return CommandKind.Emote;
        if (chat.Contains(main))
            return CommandKind.Chat;
        if (canonical.ContainsKey(main))
            return CommandKind.Game;
        if (isPluginCommand != null && isPluginCommand(Normalize(command)))
            return CommandKind.Plugin;
        return CommandKind.Unknown;
    }

    public HashSet<string> CanonicalSet(IEnumerable<string> commands)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands)
        {
            if (!string.IsNullOrWhiteSpace(command))
                set.Add(Canonicalize(command));
        }
        return set;
    }
}

internal static class CommandPolicy
{
    public const string WaitCommand = "/wait";

    // Blocked even when a reaction allows them: they log you out, close the game, or reconfigure PuppetMasterKK or
    // Dalamud itself (a stranger could otherwise turn every disabled reaction back on).
    public static bool IsAlwaysBlocked(string canonicalCommand)
    {
        return canonicalCommand is "/logout" or "/shutdown" or "/puppetmaster" or "/puppetmasterkk" or "/pmkk" ||
               canonicalCommand.StartsWith("/xl", StringComparison.OrdinalIgnoreCase);
    }

    // canonicalCommand, whitelist and blacklist must all be canonicalized through the same catalog.
    public static bool IsAllowed(
        string canonicalCommand,
        CommandKind kind,
        IReadOnlySet<string> whitelist,
        IReadOnlySet<string> blacklist,
        bool allowAllGameCommands,
        out string reason)
    {
        if (kind == CommandKind.Blocked || IsAlwaysBlocked(canonicalCommand))
        {
            reason = "always blocked";
            return false;
        }
        if (blacklist.Contains(canonicalCommand))
        {
            reason = "blocked by this reaction";
            return false;
        }
        if (whitelist.Contains(canonicalCommand))
        {
            reason = "allowed by this reaction";
            return true;
        }
        if (kind == CommandKind.Emote)
        {
            reason = "emotes are allowed";
            return true;
        }
        if (allowAllGameCommands && kind == CommandKind.Game)
        {
            reason = "game commands are allowed";
            return true;
        }
        reason = kind switch
        {
            CommandKind.Chat => "chat commands must be allowed one by one",
            CommandKind.Plugin => "plugin commands must be allowed one by one",
            CommandKind.Unknown => "not a known command",
            _ => "not allowed by this reaction",
        };
        return false;
    }

    private static readonly HashSet<string> SafePlaceholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "t", "tt", "me", "mo", "f", "1", "2", "3", "4", "5", "6", "7", "8",
    };

    // "[t]" -> "<t>" for target-style placeholders only. Others ("[pos]", "[flag]", "[se.1]") stay literal, so a chat
    // message can't make you post your location.
    public static string ConvertPlaceholders(string args)
    {
        if (args.IndexOf('[') < 0)
            return args;
        var builder = new StringBuilder(args.Length);
        var index = 0;
        while (index < args.Length)
        {
            var open = args.IndexOf('[', index);
            if (open < 0)
            {
                builder.Append(args, index, args.Length - index);
                break;
            }
            var close = args.IndexOf(']', open + 1);
            if (close < 0)
            {
                builder.Append(args, index, args.Length - index);
                break;
            }
            builder.Append(args, index, open - index);
            var name = args.Substring(open + 1, close - open - 1);
            if (SafePlaceholders.Contains(name))
                builder.Append('<').Append(name).Append('>');
            else
                builder.Append(args, open, close - open + 1);
            index = close + 1;
        }
        return builder.ToString();
    }
}

// One limit for everything PuppetMasterKK sends: a chat spammer's message rate can't become your command rate.
internal sealed class CommandRateLimiter(int burst, TimeSpan interval)
{
    private readonly object sync = new();
    private readonly long intervalTicks = Math.Max(1, (long)(interval.TotalSeconds * Stopwatch.Frequency));
    private long nextFreeTimestamp;

    public static CommandRateLimiter Shared { get; } = new(3, TimeSpan.FromSeconds(1));

    // Reserves one send and returns how long to wait before it may go out.
    public TimeSpan Reserve(long nowTimestamp)
    {
        lock (sync)
        {
            // The bucket holds at most `burst` sends of credit.
            var earliest = nowTimestamp - (burst - 1) * intervalTicks;
            if (nextFreeTimestamp < earliest)
                nextFreeTimestamp = earliest;
            var waitTicks = Math.Max(0, nextFreeTimestamp - nowTimestamp);
            nextFreeTimestamp += intervalTicks;
            return TimeSpan.FromSeconds(waitTicks / (double)Stopwatch.Frequency);
        }
    }

    // Takes a send only if one is free right now (for things that should be skipped rather than delayed).
    public bool TryAcquire(long nowTimestamp)
    {
        lock (sync)
        {
            var earliest = nowTimestamp - (burst - 1) * intervalTicks;
            if (nextFreeTimestamp < earliest)
                nextFreeTimestamp = earliest;
            if (nextFreeTimestamp > nowTimestamp)
                return false;
            nextFreeTimestamp += intervalTicks;
            return true;
        }
    }

    public void Reset()
    {
        lock (sync)
            nextFreeTimestamp = 0;
    }
}
