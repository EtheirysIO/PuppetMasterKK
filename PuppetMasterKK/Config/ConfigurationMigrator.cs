using System;
using System.Collections.Generic;

namespace PuppetMasterKK;

public static class ConfigurationMigrator
{
    private static readonly string[] LegacySitCommands = ["/sit", "/groundsit", "/lounge"];
    private const int MinRegexLength = 100;
    private const int MaxRegexLength = 10000;

    public static bool MigrateAndNormalize(Configuration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.Version < 0)
            throw new InvalidOperationException($"Invalid configuration version: {configuration.Version}.");
        if (configuration.Version > ConfigVersion.CURRENT)
            throw new InvalidOperationException(
                $"Configuration v{configuration.Version} is newer than supported v{ConfigVersion.CURRENT}.");

        var changed = false;
        while (configuration.Version < ConfigVersion.CURRENT)
        {
            changed |= configuration.Version switch
            {
                0 => MigrateV0ToV1(configuration),
                1 => MigrateV1ToV2(configuration),
                2 => MigrateV2ToV3(configuration),
                3 => MigrateV3ToV4(configuration),
                4 => MigrateV4ToV5(configuration),
                _ => throw new InvalidOperationException(
                    $"No migration path exists from configuration v{configuration.Version}."),
            };
        }

