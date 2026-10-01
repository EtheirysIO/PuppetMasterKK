using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PuppetMaster;

internal enum ReactionUiStatus
{
    Disabled,
    InvalidTrigger,
    NoChannels,
    Unsafe,
    Ready,
}

internal static class PluginUiLogic
{
    public static readonly string[] NotificationSettingLabels =
        ["Default", "Show", "Hide"];

    public static readonly string[] ChannelCategoryLabels =
        ["Common", "CWLS", "Linkshells", "System", "Combat", "Activities", "Social", "GM", "Other", "Custom"];

    public static readonly string[] AdditionalChannelCategoryLabels =
        ["System", "Combat", "Activities", "Social", "GM", "Other"];

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

    public static string GetAdvancedChannelCategory(string channelName)
    {
        if (channelName.StartsWith("Gm", StringComparison.Ordinal))
            return "GM";

        if (channelName is "Damage" or "Miss" or "Action" or "Item" or "Healing" or
            "GainBuff" or "GainDebuff" or "LoseBuff" or "LoseDebuff")
            return "Combat";

        if (channelName is "GlamourNotifications" or "LootNotice" or "Progress" or "LootRoll" or
            "Crafting" or "Gathering" or "RetainerSale" or "Orchestrion" or "Sign" or "RandomNumber")
            return "Activities";

        if (channelName is "NPCDialogue" or "NPCDialogueAnnouncements" or "FreeCompanyAnnouncement" or
            "FreeCompanyLoginLogout" or "PeriodicRecruitmentNotification" or "PvpTeamAnnouncement" or
            "PvpTeamLoginLogout" or "MessageBook" or "CustomEmote" or "StandardEmote")
            return "Social";

        if (channelName is "Debug" or "Urgent" or "Notice" or "Alarm" or "Echo" or
            "SystemMessage" or "SystemError" or "GatheringSystemMessage" or "ErrorMessage" or
            "NoviceNetworkSystem")
            return "System";

        return "Other";
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
            configuration.Reactions.Add(Reaction.CreateDefault(
                commandWhitelist: configuration.DefaultCommandWhitelist,
                commandBlacklist: configuration.DefaultCommandBlacklist,
                allowAllCommands: configuration.DefaultAllowAllCommands,
                motionOnly: configuration.DefaultMotionOnly,
                enabledChannels: configuration.DefaultEnabledChannels));
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
        return Math.Clamp(seconds, 0, 86400);
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
            ReactionExecutionPolicy.QueueEveryTrigger => "Runs every request afterward (up to 16 waiting).",
            ReactionExecutionPolicy.QueueLatestTrigger => "Keeps only the newest request.",
            ReactionExecutionPolicy.RestartImmediately => "Stops the remaining steps and reacts again immediately.",
            _ => "Ignores the new message while this reaction is busy.",
        };
    }

    public static string GetCooldownDescription(ReactionExecutionPolicy policy)
    {
        return IgnoresCooldown(policy)
            ? "Cooldown does not apply with Restart immediately."
            : "Minimum time between starts. The current run must also finish first.";
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
        var reaction = Reaction.CreateDefault(
            $"Reaction from {channelName}",
            configuration.DefaultCommandWhitelist,
            configuration.DefaultCommandBlacklist,
            configuration.DefaultAllowAllCommands,
            configuration.DefaultMotionOnly,
            configuration.DefaultEnabledChannels);
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
            return "Official Dalamud channel IDs do not belong in Custom Channels.";
        if (customChannels.Any(candidate => !ReferenceEquals(candidate, channel) && candidate.ChatType == channelId))
            return "Another custom channel already uses this ID.";
        return null;
    }

    public static bool ShouldShowCustomChannel(
        ChannelSetting channel,
        Func<int, bool> isOfficial,
        Func<int, string?> getOfficialName)
    {
        if (!isOfficial(channel.ChatType))
            return true;
        return !channel.Name.Equals(getOfficialName(channel.ChatType), StringComparison.Ordinal);
    }
}
