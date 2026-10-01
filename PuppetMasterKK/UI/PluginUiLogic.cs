using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PuppetMasterKK;

internal enum ReactionUiStatus
{
    Disabled,
    InvalidTrigger,
    NoChannels,
    Unsafe,
    NoProtections,
    Ready,
}

internal readonly record struct PreviewLine(string Command, bool Allowed, string Reason);

internal enum PreviewStatus { Empty, NoMatch, Matched, Error }

/// <summary>What a message would run for one trigger ("Try it" and "Test all triggers").</summary>
internal sealed record ReactionPreview(PreviewStatus Status, string Matched, string? Error, IReadOnlyList<PreviewLine> Lines)
{
    public static readonly ReactionPreview Empty = new(PreviewStatus.Empty, string.Empty, null, []);
    public static readonly ReactionPreview NoMatch = new(PreviewStatus.NoMatch, string.Empty, null, []);

    /// <summary>Which choice it picked ("Choice 2 of 3 (next in turn)"), or null without choices.</summary>
    public string? Choice { get; init; }

    /// <summary>The final action's lines.</summary>
    public IReadOnlyList<PreviewLine> FinalLines { get; init; } = [];
}

/// <summary>The permission check for one line of a trigger's commands (Service.IsCommandAllowed in the plugin).</summary>
internal delegate bool CommandCheck(Reaction reaction, string command, bool templateWait, out string reason);

internal enum TriggerTestOutcome { Fires, Off, NotOnChannel, SenderNotAllowed }

internal sealed record TriggerTestResult(int Index, TriggerTestOutcome Outcome, ReactionPreview Preview);

internal static class PluginUiLogic
{
    public static readonly string[] TestSenderLabels = ["A stranger", "A friend", "Free Company member", "Party member"];
    public static readonly string[] NotificationSettingLabels =
        ["Default", "Show", "Hide"];

    // Channels where anyone around you (or any stranger) can talk: a reaction that listens to one of these for Anyone
    // can be triggered by people you don't know. Numbers are XivChatType values.
    private static readonly HashSet<int> PublicChannelIds =
    [
        10, // Say
        11, // Shout
        13, // TellIncoming
        14, // Party
        15, // Alliance
        27, // NoviceNetwork
        28, // CustomEmote
        29, // StandardEmote
        30, // Yell
        32, // CrossParty
        37, 101, 102, 103, 104, 105, 106, 107, // Cross-world linkshells (often large, open communities)
    ];

    public static bool IsPublicChannel(int chatTypeId) => PublicChannelIds.Contains(chatTypeId);

    public static bool ListensToStrangers(Reaction reaction)
    {
        if (reaction.Senders?.Anyone != true || reaction.EnabledChannels == null)
            return false;
        foreach (var channel in reaction.EnabledChannels)
        {
            if (IsPublicChannel(channel))
                return true;
        }
        return false;
    }

    public static readonly (ReactionExecutionPolicy Policy, string Label)[] ExecutionPolicyOptions =
    [
        (ReactionExecutionPolicy.IgnoreWhileRunning, "Ignore"),
        (ReactionExecutionPolicy.QueueEveryTrigger, "Queue every trigger"),
        (ReactionExecutionPolicy.QueueLatestTrigger, "Queue latest trigger"),
        (ReactionExecutionPolicy.RestartImmediately, "Restart immediately"),
    ];

    public static readonly string[] ExecutionPolicyLabels =
        ExecutionPolicyOptions.Select(option => option.Label).ToArray();

    public static int EnsureReactionSelection(Configuration configuration, int preferredIndex)
    {
        if (configuration.Reactions.Count == 0)
        {
            configuration.Reactions.Add(Reaction.FromDefaults(configuration));
        }

        return Math.Clamp(preferredIndex, 0, configuration.Reactions.Count - 1);
    }

