using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PuppetMasterKK;

internal enum CommandKind
{
    Unknown,
    Emote,
    Game,
    Chat,
    // Game commands with real consequences (teleporting costs gil, leaving the party, changing gear...): never
    // covered by "Any game command", always allowed one by one.
    Sensitive,
    Plugin,
    // /follow: Follow mode's alone. Triggers never send it, not even without protections.
    FollowOnly,
    // Never runs while the trigger has protections (logout, shutdown, PuppetMasterKK's and Dalamud's own commands),
    // whatever its Allowed list says. Only "no protections" lets these through.
    Blocked,
}

// Every form the game accepts for a text command (command, short form, aliases) mapped to one canonical name, plus
// what kind of command it is. Built once from the TextCommand and Emote sheets; plugin commands are passed in at
// check time because plugins come and go.
internal sealed class CommandCatalog
{
    // /follow belongs to Follow mode (with its own sender, channel and target rules): triggers never send it.
    public const string FollowCommand = "/follow";

    private readonly Dictionary<string, string> canonical = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> emotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> chat = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> sensitive = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> blocked = new(StringComparer.OrdinalIgnoreCase);
    // Chat and risky commands: which protection group each belongs to (see ProtectionGroups).
    private readonly Dictionary<string, string> groups = new(StringComparer.OrdinalIgnoreCase);
    private string follow = FollowCommand;

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
        foreach (var group in ProtectionGroups.Chat)
        {
            foreach (var form in group.Forms)
            {
                var main = Canonicalize(form);
                chat.Add(main);
                groups.TryAdd(main, group.Key);
            }
        }
        foreach (var form in CommandPolicy.AlwaysBlockedForms)
            blocked.Add(Canonicalize(form));
        follow = Canonicalize(FollowCommand);
        foreach (var group in ProtectionGroups.Risky)
        {
            foreach (var form in group.Forms)
            {
                var main = Canonicalize(form);
                if (chat.Contains(main))
                    continue;
                sensitive.Add(main);
                groups.TryAdd(main, group.Key);
            }
        }
    }

    // The chat or risky protection group of a canonical command, or null.
    public string? GroupOf(string canonicalCommand)
    {
        return groups.TryGetValue(canonicalCommand, out var group) ? group : null;
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

    public bool IsEmote(string command)
    {
        return emotes.Contains(Canonicalize(command));
    }

    public CommandKind Classify(string command, Func<string, bool>? isPluginCommand = null)
    {
        var literal = Normalize(command);
        var main = Canonicalize(literal);
        // /follow and the always-blocked commands are also caught behind look-alikes (full-width letters, invisible
        // characters), in case the game reads those as the real command. Nothing is ever allowed that way: every
        // other kind comes from the line exactly as it will be sent.
        // An invisible character is either dropped or ends the name: whichever the game does, both are checked.
        var unmasked = Unmask(literal, cutAtInvisible: false);
        var cut = Unmask(literal, cutAtInvisible: true);
        if (IsFollow(literal) || IsFollow(unmasked) || IsFollow(cut))
            return CommandKind.FollowOnly;
        if (IsBlockedName(literal) || IsBlockedName(unmasked) || IsBlockedName(cut))
            return CommandKind.Blocked;
        if (emotes.Contains(main))
            return CommandKind.Emote;
        if (chat.Contains(main))
            return CommandKind.Chat;
        if (sensitive.Contains(main))
            return CommandKind.Sensitive;
        if (canonical.ContainsKey(main))
            return CommandKind.Game;
        if (isPluginCommand != null && isPluginCommand(literal))
            return CommandKind.Plugin;
        return CommandKind.Unknown;
    }

    private bool IsFollow(string name) => name == FollowCommand || Canonicalize(name) == follow;

    private bool IsBlockedName(string name) => CommandPolicy.IsAlwaysBlocked(name) || IsAlwaysBlocked(Canonicalize(name));

    // The command name as the game might read it: full-width forms folded to ASCII, invisible characters dropped,
    // cut at the first space.
    private static string Unmask(string normalized, bool cutAtInvisible)
    {
        var builder = new StringBuilder(normalized.Length);
        foreach (var original in normalized)
        {
            var c = original switch
            {
                >= '\uFF01' and <= '\uFF5E' => (char)(original - 0xFEE0),
                '\u3000' => ' ',
                _ => original,
            };
            if (char.IsWhiteSpace(c))
            {
                if (builder.Length > 0)
                    break;
                continue;
            }
            if (char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format)
            {
                if (cutAtInvisible && builder.Length > 0)
                    break;
                continue;
            }
            builder.Append(c);
        }
        return builder.ToString().ToLowerInvariant();
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

    // An emote line, with "motion" when the emote's chat message should be hidden.
    public static string EmoteLine(string command, bool motionOnly) => motionOnly ? $"{command} motion" : command;

    // English names of the commands that never run. The catalog also maps them to the client's own names (the game
    // knows each command by its English name as well), so the block holds on every client language.
    public static readonly string[] AlwaysBlockedForms = ["/logout", "/shutdown", "/puppetmaster", "/puppetmasterkk", "/pmkk"];

    // Blocked even when a reaction allows them: they log you out, close the game, or reconfigure PuppetMasterKK or
    // Dalamud itself (a stranger could otherwise turn every disabled reaction back on).
    public static bool IsAlwaysBlocked(string canonicalCommand)
    {
        return canonicalCommand == CommandCatalog.FollowCommand ||
               Array.Exists(AlwaysBlockedForms, form => form.Equals(canonicalCommand, StringComparison.OrdinalIgnoreCase)) ||
               canonicalCommand.StartsWith("/xl", StringComparison.OrdinalIgnoreCase);
    }

    // canonicalCommand, whitelist and blacklist must all be canonicalized through the same catalog.
    public static bool IsAllowed(
        string canonicalCommand,
        CommandKind kind,
        IReadOnlySet<string> whitelist,
        IReadOnlySet<string> blacklist,
        bool allowAllGameCommands,
        out string reason,
        bool noProtections = false,
        bool open = false)
    {
        if (kind == CommandKind.FollowOnly || canonicalCommand == CommandCatalog.FollowCommand)
        {
            reason = "only Follow mode can follow";
            return false;
        }
        // A trigger with no protections runs everything but /follow; its Allowed and Blocked lists are off too.
        if (noProtections)
        {
            reason = "this trigger has no protections";
            return true;
        }
        if (blacklist.Contains(canonicalCommand))
        {
            reason = "blocked by this trigger";
            return false;
        }
        if (kind == CommandKind.Blocked || IsAlwaysBlocked(canonicalCommand))
        {
            reason = "always blocked";
            return false;
        }
        if (whitelist.Contains(canonicalCommand))
        {
            reason = "allowed by this trigger";
            return true;
        }
        if (kind == CommandKind.Emote)
        {
            reason = "emotes are allowed";
            return true;
        }
        // Its protection is switched off for this trigger (chat channel, risky group or plugin).
        if (open && kind is CommandKind.Chat or CommandKind.Sensitive or CommandKind.Plugin)
        {
            reason = "its protection is off";
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
            CommandKind.Sensitive => "this command must be allowed one by one",
            CommandKind.Plugin => "plugin commands must be allowed one by one",
            CommandKind.Unknown => "not a known command",
            _ => "not allowed by this trigger",
        };
        return false;
    }

    // "/wait" (a pause, not a game command): a block entry always turns it off; otherwise it runs when the reaction's
    // own commands contain it, or when it's on the allow list (it came from a sender's text).
    public static bool IsWaitAllowed(
        CommandCatalog catalog,
        bool fromReactionCommands,
        IReadOnlySet<string> whitelist,
        IReadOnlySet<string> blacklist,
        out string reason,
        bool noProtections = false)
    {
        if (noProtections)
        {
            reason = "pause";
            return true;
        }
        var localized = catalog.Canonicalize(WaitCommand);
        if (blacklist.Contains(WaitCommand) || blacklist.Contains(localized))
        {
            reason = "blocked by this trigger";
            return false;
        }
        if (fromReactionCommands || whitelist.Contains(WaitCommand) || whitelist.Contains(localized))
        {
            reason = "pause";
            return true;
        }
        reason = "a pause in a sender's message must be allowed (add /wait)";
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

    // How long until a send would be free (zero when one is free now). Takes nothing: a waiting run that gets
    // cancelled must not leave a booked slot behind (that's how a spammed reaction used to block everything).
    public TimeSpan TimeUntilFree(long nowTimestamp)
    {
        lock (sync)
        {
            var earliest = nowTimestamp - (burst - 1) * intervalTicks;
            var next = Math.Max(nextFreeTimestamp, earliest);
            return TimeSpan.FromSeconds(Math.Max(0, next - nowTimestamp) / (double)Stopwatch.Frequency);
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
