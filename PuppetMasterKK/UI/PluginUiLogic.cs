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

internal static class PluginUiLogic
{
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

    public static void SetRegexMode(Reaction reaction, bool useRegex)
    {
        reaction.UseRegex = useRegex;
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
        var line = input.Trim();
        if (line.Length == 0 || line.IndexOfAny(['\r', '\n']) >= 0)
            return false;
        if (!line.StartsWith('/'))
            line = "/" + line;
        if (line.Length < 2 || ContainsCommand(commands, line))
            return false;
        commands.Add(line);
        return true;
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