    public static bool MatchesSearch(Reaction reaction, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;
        var trigger = reaction.UseRegex ? reaction.CustomPhrase : reaction.TriggerPhrase;
        return (reaction.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (trigger?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    public static ReactionUiStatus GetStatus(Reaction reaction)
    {
        if (!reaction.Enabled)
            return ReactionUiStatus.Disabled;
        var pattern = ReactionCommandMatcher.SelectPattern(reaction);
        var trigger = reaction.UseRegex ? reaction.CustomPhrase : reaction.TriggerPhrase;
        if (string.IsNullOrWhiteSpace(trigger) || pattern == null)
            return ReactionUiStatus.InvalidTrigger;
        if (reaction.EnabledChannels == null || reaction.EnabledChannels.Count == 0)
            return ReactionUiStatus.NoChannels;
        if (reaction.NoProtections)
            return ReactionUiStatus.NoProtections;
        if (ListensToStrangers(reaction))
            return ReactionUiStatus.Unsafe;
        return ReactionUiStatus.Ready;
    }

    public static void EnsureRegexRestoreTrigger(Reaction reaction)
    {
        if (string.IsNullOrWhiteSpace(reaction.TriggerPhrase))
            reaction.TriggerPhrase = Reaction.DefaultTriggerPhrase;
    }

    public static int ClampCooldown(int seconds)
    {
        return Math.Clamp(seconds, 0, Reaction.MaxCooldownSeconds);
    }

    public static bool IgnoresCooldown(ReactionExecutionPolicy policy)
    {
        return policy == ReactionExecutionPolicy.RestartImmediately;
    }

    public static bool RestartsActiveRun(ReactionExecutionPolicy policy)
    {
        return policy == ReactionExecutionPolicy.RestartImmediately;
    }

    public static string GetExecutionPolicyDescription(ReactionExecutionPolicy policy)
    {
        return policy switch
        {
            ReactionExecutionPolicy.QueueEveryTrigger => "Runs each new request after the current one finishes (up to 16 can wait).",
            ReactionExecutionPolicy.QueueLatestTrigger => "Keeps only the newest request and runs it when the current one finishes.",
            ReactionExecutionPolicy.RestartImmediately => "Stops the current run and starts over with the new request.",
            _ => "Ignores the new message while this trigger is busy.",
        };
    }

    public static string GetCooldownDescription(ReactionExecutionPolicy policy)
    {
        return IgnoresCooldown(policy)
            ? "Cooldown doesn't apply to Restart immediately."
            : "The minimum time between runs. A new run also waits for the current one to finish.";
    }

    public static void SetReactionEnabled(Reaction reaction, bool enabled, Action<Reaction>? cancel = null)
    {
        reaction.Enabled = enabled;
        if (!enabled)
            cancel?.Invoke(reaction);
    }

    public static bool TryDeleteReaction(
        List<Reaction> reactions,
        int index,
        out int nextIndex,
        Action<Reaction>? cancel = null)
    {
        nextIndex = -1;
        if (reactions.Count <= 1 || index < 0 || index >= reactions.Count)
            return false;
        cancel?.Invoke(reactions[index]);
        reactions.RemoveAt(index);
        nextIndex = Math.Clamp(index, 0, reactions.Count - 1);
        return true;
    }

    public static string NormalizeCommand(string input)
    {
        input = input.Trim();
        if (input.Length == 0)
            return string.Empty;
        if (!input.StartsWith('/'))
            input = $"/{input}";
        input = input.Replace('[', '<').Replace(']', '>');
        var space = input.IndexOf(' ');
        return (space == -1 ? input : input[..space]).ToLowerInvariant();
    }

    public static bool ContainsCommand(IEnumerable<string> commands, string command)
    {
        return commands.Any(item => item.Equals(command, StringComparison.OrdinalIgnoreCase));
    }

    public static bool AddCommandRule(List<string> commands, List<string> oppositeCommands, string input)
    {
        var command = NormalizeCommand(input);
        if (command.Length == 0)
            return false;
        oppositeCommands.RemoveAll(item => item.Equals(command, StringComparison.OrdinalIgnoreCase));
        if (ContainsCommand(commands, command))
            return false;
        commands.Add(command);
        return true;
    }

    /// <summary>
    /// Adds a whole command line, arguments kept ("/echo stopped"), with a leading "/" added if missing. False when
    /// it's blank, more than one line, or already listed.
    /// </summary>
    public static bool AddCommandLine(List<string> commands, string input)
    {
        var line = CommandLine(input);
        if (line == null || ContainsCommand(commands, line))
            return false;
        commands.Add(line);
        return true;
    }

    // One command line with a leading "/", or null when it's blank or more than one line.
    private static string? CommandLine(string input)
    {
        var line = input.Trim();
        if (line.Length == 0 || line.IndexOfAny(['\r', '\n']) >= 0)
            return null;
        if (!line.StartsWith('/'))
            line = "/" + line;
        return line.Length < 2 ? null : line;
    }

    /// <summary>
    /// Adds a player, "Name@World" or just "Name" for any world, unless it's already listed. False when nothing was
    /// added (blank, or "Name@" / "@World", which match nobody).
    /// </summary>
    public static bool AddPlayerName(List<string> names, string input)
    {
        var text = input.Trim();
        var at = text.IndexOf('@');
        var name = (at < 0 ? text : text[..at]).Trim();
        var world = at < 0 ? string.Empty : text[(at + 1)..].Trim();
        if (name.Length == 0 || (at >= 0 && world.Length == 0))
            return false;
        var entry = world.Length == 0 ? name : $"{name}@{world}";
        if (names.Exists(existing => existing.Equals(entry, StringComparison.OrdinalIgnoreCase)))
            return false;
        names.Add(entry);
        return true;
    }

    /// <summary>The first of "|"-separated alternatives ("Ami|Kitty" gives "Ami"), or <paramref name="fallback"/> when there's none.</summary>
    public static string FirstAlternative(string? text, string fallback)
    {
        if (text == null)
            return fallback;
        foreach (var part in text.Split('|'))
        {
            if (!string.IsNullOrWhiteSpace(part))
                return part.Trim();
        }
        return fallback;
    }

    public static Reaction CloneReaction(Reaction source)
    {
        return new Reaction
        {
            Enabled = false,
            Name = $"{source.Name} Copy",
            TriggerPhrase = source.TriggerPhrase,
            AllowSit = source.AllowSit,
            MotionOnly = source.MotionOnly,
            CooldownSeconds = source.CooldownSeconds,
            ExecutionPolicy = source.ExecutionPolicy,
            ProgressNotifications = source.ProgressNotifications,
            SuppressedNotifications = source.SuppressedNotifications,
            AllowAllCommands = source.AllowAllCommands,
            UseRegex = source.UseRegex,
            CustomPhrase = source.CustomPhrase,
            ReplaceMatch = source.ReplaceMatch,
            TestInput = source.TestInput,
            EnabledChannels = new List<int>(source.EnabledChannels),
            CommandWhitelist = new List<string>(source.CommandWhitelist),
            CommandBlacklist = new List<string>(source.CommandBlacklist),
            Senders = (source.Senders ?? SenderFilter.AnyoneFilter()).Clone(),
            Protections = (source.Protections ?? new ProtectionSettings()).Clone(),
            // A copy never starts without protections: that takes its own two confirmations.
            NoProtections = false,
            PerSenderCooldownSeconds = source.PerSenderCooldownSeconds,
            OneWaitingPerSender = source.OneWaitingPerSender,
            ChoiceMode = source.ChoiceMode,
            Choices = (source.Choices ?? []).Select(choice => new ReactionChoice { Word = choice.Word, Commands = choice.Commands }).ToList(),
            FinalCommands = new List<string>(source.FinalCommands ?? []),
            FinalWhen = source.FinalWhen,
            // A copy takes turns with the original (and the rest of its group).
            TurnGroup = source.TurnGroup,
        };
    }

    public static bool ResolveNotificationSetting(ReactionNotificationSetting setting, bool globalDefault)
    {
        return setting switch
        {
            ReactionNotificationSetting.Enabled => true,
            ReactionNotificationSetting.Disabled => false,
            _ => globalDefault,
        };
    }

    public static Reaction CreateReactionFromLog(
        int chatTypeId,
        string triggerText,
        string channelName,
        Configuration configuration)
    {
        var reaction = Reaction.FromDefaults(configuration, $"Trigger from {channelName}");
        reaction.EnabledChannels.Clear();
        reaction.UseRegex = true;
        reaction.CustomPhrase = $"^{Regex.Escape(triggerText)}$";
        reaction.TestInput = triggerText;
        reaction.EnabledChannels.Add(chatTypeId);
        // System and custom channels have no player behind them, so only "Anyone" can ever match there.
        if (!IsPlayerChatChannel(chatTypeId))
            reaction.Senders = SenderFilter.AnyoneFilter();
        return reaction;
    }

    // Channels whose lines come from a player (so a sender filter can tell who). Numbers are XivChatType values.
    public static bool IsPlayerChatChannel(int chatTypeId)
    {
        // Say..CrossParty (10-32), PvPTeam (36), CrossLinkShell1 (37), CrossLinkShell2-8 (101-107).
        return chatTypeId is (>= 10 and <= 32) or 36 or 37 or (>= 101 and <= 107);
    }

    public static void SetChannel(List<int> selectedChannels, int chatTypeId, bool enabled)
    {
        if (enabled)
        {
            if (!selectedChannels.Contains(chatTypeId))
                selectedChannels.Add(chatTypeId);
        }
        else
        {
            selectedChannels.RemoveAll(id => id == chatTypeId);
        }
    }

    /// <summary>
    /// Matches <paramref name="message"/> the way live chat does and lists each command line with whether it may run.
    /// Nothing is sent.
    /// </summary>
    /// <param name="turn">In turn: the trigger's next position (ChatHandler.GetChoiceTurn).</param>
    /// <param name="random">Random choices: 0..n-1 (null: Random.Shared).</param>
    public static ReactionPreview BuildPreview(Reaction reaction, string message, Func<string, bool> isEmote, CommandCheck check,
        int turn = 0, Func<int, int>? random = null)
    {
        if (string.IsNullOrWhiteSpace(message))
            return ReactionPreview.Empty;
        var choices = ChoiceSet.From(reaction);
        var status = ReactionCommandMatcher.TryGenerateCommand(
            ReactionCommandMatcher.SelectPattern(reaction),
            ReactionCommandMatcher.SanitizeIncoming(message),
            ReactionCommandMatcher.SelectReplacement(reaction),
            choices,
            turn,
            random,
            out var command,
            out var matched,
            out var choice,
            out var error);
        if (status == ReactionMatchStatus.InvalidReplacement)
            return new(PreviewStatus.Error, string.Empty, error ?? "Couldn't build commands from this pattern.", []);
        if (status == ReactionMatchStatus.TimedOut)
            return new(PreviewStatus.Error, string.Empty, "The pattern took too long on this message.", []);
        if (status != ReactionMatchStatus.Success)
            return ReactionPreview.NoMatch;

        var template = choices != null && choice >= 0 ? choices.Commands[choice] : ReactionCommandMatcher.SelectReplacement(reaction);
        var finalLines = FinalCommandLines(reaction);
        return new(PreviewStatus.Matched, matched, null,
                   PreviewLines(reaction, command, ReactionCommandMatcher.TemplateWaitLines(template), isEmote, check))
        {
            Choice = choices != null && choice >= 0 ? ChoiceSelector.Describe(choices.Mode, choices.Words, choice) : null,
            FinalLines = PreviewLines(reaction, string.Join('\n', finalLines),
                                      ReactionCommandMatcher.TemplateWaitLines(string.Join('\n', finalLines)), isEmote, check),
        };
    }

    // Each command line as it would run, with whether it may.
    private static List<PreviewLine> PreviewLines(Reaction reaction, string command, bool[] waitLines, Func<string, bool> isEmote,
        CommandCheck check)
    {
        var lines = new List<PreviewLine>();
        var split = ReactionCommandMatcher.SplitLines(command);
        for (var lineIndex = 0; lineIndex < split.Length; lineIndex++)
        {
            var parsed = ReactionCommandMatcher.FormatCommand(split[lineIndex]);
            if (string.IsNullOrWhiteSpace(parsed.Main))
                continue;
            if (reaction.MotionOnly && isEmote(parsed.Main))
                parsed.Args = "motion";
            var allowed = check(reaction, parsed.Main, ReactionCommandMatcher.IsTemplateWait(waitLines, lineIndex), out var reason);
            lines.Add(new PreviewLine(parsed.ToString(), allowed, Capitalize(reason)));
        }
        return lines;
    }

    /// <summary>The final action's lines as they run: blank ones and ones with a line break skipped, at most five.</summary>
    public static string[] FinalCommandLines(Reaction reaction)
    {
        if (reaction.FinalCommands == null)
            return [];
        return reaction.FinalCommands
            .Where(line => !string.IsNullOrWhiteSpace(line) && line.IndexOfAny(['\r', '\n']) < 0)
            .Take(Reaction.MaxFinalCommands)
            .ToArray();
    }

    /// <summary>
    /// Adds a final-action line (see <see cref="AddCommandLine"/>), unless there are already
    /// <see cref="Reaction.MaxFinalCommands"/>. Duplicates are fine here: "/wave" twice is a real routine.
    /// </summary>
    public static bool AddFinalCommand(List<string> commands, string input)
    {
        var line = CommandLine(input);
        if (line == null || commands.Count >= Reaction.MaxFinalCommands)
            return false;
        commands.Add(line);
        return true;
    }

    /// <summary>A pretend sender for "Test all triggers": a kind from <see cref="TestSenderLabels"/>, plus an optional Name@World.</summary>
    internal static SenderInfo TestSender(int kind, string? nameAndWorld)
    {
        var text = nameAndWorld?.Trim() ?? string.Empty;
        var at = text.IndexOf('@');
        var name = (at < 0 ? text : text[..at]).Trim();
        var world = at < 0 ? string.Empty : text[(at + 1)..].Trim();
        return new SenderInfo(name, world, false, kind == 1, kind == 2, kind == 3);
    }

    /// <summary>
    /// Every trigger whose pattern matches <paramref name="message"/>, with what it would run and whether it would fire
    /// for this channel and sender. Triggers that don't match are left out. Nothing is sent.
    /// </summary>
    internal static List<TriggerTestResult> TestAllTriggers(IReadOnlyList<Reaction> reactions, string message, int channel,
        in SenderInfo sender, Func<string, bool> isEmote, CommandCheck check, Func<Reaction, int>? turnOf = null,
        Func<int, int>? random = null)
    {
        var results = new List<TriggerTestResult>();
        for (var index = 0; index < reactions.Count; index++)
        {
            var reaction = reactions[index];
            var preview = BuildPreview(reaction, message, isEmote, check, turnOf?.Invoke(reaction) ?? 0, random);
            if (preview.Status is PreviewStatus.Empty or PreviewStatus.NoMatch)
                continue;
            var outcome = !reaction.Enabled ? TriggerTestOutcome.Off
                : reaction.EnabledChannels?.Contains(channel) != true ? TriggerTestOutcome.NotOnChannel
                : reaction.Senders != null && !reaction.Senders.Allows(sender) ? TriggerTestOutcome.SenderNotAllowed
                : TriggerTestOutcome.Fires;
            results.Add(new TriggerTestResult(index, outcome, preview));
        }
        return results;
    }

    public static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    public static string? ValidateCustomChannelId(
        ChannelSetting channel,
        int channelId,
        IReadOnlyList<ChannelSetting> customChannels,
        Func<int, bool> isOfficial)
    {
        if (channelId < ushort.MinValue || channelId > ushort.MaxValue)
            return $"Channel ID must be between {ushort.MinValue} and {ushort.MaxValue}.";
        if (isOfficial(channelId))
            return "That ID is already a built-in channel.";
        if (customChannels.Any(candidate => !ReferenceEquals(candidate, channel) && candidate.ChatType == channelId))
            return "Another custom channel already uses this ID.";
        return null;
    }
}