        changed |= NormalizeLegacyCommandRules(configuration);
        return changed;
    }

    private static bool MigrateV0ToV1(Configuration configuration)
    {
        var enabledChannels = new List<int>();
        foreach (var channel in configuration.EnabledChannels ?? [])
        {
            if (channel is { Enabled: true })
                enabledChannels.Add(channel.ChatType);
        }

        configuration.Reactions =
        [
            new Reaction
            {
                Enabled = true,
                Name = "Reaction",
                TriggerPhrase = configuration.TriggerPhrase,
                AllowSit = configuration.AllowSit,
                MotionOnly = configuration.MotionOnly,
                AllowAllCommands = configuration.AllowAllCommands,
                UseRegex = configuration.UseRegex,
                CustomPhrase = configuration.CustomPhrase,
                ReplaceMatch = configuration.ReplaceMatch,
                TestInput = configuration.TestInput,
                EnabledChannels = enabledChannels,
            },
        ];
        configuration.Version = 1;
        return true;
    }

    private static bool MigrateV1ToV2(Configuration configuration)
    {
        configuration.ShowReactionNotifications = false;
        configuration.ShowSuppressedReactionNotifications = false;
        configuration.DefaultCommandWhitelist = [];
        configuration.DefaultCommandBlacklist = [.. LegacySitCommands];
        configuration.DefaultAllowAllCommands = false;
        configuration.DefaultMotionOnly = true;
        configuration.DefaultEnabledChannels = [];
        configuration.Version = 2;
        return true;
    }

    private static bool MigrateV2ToV3(Configuration configuration)
    {
        configuration.Reactions ??= [];
        foreach (var reaction in configuration.Reactions)
        {
            if (reaction == null)
                continue;
            reaction.ProgressNotifications = ReactionNotificationSetting.Inherit;
            reaction.SuppressedNotifications = ReactionNotificationSetting.Inherit;
        }
        configuration.Version = 3;
        return true;
    }

    // Mimic gets its own settings. It used Follow mode's, so it starts with copies of them.
    private static bool MigrateV4ToV5(Configuration configuration)
    {
        var follow = configuration.Follow ?? new FollowSettings();
        var mimic = configuration.Mimic ?? new MimicSettings();
        var words = follow.MimicWords;
        mimic.Enabled = follow.Enabled && (words == null || !string.IsNullOrWhiteSpace(words));
        mimic.MimicWords = string.IsNullOrWhiteSpace(words) ? "mimic" : words;
        mimic.MotionOnly = follow.MimicMotionOnly ?? true;
        mimic.CallNames = follow.CallNames ?? string.Empty;
        mimic.StopWords = follow.StopWords ?? "stop";
        mimic.Channels = follow.Channels != null ? [.. follow.Channels] : [13, 14, 24];
        mimic.Senders = follow.Senders?.Clone() ?? new SenderFilter();
        mimic.NeverFrom = follow.NeverFrom != null ? [.. follow.NeverFrom] : [];
        mimic.OnlyMimic = follow.OnlyFollow != null ? [.. follow.OnlyFollow] : [];
        mimic.NeverMimic = follow.NeverFollow != null ? [.. follow.NeverFollow] : [];
        mimic.ReplyWhenNotNearby = follow.ReplyWhenNotNearby;
        mimic.NotNearbyMessage = follow.NotNearbyMessage ?? mimic.NotNearbyMessage;
        configuration.Mimic = mimic;
        follow.MimicWords = null;
        follow.MimicMotionOnly = null;
        configuration.Version = 5;
        return true;
    }

    private static bool MigrateV3ToV4(Configuration configuration)
    {
        // Sender filters are new: keep every existing reaction working exactly as before.
        configuration.Reactions ??= [];
        foreach (var reaction in configuration.Reactions)
        {
            if (reaction == null)
                continue;
            reaction.Senders = SenderFilter.AnyoneFilter();
            // "Any command" used to include chat and plugin commands; now it covers plain game commands only. Ask the
            // user to look these over (they also react to anyone now).
            if (reaction.AllowAllCommands)
                configuration.ReviewAfterMigration.Add(string.IsNullOrWhiteSpace(reaction.Name) ? "Unnamed" : reaction.Name);
        }
        configuration.IgnoreOwnMessages = true;
        configuration.Version = 4;
        return true;
    }

    private static bool NormalizeLegacyCommandRules(Configuration configuration)
    {
        var changed = false;
        if (configuration.EnabledChannels == null)
        {
            configuration.EnabledChannels = [];
            changed = true;
        }
        if (configuration.CustomChannels == null)
        {
            configuration.CustomChannels = [];
            changed = true;
        }
        if (configuration.Reactions == null)
        {
            configuration.Reactions = [];
            changed = true;
        }
        changed |= RemoveNullEntries(configuration.EnabledChannels);
        changed |= RemoveNullEntries(configuration.CustomChannels);
        changed |= RemoveNullEntries(configuration.Reactions);
        configuration.DefaultProtections = RepairProtections(configuration.DefaultProtections, ref changed);
        if (configuration.DefaultCommandWhitelist == null)
        {
            configuration.DefaultCommandWhitelist = [];
            changed = true;
        }
        if (configuration.DefaultCommandBlacklist == null)
        {
            configuration.DefaultCommandBlacklist = [.. LegacySitCommands];
            changed = true;
        }
        if (configuration.DefaultEnabledChannels == null)
        {
            configuration.DefaultEnabledChannels = [];
            changed = true;
        }
        if (configuration.MaxRegexLength is < MinRegexLength or > MaxRegexLength)
        {
            configuration.MaxRegexLength = Configuration.DefaultMaxRegexLength;
            changed = true;
        }
        if (!float.IsFinite(configuration.TextScale))
        {
            configuration.TextScale = 1f;
            changed = true;
        }
        if (!Enum.IsDefined(configuration.Accent))
        {
            configuration.Accent = default;
            changed = true;
        }

        if (configuration.EmoteReplies == null)
        {
            configuration.EmoteReplies = new EmoteReplySettings();
            changed = true;
        }
        var replies = configuration.EmoteReplies;
        replies.Senders = RepairSenders(replies.Senders, ref changed);
        if (replies.Overrides == null) { replies.Overrides = []; changed = true; }
        changed |= RemoveNullEntries(replies.Overrides);
        // An override with no emote to match can never apply; a null reply means "don't reply".
        changed |= replies.Overrides.RemoveAll(entry => string.IsNullOrWhiteSpace(entry.When)) > 0;
        foreach (var entry in replies.Overrides)
        {
            if (entry.Reply == null) { entry.Reply = string.Empty; changed = true; }
        }
        var replyCooldown = Math.Clamp(replies.PerPlayerCooldownSeconds,
            EmoteReplySettings.MinimumCooldownSeconds, EmoteReplySettings.MaximumCooldownSeconds);
        if (replies.PerPlayerCooldownSeconds != replyCooldown)
        {
            replies.PerPlayerCooldownSeconds = replyCooldown;
            changed = true;
        }
        if (replies.BlockedEmotes == null) { replies.BlockedEmotes = []; changed = true; }
        changed |= DeduplicateCommands(replies.BlockedEmotes);

        if (configuration.Mimic == null)
        {
            configuration.Mimic = new MimicSettings();
            changed = true;
        }
        var mimic = configuration.Mimic;
        if (mimic.CallNames == null) { mimic.CallNames = string.Empty; changed = true; }
        if (mimic.MimicWords == null) { mimic.MimicWords = "mimic"; changed = true; }
        if (mimic.StopWords == null) { mimic.StopWords = "stop"; changed = true; }
        if (mimic.NotNearbyMessage == null) { mimic.NotNearbyMessage = string.Empty; changed = true; }
        if (mimic.Channels == null) { mimic.Channels = []; changed = true; }
        changed |= DeduplicateChannels(mimic.Channels);
        mimic.Senders = RepairSenders(mimic.Senders, ref changed);
        mimic.NeverFrom = RepairStrings(mimic.NeverFrom, ref changed);
        mimic.OnlyMimic = RepairStrings(mimic.OnlyMimic, ref changed);
        mimic.NeverMimic = RepairStrings(mimic.NeverMimic, ref changed);
        var delay = float.IsFinite(mimic.DelaySeconds) ? Math.Clamp(mimic.DelaySeconds, 0f, MimicSettings.MaxDelaySeconds) : 0f;
        if (delay != mimic.DelaySeconds) { mimic.DelaySeconds = delay; changed = true; }
        var guard = float.IsFinite(mimic.RepeatGuardSeconds) ? Math.Clamp(mimic.RepeatGuardSeconds, 0f, MimicSettings.MaxRepeatGuardSeconds) : 3f;
        if (guard != mimic.RepeatGuardSeconds) { mimic.RepeatGuardSeconds = guard; changed = true; }

        if (configuration.Follow == null)
        {
            configuration.Follow = new FollowSettings();
            changed = true;
        }
        var follow = configuration.Follow;
        if (follow.CallNames == null) { follow.CallNames = string.Empty; changed = true; }
        if (follow.FollowWords == null) { follow.FollowWords = "follow"; changed = true; }
        if (follow.StopWords == null) { follow.StopWords = "stop"; changed = true; }
        if (follow.ComeWords == null) { follow.ComeWords = "come"; changed = true; }
        if (follow.NotNearbyMessage == null) { follow.NotNearbyMessage = string.Empty; changed = true; }
        if (follow.Channels == null) { follow.Channels = []; changed = true; }
        changed |= DeduplicateChannels(follow.Channels);
        follow.Senders = RepairSenders(follow.Senders, ref changed);
        follow.NeverFrom = RepairStrings(follow.NeverFrom, ref changed);
        follow.OnlyFollow = RepairStrings(follow.OnlyFollow, ref changed);
        follow.NeverFollow = RepairStrings(follow.NeverFollow, ref changed);
        follow.StopCommands = RepairStrings(follow.StopCommands, ref changed);

        changed |= NormalizeCustomChannels(configuration.CustomChannels);
        changed |= DeduplicateCommands(configuration.DefaultCommandWhitelist);
        changed |= DeduplicateCommands(configuration.DefaultCommandBlacklist);
        changed |= DeduplicateChannels(configuration.DefaultEnabledChannels);

        foreach (var reaction in configuration.Reactions)
        {
            if (reaction.Name == null) { reaction.Name = string.Empty; changed = true; }
            if (reaction.TriggerPhrase == null) { reaction.TriggerPhrase = Reaction.DefaultTriggerPhrase; changed = true; }
            if (reaction.CustomPhrase == null) { reaction.CustomPhrase = string.Empty; changed = true; }
            if (reaction.ReplaceMatch == null) { reaction.ReplaceMatch = string.Empty; changed = true; }
            if (reaction.TestInput == null) { reaction.TestInput = string.Empty; changed = true; }
            if (reaction.EnabledChannels == null) { reaction.EnabledChannels = []; changed = true; }
            if (reaction.CommandWhitelist == null) { reaction.CommandWhitelist = []; changed = true; }
            if (reaction.CommandBlacklist == null) { reaction.CommandBlacklist = []; changed = true; }
            if (reaction.Senders == null) { reaction.Senders = SenderFilter.AnyoneFilter(); changed = true; }
            reaction.Senders = RepairSenders(reaction.Senders, ref changed);
            reaction.Protections = RepairProtections(reaction.Protections, ref changed);

            changed |= DeduplicateChannels(reaction.EnabledChannels);
            changed |= DeduplicateCommands(reaction.CommandWhitelist);
            changed |= DeduplicateCommands(reaction.CommandBlacklist);

            if (!reaction.AllowSit)
            {
                foreach (var command in LegacySitCommands)
                {
                    if (!PluginUiLogic.ContainsCommand(reaction.CommandBlacklist, command))
                    {
                        reaction.CommandBlacklist.Add(command);
                        changed = true;
                    }
                }

                reaction.AllowSit = true;
                changed = true;
            }

            var cooldown = Math.Clamp(reaction.CooldownSeconds, 0, Reaction.MaxCooldownSeconds);
            if (reaction.CooldownSeconds != cooldown)
            {
                reaction.CooldownSeconds = cooldown;
                changed = true;
            }
            if (!Enum.IsDefined(reaction.ExecutionPolicy))
            {
                reaction.ExecutionPolicy = ReactionExecutionPolicy.QueueEveryTrigger;
                changed = true;
            }
            if (!Enum.IsDefined(reaction.ProgressNotifications))
            {
                reaction.ProgressNotifications = ReactionNotificationSetting.Inherit;
                changed = true;
            }
            if (!Enum.IsDefined(reaction.SuppressedNotifications))
            {
                reaction.SuppressedNotifications = ReactionNotificationSetting.Inherit;
                changed = true;
            }
        }
        return changed;
    }

    private static bool DeduplicateCommands(List<string> commands)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var writeIndex = 0;
        for (var readIndex = 0; readIndex < commands.Count; readIndex++)
        {
            var command = commands[readIndex];
            if (string.IsNullOrWhiteSpace(command) || !seen.Add(command))
                continue;
            commands[writeIndex++] = command;
        }

        if (writeIndex == commands.Count)
            return false;
        commands.RemoveRange(writeIndex, commands.Count - writeIndex);
        return true;
    }

    private static bool DeduplicateChannels(List<int> channels)
    {
        var seen = new HashSet<int>();
        var writeIndex = 0;
        for (var readIndex = 0; readIndex < channels.Count; readIndex++)
        {
            if (!seen.Add(channels[readIndex]))
                continue;
            if (channels[readIndex] < ushort.MinValue || channels[readIndex] > ushort.MaxValue)
                continue;
            channels[writeIndex++] = channels[readIndex];
        }

        if (writeIndex == channels.Count)
            return false;
        channels.RemoveRange(writeIndex, channels.Count - writeIndex);
        return true;
    }

    private static bool NormalizeCustomChannels(List<ChannelSetting> channels)
    {
        var seen = new HashSet<int>();
        var writeIndex = 0;
        var changed = false;
        for (var readIndex = 0; readIndex < channels.Count; readIndex++)
        {
            var channel = channels[readIndex];
            if (channel.ChatType < ushort.MinValue || channel.ChatType > ushort.MaxValue ||
                !seen.Add(channel.ChatType))
            {
                changed = true;
                continue;
            }
            if (channel.Name == null)
            {
                channel.Name = string.Empty;
                changed = true;
            }
            channels[writeIndex++] = channel;
        }

        if (writeIndex == channels.Count)
            return changed;
        channels.RemoveRange(writeIndex, channels.Count - writeIndex);
        return true;
    }

    private static bool RemoveNullEntries<T>(List<T> items)
        where T : class
    {
        return items.RemoveAll(static item => item == null) > 0;
    }

    private static ProtectionSettings RepairProtections(ProtectionSettings? protections, ref bool changed)
    {
        if (protections == null)
        {
            changed = true;
            return new ProtectionSettings();
        }
        protections.OpenChat = RepairStrings(protections.OpenChat, ref changed);
        protections.OpenRisky = RepairStrings(protections.OpenRisky, ref changed);
        protections.OpenPlugins = RepairStrings(protections.OpenPlugins, ref changed);
        return protections;
    }

    // A missing filter gets the new-reaction default (friends, free company, party).
    private static SenderFilter RepairSenders(SenderFilter? senders, ref bool changed)
    {
        if (senders == null)
        {
            changed = true;
            return new SenderFilter();
        }
        senders.Named = RepairStrings(senders.Named, ref changed);
        return senders;
    }

    // Never null, and no null or blank entries (duplicates are kept: they may be meant).
    private static List<string> RepairStrings(List<string>? items, ref bool changed)
    {
        if (items == null)
        {
            changed = true;
            return [];
        }
        if (items.RemoveAll(string.IsNullOrWhiteSpace) > 0)
            changed = true;
        return items;
    }
}
