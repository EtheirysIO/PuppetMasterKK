using System.Text.RegularExpressions;
using PuppetMaster;

Run("PuppetMaster_v0.json", configuration =>
{
    Assert(configuration.Version == ConfigVersion.CURRENT, "v0 should migrate to the current version");
    Assert(configuration.Reactions.Count == 1, "v0 should create one reaction");
    var reaction = configuration.Reactions[0];
    Assert(reaction.TriggerPhrase == "please do", "v0 trigger should be preserved");
    Assert(reaction.EnabledChannels.Contains(10), "v0 enabled Say channel should be preserved");
    Assert(reaction.CommandBlacklist.SequenceEqual(["/sit", "/groundsit", "/lounge"]), "v0 sit rules should normalize");
    Assert(!configuration.ShowReactionNotifications, "v0 should keep notifications off during migration");
    Assert(!configuration.ShowSuppressedReactionNotifications, "v0 should keep suppression notifications off by default");
    Assert(configuration.DefaultCommandWhitelist.Count == 0, "v0 should receive an empty default whitelist");
    Assert(configuration.DefaultCommandBlacklist.SequenceEqual(["/sit", "/groundsit", "/lounge"]), "v0 should receive safe command defaults");
    Assert(!configuration.DefaultAllowAllCommands && configuration.DefaultMotionOnly, "v0 should receive safe command behavior defaults");
    Assert(configuration.DefaultEnabledChannels.Count == 0, "v0 should receive empty channel defaults");
    Assert(reaction.ExecutionPolicy == ReactionExecutionPolicy.QueueEveryTrigger,
        "v0 reaction should preserve legacy retrigger behavior");
    Assert(reaction.Senders.Anyone, "migrated reactions should still react to anyone");
    Assert(configuration.IgnoreOwnMessages, "migrated configs should ignore your own messages");
    Assert(!configuration.EmoteReplies.Enabled, "emote replies should start off");
    Assert(reaction.ProgressNotifications == ReactionNotificationSetting.Inherit &&
           reaction.SuppressedNotifications == ReactionNotificationSetting.Inherit,
        "v0 reaction should inherit the v3 notification defaults");
});

Run("PuppetMaster_v1.json", configuration =>
{
    Assert(configuration.Version == ConfigVersion.CURRENT, "v1 should migrate to the current version");
    Assert(!configuration.ShowReactionNotifications, "v1 should keep notifications off during migration");
    Assert(!configuration.ShowSuppressedReactionNotifications, "v1 should keep suppression notifications off by default");
    Assert(configuration.Reactions[0].AllowAllCommands, "v1 AllowAllCommands should be preserved");
    Assert(configuration.Reactions[0].EnabledChannels.SequenceEqual([10, 14]), "v1 channels should be preserved");
    Assert(configuration.DefaultCommandBlacklist.SequenceEqual(["/sit", "/groundsit", "/lounge"]), "v1 should receive safe command defaults");
    Assert(!configuration.DefaultAllowAllCommands && configuration.DefaultMotionOnly, "v1 should receive safe command behavior defaults");
    Assert(configuration.DefaultEnabledChannels.Count == 0, "v1 should receive empty channel defaults");
    Assert(configuration.Reactions[0].ExecutionPolicy == ReactionExecutionPolicy.QueueEveryTrigger,
        "v1 reaction should preserve legacy retrigger behavior");
});

Run("PuppetMaster_v2_legacy.json", configuration =>
{
    Assert(configuration.Version == ConfigVersion.CURRENT, "v2 should migrate to the current version");
    Assert(!configuration.ShowReactionNotifications, "existing v2 notification choice should be preserved");
    Assert(configuration.ShowSuppressedReactionNotifications, "existing v2 suppression notification choice should be preserved");
    var reaction = configuration.Reactions[0];
    Assert(reaction.AllowSit, "legacy sit marker should be normalized");
    Assert(reaction.AllowAllCommands, "existing AllowAllCommands should be preserved");
    Assert(reaction.CooldownSeconds == 0, "negative cooldown should normalize to zero");
    Assert(reaction.ExecutionPolicy == ReactionExecutionPolicy.QueueEveryTrigger,
        "existing v2 reaction without an execution policy should preserve legacy retrigger behavior");
    Assert(reaction.CommandWhitelist.SequenceEqual(["/echo"]), "whitelist should deduplicate case-insensitively");
    Assert(reaction.CommandBlacklist.SequenceEqual(["/sit", "/groundsit", "/lounge"]), "blacklist should deduplicate and add legacy sit rules");
    Assert(configuration.DefaultCommandWhitelist.SequenceEqual(["/echo"]), "default whitelist should deduplicate case-insensitively");
    Assert(configuration.DefaultCommandBlacklist.SequenceEqual(["/sit", "/groundsit"]), "default blacklist should deduplicate case-insensitively");
    Assert(configuration.DefaultEnabledChannels.SequenceEqual([10, 14]), "default channels should deduplicate");
    Assert(configuration.CustomChannels.Count == 1 &&
           configuration.CustomChannels[0].ChatType == 77 &&
           configuration.CustomChannels[0].Name == "First",
        "custom channels should remove invalid and duplicate IDs while preserving the first entry");
    Assert(reaction.ProgressNotifications == ReactionNotificationSetting.Inherit &&
           reaction.SuppressedNotifications == ReactionNotificationSetting.Inherit,
        "v2 reactions should inherit global notification choices after migration");
    Assert(!PluginUiLogic.ResolveNotificationSetting(reaction.ProgressNotifications, configuration.ShowReactionNotifications) &&
           PluginUiLogic.ResolveNotificationSetting(reaction.SuppressedNotifications, configuration.ShowSuppressedReactionNotifications),
        "v2 notification behavior should be unchanged after migration");

    var created = Reaction.CreateDefault(
        commandWhitelist: configuration.DefaultCommandWhitelist,
        commandBlacklist: configuration.DefaultCommandBlacklist,
        allowAllCommands: configuration.DefaultAllowAllCommands,
        motionOnly: configuration.DefaultMotionOnly,
        enabledChannels: configuration.DefaultEnabledChannels);
    configuration.DefaultCommandWhitelist.Add("/wait");
    configuration.DefaultEnabledChannels.Add(57);
    Assert(created.CommandWhitelist.SequenceEqual(["/echo"]), "new reactions should copy rather than share command defaults");
    Assert(created.AllowAllCommands, "new reactions should copy the allow-all default");
    Assert(!created.MotionOnly, "new reactions should copy the emote motion default");
    Assert(created.EnabledChannels.SequenceEqual([10, 14]), "new reactions should copy rather than share channel defaults");
    Assert(created.ExecutionPolicy == ReactionExecutionPolicy.IgnoreWhileRunning,
        "new reactions should use the safe execution policy default");
    Assert(!created.Enabled &&
           created.ProgressNotifications == ReactionNotificationSetting.Inherit &&
           created.SuppressedNotifications == ReactionNotificationSetting.Inherit,
        "new reactions should start disabled and follow both global notification defaults");
    Assert(created.TriggerPhrase == Reaction.DefaultTriggerPhrase && created.TriggerPhrase == "please do",
        "new reactions should start with the familiar default trigger");
    Assert(!created.Senders.Anyone && created.Senders.Friends && created.Senders.FreeCompany && created.Senders.Party,
        "new reactions should only react to friends, free company and party by default");
    Assert(reaction.Senders.Anyone, "a reaction migrated from v2 should still react to anyone");

    created.TriggerPhrase = string.Empty;
    PluginUiLogic.EnsureRegexRestoreTrigger(created);
    Assert(created.TriggerPhrase == "please do",
        "restoring regex defaults should repair an empty base trigger");
});

Run("PuppetMaster_v2_null_collections.json", configuration =>
{
    Assert(configuration.Version == ConfigVersion.CURRENT, "null-collection fixture should migrate to the current version");
    Assert(configuration.EnabledChannels.Count == 0, "null enabled channels should normalize to an empty list");
    Assert(configuration.CustomChannels.Count == 0, "null custom channels should normalize to an empty list");
    Assert(configuration.Reactions.Count == 0, "null reactions should normalize to an empty list");
    Assert(configuration.DefaultCommandWhitelist.Count == 0,
        "null default whitelist should normalize to an empty list");
    Assert(configuration.DefaultCommandBlacklist.SequenceEqual(["/sit", "/groundsit", "/lounge"]),
        "null default blacklist should normalize to safe defaults");
    Assert(configuration.DefaultEnabledChannels.Count == 0,
        "null default channels should normalize to an empty list");
});

var future = new Configuration { Version = ConfigVersion.CURRENT + 1 };
AssertThrows<InvalidOperationException>(() => ConfigurationMigrator.MigrateAndNormalize(future), "future config should be rejected");

RunExecutionGateTests();
RunReactionCommandMatcherTests();
RunPluginUiLogicTests();
RunConfigurationBoundaryTests();
RunDebugLogBufferTests();
RunRetriggerQueueTests();
RunRetriggerSchedulerTests();
RunReactionVisualizerStateTests();
RunConfigurationUpgradeTransactionTests();
RunDalamudRoundTripTests();
RunWaitParsingTests();
RunCommandPolicyTests();
RunSenderFilterTests();
RunRateLimiterTests();

Console.WriteLine("All PuppetMaster configuration migration tests passed.");
return;

void Run(string fixtureName, Action<Configuration> assertions)
{
    var path = Path.Combine(AppContext.BaseDirectory, "TestConfigs", fixtureName);
    var configuration = DalamudJson.Load(File.ReadAllText(path));
    ConfigurationMigrator.MigrateAndNormalize(configuration);
    assertions(configuration);
    var normalizedJson = DalamudJson.Save(configuration);
    var changedAgain = ConfigurationMigrator.MigrateAndNormalize(configuration);
    Assert(!changedAgain, $"{fixtureName} migration should be idempotent");
    Assert(DalamudJson.Save(configuration) == normalizedJson, $"{fixtureName} should not change on a second pass");

    // The path a real user takes: the saved file is loaded again on the next start and migrated again.
    var reloaded = DalamudJson.Load(normalizedJson);
    Assert(!ConfigurationMigrator.MigrateAndNormalize(reloaded), $"{fixtureName} should not change after save and reload");
    Assert(DalamudJson.Save(reloaded) == normalizedJson, $"{fixtureName} should survive save -> load -> save unchanged");
    Console.WriteLine($"PASS {fixtureName}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException($"Assertion failed: {message}");
}

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException($"Assertion failed: {message}");
}

static void RunExecutionGateTests()
{
    var gate = new ReactionExecutionGate();
    var reaction = new Reaction();
    const long startedAt = 1_000_000;
    var second = System.Diagnostics.Stopwatch.Frequency;

    Assert(gate.TryEnter(reaction, TimeSpan.FromSeconds(10), startedAt, out var first, out var firstReason),
        "first reaction run should enter");
    Assert(firstReason == ReactionRejectionReason.None, "accepted run should have no rejection reason");
    Assert(!gate.TryEnter(reaction, TimeSpan.FromSeconds(10), startedAt + 20 * second, out _, out var runningReason),
        "single-flight should win even after cooldown expires");
    Assert(runningReason == ReactionRejectionReason.Busy, "active run should reject as busy");

    first!.Dispose();
    Assert(gate.TryEnter(reaction, TimeSpan.FromSeconds(10), startedAt + 20 * second, out var secondLease, out _),
        "reaction should run immediately after a long run when cooldown already expired");
    secondLease!.Dispose();

    var cooldownReaction = new Reaction();
    Assert(gate.TryEnter(cooldownReaction, TimeSpan.FromSeconds(10), startedAt, out var cooldownLease, out _),
        "cooldown test run should enter");
    cooldownLease!.Dispose();
    Assert(!gate.TryEnter(cooldownReaction, TimeSpan.FromSeconds(10), startedAt + 5 * second, out _, out var cooldownReason),
        "completed reaction should remain blocked during cooldown");
    Assert(cooldownReason == ReactionRejectionReason.Cooldown, "early retrigger should reject as cooldown");
    Assert(gate.TryEnter(cooldownReaction, TimeSpan.FromSeconds(10), startedAt + 10 * second, out var finalLease, out _),
        "reaction should enter at cooldown boundary");
    finalLease!.Dispose();

    var restartReaction = new Reaction();
    Assert(gate.TryEnter(restartReaction, TimeSpan.FromSeconds(30), startedAt, out var restartInitialLease, out _),
        "restart test should acquire its initial lease");
    restartInitialLease!.Dispose();
    Assert(gate.TryEnter(
            restartReaction,
            TimeSpan.Zero,
            startedAt + second,
            out var restartLease,
            out _,
            ignoreCooldown: true),
        "restart-immediately should bypass a stored cooldown");
    restartLease!.Dispose();

    var priorityGate = new ReactionExecutionGate();
    var priorityReaction = new Reaction();
    Assert(priorityGate.TryEnter(
            priorityReaction,
            TimeSpan.Zero,
            System.Diagnostics.Stopwatch.GetTimestamp(),
            out var activeLease,
            out _),
        "priority test should acquire its initial lease");
    var queuedLeaseTask = priorityGate.EnterWhenAvailableAsync(
        priorityReaction,
        TimeSpan.Zero,
        CancellationToken.None);
    Assert(!queuedLeaseTask.IsCompleted, "queued entrant should wait for the active run");
    Assert(!priorityGate.TryEnter(
            priorityReaction,
            TimeSpan.Zero,
            System.Diagnostics.Stopwatch.GetTimestamp(),
            out _,
            out var priorityReason),
        "fresh trigger should not bypass an existing queued entrant");
    Assert(priorityReason == ReactionRejectionReason.Busy,
        "fresh trigger behind a queued entrant should be treated as busy");
    activeLease!.Dispose();
    var queuedLease = queuedLeaseTask.GetAwaiter().GetResult();
    queuedLease.Dispose();

    Console.WriteLine("PASS reaction execution gate");
}

static void RunReactionCommandMatcherTests()
{
    var toggledReaction = new Reaction
    {
        UseRegex = false,
        Rx = new Regex(@"(?i)\b(?:please do)\s+(?:\((.*?)\)|(\w+))"),
    };
    Assert(ReactionCommandMatcher.SelectPattern(toggledReaction) == toggledReaction.Rx,
        "simple mode should select the generated simple pattern");
    toggledReaction.UseRegex = true;
    Assert(ReactionCommandMatcher.SelectPattern(toggledReaction) == null,
        "switching to regex mode with an empty custom pattern should not fall back to the simple pattern");
    var emptyPatternStatus = ReactionCommandMatcher.TryGenerateCommand(
        ReactionCommandMatcher.SelectPattern(toggledReaction),
        "please do wave",
        "/$1$2",
        out var emptyPatternCommand,
        out var emptyPatternError);
    Assert(emptyPatternStatus == ReactionMatchStatus.NoMatch && emptyPatternCommand.Length == 0 && emptyPatternError == null,
        "an empty active regex should produce no preview instead of throwing");

    var lookbehindPattern = new Regex(
        @"(?<=Boss uses )(Fire)",
        RegexOptions.None,
        TimeSpan.FromMilliseconds(250));
    var status = ReactionCommandMatcher.TryGenerateCommand(
        lookbehindPattern,
        "Boss uses Fire",
        "/echo $1",
        out var command,
        out var error);

    Assert(status == ReactionMatchStatus.Success, "lookbehind match should generate a command");
    Assert(command == "/echo Fire", "replacement should use captures from the original match");
    Assert(error is null, "successful replacement should not report an error");

    var invalidStatus = ReactionCommandMatcher.TryGenerateCommand(
        new Regex("(Fire)", RegexOptions.None, TimeSpan.FromMilliseconds(250)),
        "Fire",
        "$2147483648",
        out _,
        out var invalidError);

    Assert(invalidStatus == ReactionMatchStatus.InvalidReplacement, "malformed replacement should be rejected");
    Assert(!string.IsNullOrWhiteSpace(invalidError), "malformed replacement should explain the error");

    Console.WriteLine("PASS reaction command matcher");
}

static void RunPluginUiLogicTests()
{
    var reaction = new Reaction
    {
        Name = "Morning Wave",
        TriggerPhrase = "please do",
        TestInput = "please do wave",
        Rx = new Regex(@"(?i)\b(?:please do)\s+(?:\((.*?)\)|(\w+))"),
        EnabledChannels = [10],
        CommandWhitelist = ["/echo"],
        CommandBlacklist = ["/logout"],
    };

    Assert(PluginUiLogic.MatchesSearch(reaction, "morning"), "reaction search should match names");
    Assert(PluginUiLogic.MatchesSearch(reaction, "PLEASE"), "reaction search should match simple triggers case-insensitively");
    Assert(!PluginUiLogic.MatchesSearch(reaction, "missing"), "reaction search should reject unrelated text");
    reaction.UseRegex = true;
    reaction.CustomPhrase = "^hello$";
    Assert(PluginUiLogic.MatchesSearch(reaction, "HELLO"), "reaction search should use the active regex trigger");
    reaction.UseRegex = false;
    var groupedReactions = new List<Reaction>
    {
        reaction,
        new() { Name = "Morning Wave", TriggerPhrase = "second" },
        new() { Name = "Individual", TriggerPhrase = "solo" },
    };
    var cancelled = new List<Reaction>();
    PluginUiLogic.SetReactionEnabled(groupedReactions[0], false, cancelled.Add);
    Assert(!groupedReactions[0].Enabled && cancelled.SequenceEqual([groupedReactions[0]]),
        "disabling from the editor should cancel that reaction");
    Assert(PluginUiLogic.TryDeleteReaction(groupedReactions, 1, out var nextIndex, cancelled.Add) && nextIndex == 1,
        "deleting a reaction should select the next valid index");
    Assert(groupedReactions.Count == 2 && groupedReactions[1].Name == "Individual",
        "deleting should remove only the selected reaction");
    var singleReaction = new List<Reaction> { new() };
    Assert(!PluginUiLogic.TryDeleteReaction(singleReaction, 0, out _, cancelled.Add),
        "the UI should preserve its final reaction");

    Assert(PluginUiLogic.GetStatus(reaction) == ReactionUiStatus.Disabled, "disabled reactions should report disabled");
    reaction.Enabled = true;
    reaction.Rx = null;
    Assert(PluginUiLogic.GetStatus(reaction) == ReactionUiStatus.InvalidTrigger, "missing compiled triggers should report invalid");
    reaction.Rx = new Regex("please do");
    reaction.EnabledChannels.Clear();
    Assert(PluginUiLogic.GetStatus(reaction) == ReactionUiStatus.NoChannels, "reactions without channels should request attention");
    reaction.EnabledChannels.Add(10);
    reaction.Senders = SenderFilter.AnyoneFilter();
    Assert(PluginUiLogic.GetStatus(reaction) == ReactionUiStatus.Unsafe,
        "a reaction anyone can trigger in a public channel (Say) should ask for attention");
    reaction.EnabledChannels[0] = 24;
    Assert(PluginUiLogic.GetStatus(reaction) == ReactionUiStatus.Ready,
        "anyone in Free Company chat is not a stranger in a public channel");
    reaction.EnabledChannels[0] = 10;
    reaction.Senders = new SenderFilter();
    reaction.AllowAllCommands = true;
    Assert(PluginUiLogic.GetStatus(reaction) == ReactionUiStatus.Ready,
        "a reaction limited to trusted senders should report ready, even with any game command allowed");
    reaction.AllowAllCommands = false;
    Assert(PluginUiLogic.GetStatus(reaction) == ReactionUiStatus.Ready, "complete reactions should report ready");

    var named = new Reaction { Senders = new SenderFilter { Anyone = false, Named = ["Some Body@Ultros"] } };
    var namedCopy = PluginUiLogic.CloneReaction(named);
    namedCopy.Senders.Named.Add("Other");
    Assert(namedCopy.Senders.Named.Count == 2 && named.Senders.Named.Count == 1 && !namedCopy.Senders.Anyone,
        "duplicating a reaction should copy its sender filter, not share it");
    Assert(PluginUiLogic.CreateReactionFromLog(57, "hello", "SystemMessage", new Configuration()).Senders.Anyone &&
           !PluginUiLogic.CreateReactionFromLog(24, "hello", "Free Company", new Configuration()).Senders.Anyone,
        "a reaction made from a system line must allow anyone (no player sends it); one from player chat keeps the safe default");

    PluginUiLogic.SetRegexMode(reaction, true);
    Assert(reaction.UseRegex && ReactionCommandMatcher.SelectPattern(reaction) == null,
        "regex toggle should select only the empty custom pattern without throwing");
    PluginUiLogic.SetRegexMode(reaction, false);
    Assert(ReactionCommandMatcher.SelectPattern(reaction) == reaction.Rx,
        "switching back should restore the simple preview pattern");
    Assert(PluginUiLogic.ClampCooldown(-1) == 0 && PluginUiLogic.ClampCooldown(90000) == 86400,
        "cooldown editor values should stay within UI bounds");
    Assert(PluginUiLogic.IgnoresCooldown(ReactionExecutionPolicy.RestartImmediately) &&
           PluginUiLogic.RestartsActiveRun(ReactionExecutionPolicy.RestartImmediately),
        "restart-immediately UI should disable cooldown and identify the active run for replacement");
    Assert(!PluginUiLogic.IgnoresCooldown(ReactionExecutionPolicy.QueueLatestTrigger) &&
           !PluginUiLogic.RestartsActiveRun(ReactionExecutionPolicy.QueueEveryTrigger),
        "existing execution policies should retain their cooldown and non-restart behavior");
    Assert(PluginUiLogic.ExecutionPolicyOptions.Select(option => option.Policy).SequenceEqual(
               [
                   ReactionExecutionPolicy.IgnoreWhileRunning,
                   ReactionExecutionPolicy.QueueEveryTrigger,
                   ReactionExecutionPolicy.QueueLatestTrigger,
                   ReactionExecutionPolicy.RestartImmediately,
               ]) &&
           PluginUiLogic.ExecutionPolicyLabels.SequenceEqual(
               ["Ignore", "Queue every trigger", "Queue latest trigger", "Restart immediately"]) &&
           PluginUiLogic.ExecutionPolicyOptions.Select(option => option.Policy).Distinct().Count() ==
               Enum.GetValues<ReactionExecutionPolicy>().Length,
        "execution-policy selector should use the guide order while mapping every persisted enum exactly once");
    Assert(PluginUiLogic.NotificationSettingLabels.SequenceEqual(
               ["Default", "Show", "Hide"]) &&
           PluginUiLogic.NotificationSettingLabels.Length == Enum.GetValues<ReactionNotificationSetting>().Length &&
           PluginUiLogic.NotificationSettingLabels[(int)ReactionNotificationSetting.Inherit] == "Default" &&
           PluginUiLogic.NotificationSettingLabels[(int)ReactionNotificationSetting.Enabled] == "Show" &&
           PluginUiLogic.NotificationSettingLabels[(int)ReactionNotificationSetting.Disabled] == "Hide",
        "notification radio groups should map every persisted enum value to the matching UI label");
    Assert(PluginUiLogic.ChannelCategoryLabels.SequenceEqual(
               ["Common", "CWLS", "Linkshells", "System", "Combat", "Activities", "Social", "GM", "Other", "Custom"]),
        "channel selectors should expose one compact category at a time");
    Assert(PluginUiLogic.AdditionalChannelCategoryLabels.SequenceEqual(
               ["System", "Combat", "Activities", "Social", "GM", "Other"]) &&
           PluginUiLogic.GetAdvancedChannelCategory("SystemMessage") == "System" &&
           PluginUiLogic.GetAdvancedChannelCategory("GainBuff") == "Combat" &&
           PluginUiLogic.GetAdvancedChannelCategory("LootRoll") == "Activities" &&
           PluginUiLogic.GetAdvancedChannelCategory("NPCDialogue") == "Social" &&
           PluginUiLogic.GetAdvancedChannelCategory("GmTell") == "GM" &&
           PluginUiLogic.GetAdvancedChannelCategory("UnknownFutureType") == "Other",
        "advanced channels should be grouped by user-facing purpose");
    Assert(PluginUiLogic.GetExecutionPolicyDescription(ReactionExecutionPolicy.QueueEveryTrigger)
               .Contains("up to 16 waiting") &&
           PluginUiLogic.GetExecutionPolicyDescription(ReactionExecutionPolicy.QueueLatestTrigger)
               .Contains("newest request") &&
           PluginUiLogic.GetExecutionPolicyDescription(ReactionExecutionPolicy.RestartImmediately)
               .Contains("Stops the remaining steps") &&
           PluginUiLogic.GetExecutionPolicyDescription(ReactionExecutionPolicy.IgnoreWhileRunning)
               .Contains("Ignores the new message"),
        "repeat-behavior choices should use clear user-facing descriptions");
    Assert(PluginUiLogic.GetCooldownDescription(ReactionExecutionPolicy.RestartImmediately)
               .Contains("Cooldown does not apply") &&
           PluginUiLogic.GetCooldownDescription(ReactionExecutionPolicy.QueueLatestTrigger)
               .Contains("Minimum time between starts"),
        "repeat-behavior editor should clearly explain cooldown behavior");

    Assert(PluginUiLogic.NormalizeCommand(" echo hello ") == "/echo", "command input should normalize to its lowercase command name");
    Assert(PluginUiLogic.NormalizeCommand("/AC Vercure [t]") == "/ac", "command normalization should ignore arguments and casing");
    Assert(PluginUiLogic.NormalizeCommand("  ") == string.Empty, "blank command input should be ignored");
    var allowed = new List<string> { "/echo" };
    var denied = new List<string> { "/logout", "/AC" };
    Assert(PluginUiLogic.AddCommandRule(allowed, denied, "/ac Vercure [t]"), "adding an allowed rule should succeed");
    Assert(allowed.SequenceEqual(["/echo", "/ac"]) && denied.SequenceEqual(["/logout"]),
        "adding a rule should remove its case-insensitive opposite entry");
    Assert(!PluginUiLogic.AddCommandRule(allowed, denied, "AC another argument"), "duplicate command rules should not be added");

    reaction.UseRegex = true;
    reaction.CustomPhrase = "^hello$";
    reaction.CustomRx = new Regex("^hello$");
    reaction.ExecutionPolicy = ReactionExecutionPolicy.QueueLatestTrigger;
    reaction.ProgressNotifications = ReactionNotificationSetting.Disabled;
    reaction.SuppressedNotifications = ReactionNotificationSetting.Enabled;
    var copy = PluginUiLogic.CloneReaction(reaction);
    Assert(!copy.Enabled && copy.Name == "Morning Wave Copy", "duplicated reactions should start disabled with a copy name");
    Assert(copy.UseRegex && copy.CustomPhrase == reaction.CustomPhrase && copy.ExecutionPolicy == reaction.ExecutionPolicy,
        "duplicate should preserve editor behavior");
    Assert(copy.ProgressNotifications == ReactionNotificationSetting.Disabled &&
           copy.SuppressedNotifications == ReactionNotificationSetting.Enabled,
        "duplicate should preserve notification overrides");
    copy.EnabledChannels.Add(99);
    copy.CommandWhitelist.Add("/wait");
    Assert(!reaction.EnabledChannels.Contains(99) && !reaction.CommandWhitelist.Contains("/wait"),
        "duplicate collections should not share mutable state");

    var configuration = new Configuration
    {
        DefaultCommandWhitelist = ["/echo"],
        DefaultCommandBlacklist = ["/logout"],
        DefaultAllowAllCommands = false,
        DefaultMotionOnly = false,
        DefaultEnabledChannels = [10, 14],
    };
    var fromLog = PluginUiLogic.CreateReactionFromLog(57, "good morning!", "Party", configuration);
    Assert(!fromLog.Enabled && fromLog.UseRegex, "log-created reactions should be disabled regex reactions");
    Assert(fromLog.CustomPhrase == "^good\\ morning!$" && fromLog.TestInput == "good morning!",
        "log-created reactions should exactly escape and preload the captured text");
    Assert(fromLog.EnabledChannels.SequenceEqual([57]), "log-created reactions should use only their source channel");
    Assert(fromLog.CommandWhitelist.SequenceEqual(["/echo"]) && fromLog.CommandBlacklist.SequenceEqual(["/logout"]) && !fromLog.MotionOnly,
        "log-created reactions should copy the current command defaults");

    var channels = new List<int> { 10, 10 };
    PluginUiLogic.SetChannel(channels, 10, false);
    Assert(channels.Count == 0, "disabling a channel should remove duplicate stale selections");
    PluginUiLogic.SetChannel(channels, 14, true);
    PluginUiLogic.SetChannel(channels, 14, true);
    Assert(channels.SequenceEqual([14]), "enabling a channel should not add duplicates");

    var custom = new ChannelSetting { ChatType = 77, Name = "Custom" };
    var duplicate = new ChannelSetting { ChatType = 88, Name = "Other" };
    var customChannels = new List<ChannelSetting> { custom, duplicate };
    Assert(PluginUiLogic.ValidateCustomChannelId(custom, -1, customChannels, _ => false) != null,
        "negative custom channel IDs should be rejected");
    Assert(PluginUiLogic.ValidateCustomChannelId(custom, 70000, customChannels, _ => false) != null,
        "oversized custom channel IDs should be rejected");
    Assert(PluginUiLogic.ValidateCustomChannelId(custom, 10, customChannels, id => id == 10) != null,
        "official channel IDs should be rejected as custom");
    Assert(PluginUiLogic.ValidateCustomChannelId(custom, 88, customChannels, _ => false) != null,
        "duplicate custom channel IDs should be rejected");
    Assert(PluginUiLogic.ValidateCustomChannelId(custom, 99, customChannels, _ => false) == null,
        "unique undocumented channel IDs should be accepted");
    Assert(PluginUiLogic.ShouldShowCustomChannel(
               new ChannelSetting { ChatType = 5, Name = "EnemyActions" },
               id => id == 5,
               _ => "Party"),
        "a custom channel with a conflicting official ID should remain visible for correction");
    Assert(!PluginUiLogic.ShouldShowCustomChannel(
               new ChannelSetting { ChatType = 5, Name = "Party" },
               id => id == 5,
               _ => "Party"),
        "legacy official channel entries should stay hidden from custom-channel settings");

    configuration.ShowReactionNotifications = false;
    configuration.ShowSuppressedReactionNotifications = true;
    Assert(!configuration.ShowReactionNotifications && configuration.ShowSuppressedReactionNotifications,
        "notification UI settings should remain independent");
    Assert(!PluginUiLogic.ResolveNotificationSetting(ReactionNotificationSetting.Inherit, false) &&
           PluginUiLogic.ResolveNotificationSetting(ReactionNotificationSetting.Inherit, true) &&
           PluginUiLogic.ResolveNotificationSetting(ReactionNotificationSetting.Enabled, false) &&
           !PluginUiLogic.ResolveNotificationSetting(ReactionNotificationSetting.Disabled, true),
        "per-reaction notification overrides should resolve independently from global defaults");

    Console.WriteLine("PASS plugin UI logic");
}

static void RunConfigurationBoundaryTests()
{
    AssertThrows<ArgumentNullException>(
        () => ConfigurationMigrator.MigrateAndNormalize(null!),
        "a null configuration should fail with a clear argument error");
    AssertThrows<InvalidOperationException>(
        () => ConfigurationMigrator.MigrateAndNormalize(new Configuration { Version = -1 }),
        "negative configuration versions should be rejected");

    var empty = new Configuration
    {
        Reactions = [],
        CurrentReactionEdit = -10,
        DefaultCommandWhitelist = ["/echo"],
        DefaultCommandBlacklist = ["/logout"],
        DefaultEnabledChannels = [10],
        DefaultMotionOnly = false,
    };
    var emptyIndex = PluginUiLogic.EnsureReactionSelection(empty, empty.CurrentReactionEdit);
    Assert(emptyIndex == 0 && empty.Reactions.Count == 1,
        "opening a zero-reaction configuration should create one editable reaction");
    Assert(empty.Reactions[0].TriggerPhrase == Reaction.DefaultTriggerPhrase &&
           empty.Reactions[0].EnabledChannels.SequenceEqual([10]) &&
           empty.Reactions[0].CommandWhitelist.SequenceEqual(["/echo"]) &&
           !empty.Reactions[0].MotionOnly,
        "the zero-config reaction should copy current defaults and remain usable");
    Assert(PluginUiLogic.EnsureReactionSelection(empty, 99) == 0 && empty.Reactions.Count == 1,
        "repairing a stale selection should not create duplicate reactions");

    var malformed = new Configuration
    {
        Reactions =
        [
            new Reaction
            {
                Name = null!,
                TriggerPhrase = null!,
                CustomPhrase = null!,
                ReplaceMatch = null!,
                TestInput = null!,
                EnabledChannels = null!,
                CommandWhitelist = null!,
                CommandBlacklist = null!,
                ExecutionPolicy = (ReactionExecutionPolicy)999,
                ProgressNotifications = (ReactionNotificationSetting)999,
                SuppressedNotifications = (ReactionNotificationSetting)(-1),
                CooldownSeconds = -20,
            },
        ],
        CustomChannels = [new ChannelSetting { ChatType = 77, Name = null! }],
        DefaultEnabledChannels = [-1, 10, 10, 70000],
        DefaultCommandWhitelist = [null!, " ", "/echo", "/ECHO"],
        DefaultCommandBlacklist = [null!, "/logout"],
    };
    Assert(ConfigurationMigrator.MigrateAndNormalize(malformed),
        "malformed current-version data should report that normalization changed it");
    var repaired = malformed.Reactions[0];
    Assert(repaired.Name == string.Empty && repaired.TriggerPhrase == Reaction.DefaultTriggerPhrase &&
           repaired.CustomPhrase == string.Empty && repaired.ReplaceMatch == string.Empty &&
           repaired.TestInput == string.Empty,
        "null reaction text should normalize to safe editor values");
    Assert(repaired.EnabledChannels.Count == 0 && repaired.CommandWhitelist.Count == 0,
        "null reaction collections should normalize to empty collections");
    Assert(repaired.ExecutionPolicy == ReactionExecutionPolicy.QueueEveryTrigger &&
           repaired.ProgressNotifications == ReactionNotificationSetting.Inherit &&
           repaired.SuppressedNotifications == ReactionNotificationSetting.Inherit &&
           repaired.CooldownSeconds == 0,
        "invalid enum transitions and negative cooldowns should return to supported defaults");
    Assert(malformed.DefaultEnabledChannels.SequenceEqual([10]) &&
           malformed.DefaultCommandWhitelist.SequenceEqual(["/echo"]) &&
           malformed.DefaultCommandBlacklist.SequenceEqual(["/logout"]) &&
           malformed.CustomChannels[0].Name == string.Empty,
        "invalid, duplicate, blank, and null default entries should normalize safely");
    Assert(!ConfigurationMigrator.MigrateAndNormalize(malformed),
        "boundary normalization should be idempotent");

    var isolatedNullDefaults = new Configuration
    {
        DefaultCommandWhitelist = null!,
        DefaultCommandBlacklist = null!,
        DefaultEnabledChannels = null!,
        CustomChannels = [new ChannelSetting { ChatType = 99, Name = null! }],
    };
    Assert(ConfigurationMigrator.MigrateAndNormalize(isolatedNullDefaults) &&
           isolatedNullDefaults.DefaultCommandWhitelist.Count == 0 &&
           isolatedNullDefaults.DefaultCommandBlacklist.SequenceEqual(["/sit", "/groundsit", "/lounge"]) &&
           isolatedNullDefaults.DefaultEnabledChannels.Count == 0 &&
           isolatedNullDefaults.CustomChannels[0].Name == string.Empty,
        "isolated null defaults and custom names should be repaired and reported as changed");
    Assert(!ConfigurationMigrator.MigrateAndNormalize(isolatedNullDefaults),
        "isolated null-default repair should be idempotent");

    var nullableSearch = new Reaction { Name = null!, TriggerPhrase = null!, EnabledChannels = null! };
    Assert(!PluginUiLogic.MatchesSearch(nullableSearch, "anything"),
        "search should tolerate null text before normalization");
    nullableSearch.Enabled = true;
    nullableSearch.Rx = new Regex("x");
    Assert(PluginUiLogic.GetStatus(nullableSearch) == ReactionUiStatus.InvalidTrigger,
        "status evaluation should tolerate malformed pre-normalized reactions");

    var transitionList = new List<Reaction> { new() { Name = "A" }, new() { Name = "B" }, new() { Name = "C" } };
    Assert(PluginUiLogic.TryDeleteReaction(transitionList, 0, out var afterFirst) && afterFirst == 0 &&
           transitionList[0].Name == "B",
        "deleting the first reaction should select its successor");
    Assert(PluginUiLogic.TryDeleteReaction(transitionList, 1, out var afterLast) && afterLast == 0 &&
           transitionList.Single().Name == "B",
        "deleting the last reaction should select the remaining predecessor");
    Assert(!PluginUiLogic.TryDeleteReaction(transitionList, -1, out _) &&
           !PluginUiLogic.TryDeleteReaction(transitionList, 5, out _),
        "invalid deletion transitions should leave the collection unchanged");

    Console.WriteLine("PASS configuration and transition boundaries");
}

static void RunDebugLogBufferTests()
{
    DebugLogBuffer.Clear();
    var revisionBeforeAdds = DebugLogBuffer.Revision;
    for (var index = 0; index < 505; index++)
        DebugLogBuffer.Add(index, $"display {index}", $"trigger {index}");
    var entries = DebugLogBuffer.Snapshot();
    Assert(entries.Length == 500, "logs UI should retain at most 500 entries");
    Assert(entries[0].ChatTypeId == 5 && entries[^1].ChatTypeId == 504,
        "logs UI should discard the oldest entries when full");
    Assert(DebugLogBuffer.Revision == revisionBeforeAdds + 505,
        "log revision should advance for every entry even after the buffer reaches its limit");

    var directory = Path.Combine(Path.GetTempPath(), $"PuppetMaster-LogTests-{Guid.NewGuid():N}");
    try
    {
        var exportPath = DebugLogBuffer.SaveSnapshot(directory, entries[..2]);
        var exported = File.ReadAllLines(exportPath);
        Assert(exported.Any(line => line == "# Entries: 2"), "log export should include its entry count");
        Assert(exported[^2] == "display 5" && exported[^1] == "display 6",
            "log export should preserve visible log order and text");
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
        var revisionBeforeClear = DebugLogBuffer.Revision;
        DebugLogBuffer.Clear();
        Assert(DebugLogBuffer.Revision == revisionBeforeClear + 1,
            "clearing logs should advance the revision used by auto-scroll");
    }
    Assert(DebugLogBuffer.Snapshot().Length == 0, "clearing logs should empty the UI buffer");
    Console.WriteLine("PASS debug log UI");
}

static void RunRetriggerQueueTests()
{
    var ignored = new BoundedRetriggerQueue<string>(3);
    Assert(ignored.Enqueue(ReactionExecutionPolicy.IgnoreWhileRunning, "A") == 0,
        "ignored retrigger should not count as an overload drop");
    Assert(ignored.Count == 0, "ignore policy should not queue a retrigger");

    var latest = new BoundedRetriggerQueue<string>(3);
    latest.Enqueue(ReactionExecutionPolicy.QueueLatestTrigger, "A");
    Assert(latest.TryPeek(out var observedLatest) && observedLatest == "A",
        "drainer should inspect the next retrigger without removing it");
    latest.Enqueue(ReactionExecutionPolicy.QueueLatestTrigger, "B");
    Assert(latest.Count == 1, "latest policy should keep one retrigger");
    Assert(latest.TryDequeue(out var latestItem) && latestItem == "B",
        "latest policy should keep the newest retrigger");

    var restart = new BoundedRetriggerQueue<string>(3);
    restart.Enqueue(ReactionExecutionPolicy.RestartImmediately, "A");
    restart.Enqueue(ReactionExecutionPolicy.RestartImmediately, "B");
    Assert(restart.Count == 1 && restart.TryDequeue(out var restartItem) && restartItem == "B",
        "restart-immediately should retain only the newest replacement");

    var every = new BoundedRetriggerQueue<string>(3);
    every.Enqueue(ReactionExecutionPolicy.QueueEveryTrigger, "A");
    every.Enqueue(ReactionExecutionPolicy.QueueEveryTrigger, "B");
    every.Enqueue(ReactionExecutionPolicy.QueueEveryTrigger, "C");
    Assert(every.Enqueue(ReactionExecutionPolicy.QueueEveryTrigger, "D") == 1,
        "bounded queue should report one dropped oldest retrigger");
    Assert(every.TryDequeue(out var first) && first == "B", "bounded queue should drop the oldest item");
    Assert(every.TryDequeue(out var second) && second == "C", "queue-every should preserve FIFO order");
    Assert(every.TryDequeue(out var third) && third == "D", "queue-every should retain the newest item");

    every.Enqueue(ReactionExecutionPolicy.QueueEveryTrigger, "E");
    every.Clear();
    Assert(every.Count == 0, "clearing a retrigger queue should remove all pending work");

    Console.WriteLine("PASS bounded retrigger queue");
}

static void RunRetriggerSchedulerTests()
{
    var gateOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var acquireStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var executed = new List<string>();
    var scheduler = new BoundedRetriggerScheduler<string>(
        16,
        async (_, cancellationToken) =>
        {
            acquireStarted.TrySetResult();
            await gateOpened.Task.WaitAsync(cancellationToken);
            return new CancellationTokenSource();
        },
        (item, lease) =>
        {
            lease.Dispose();
            executed.Add(item);
            return Task.CompletedTask;
        });

    var drainer = scheduler.Enqueue(
        ReactionExecutionPolicy.QueueLatestTrigger,
        "A",
        CancellationToken.None)!;
    acquireStarted.Task.GetAwaiter().GetResult();
    scheduler.Enqueue(ReactionExecutionPolicy.QueueLatestTrigger, "B", CancellationToken.None);
    Assert(scheduler.PendingCount == 1, "waiting latest scheduler should keep exactly one visible item");
    gateOpened.TrySetResult();
    drainer.GetAwaiter().GetResult();
    Assert(executed.SequenceEqual(["B"]),
        "latest scheduler should execute only the newest item that arrived while waiting");

    var restartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var restartAcquireStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var restartExecuted = new List<string>();
    var restartScheduler = new BoundedRetriggerScheduler<string>(
        16,
        async (_, cancellationToken) =>
        {
            restartAcquireStarted.TrySetResult();
            await restartGate.Task.WaitAsync(cancellationToken);
            return new CancellationTokenSource();
        },
        (item, lease) =>
        {
            lease.Dispose();
            restartExecuted.Add(item);
            return Task.CompletedTask;
        });
    var restartDrainer = restartScheduler.Enqueue(
        ReactionExecutionPolicy.RestartImmediately,
        "A",
        CancellationToken.None)!;
    restartAcquireStarted.Task.GetAwaiter().GetResult();
    restartScheduler.Enqueue(ReactionExecutionPolicy.RestartImmediately, "B", CancellationToken.None);
    restartScheduler.Enqueue(ReactionExecutionPolicy.RestartImmediately, "C", CancellationToken.None);
    Assert(restartScheduler.PendingCount == 1,
        "restart scheduler should expose only the newest waiting replacement");
    restartGate.TrySetResult();
    restartDrainer.GetAwaiter().GetResult();
    Assert(restartExecuted.SequenceEqual(["C"]),
        "restart scheduler should discard superseded replacements before execution");

    var neverOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var cancelledExecutions = 0;
    var cancellableScheduler = new BoundedRetriggerScheduler<string>(
        16,
        async (_, cancellationToken) =>
        {
            await neverOpen.Task.WaitAsync(cancellationToken);
            return new CancellationTokenSource();
        },
        (_, lease) =>
        {
            lease.Dispose();
            cancelledExecutions++;
            return Task.CompletedTask;
        });
    var cancelledDrainer = cancellableScheduler.Enqueue(
        ReactionExecutionPolicy.QueueEveryTrigger,
        "never",
        CancellationToken.None)!;
    cancellableScheduler.Cancel();
    cancelledDrainer.GetAwaiter().GetResult();
    Assert(cancellableScheduler.PendingCount == 0, "cancelling a scheduler should clear pending work");
    Assert(cancelledExecutions == 0, "cancelling while waiting should not execute pending work");
    var restartedDrainer = cancellableScheduler.Enqueue(
        ReactionExecutionPolicy.QueueEveryTrigger,
        "after cancel",
        CancellationToken.None)!;
    neverOpen.TrySetResult();
    restartedDrainer.GetAwaiter().GetResult();
    Assert(cancelledExecutions == 1, "scheduler should accept new work after cancellation");

    var fifoGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var fifoExecuted = new List<int>();
    var overflowDrops = 0;
    var fifoScheduler = new BoundedRetriggerScheduler<int>(
        16,
        async (_, cancellationToken) =>
        {
            await fifoGate.Task.WaitAsync(cancellationToken);
            return new CancellationTokenSource();
        },
        (item, lease) =>
        {
            lease.Dispose();
            fifoExecuted.Add(item);
            return Task.CompletedTask;
        },
        dropped => overflowDrops += dropped);
    Task? fifoDrainer = null;
    for (var item = 1; item <= 17; item++)
    {
        var startedDrainer = fifoScheduler.Enqueue(
            ReactionExecutionPolicy.QueueEveryTrigger,
            item,
            CancellationToken.None);
        fifoDrainer ??= startedDrainer;
    }
    Assert(fifoScheduler.PendingCount == 16,
        $"scheduler should include its waiting front item in the 16-item bound (actual {fifoScheduler.PendingCount})");
    Assert(overflowDrops == 1,
        $"scheduler should report one drop when its exact bound is exceeded (actual {overflowDrops})");
    fifoGate.TrySetResult();
    fifoDrainer!.GetAwaiter().GetResult();
    Assert(fifoExecuted.SequenceEqual(Enumerable.Range(2, 16)),
        "queue-every scheduler should drop the oldest and preserve FIFO order");

    Exception? reportedFailure = null;
    var reportedDiscarded = -1;
    var failureScheduler = new BoundedRetriggerScheduler<string>(
        16,
        (_, _) => throw new InvalidOperationException("scheduler test failure"),
        (_, lease) =>
        {
            lease.Dispose();
            return Task.CompletedTask;
        },
        reportFailure: (exception, discarded) =>
        {
            reportedFailure = exception;
            reportedDiscarded = discarded;
        });
    var failedDrainer = failureScheduler.Enqueue(
        ReactionExecutionPolicy.QueueEveryTrigger,
        "A",
        CancellationToken.None)!;
    failureScheduler.Enqueue(ReactionExecutionPolicy.QueueEveryTrigger, "B", CancellationToken.None);
    failedDrainer.GetAwaiter().GetResult();
    Assert(reportedFailure is InvalidOperationException,
        "unexpected scheduler failures should be reported");
    Assert(reportedDiscarded >= 1, "scheduler failure should report discarded pending work");
    Assert(failureScheduler.PendingCount == 0, "scheduler failure should clear its unusable backlog");

    Console.WriteLine("PASS bounded retrigger scheduler");
}

static void RunReactionVisualizerStateTests()
{
    Assert(ReactionVisualizerState.ResolveFinishedStatus(cancelled: false, reactionEnabled: false) ==
           VisualizerRunStatus.Completed,
        "completed visualizer runs should remain completed even if the reaction is disabled afterward");
    Assert(ReactionVisualizerState.ResolveFinishedStatus(cancelled: true, reactionEnabled: true) ==
           VisualizerRunStatus.Cancelled,
        "ordinary cancellations should remain visually distinct");
    Assert(ReactionVisualizerState.ResolveFinishedStatus(cancelled: true, reactionEnabled: false) ==
           VisualizerRunStatus.Disabled,
        "runs cancelled by disabling a reaction should use the neutral stopped state");

    ReactionVisualizerState.Reset();
    var runId = ReactionVisualizerState.Started(1, "Self-disabling reaction", "/puppetmaster off");
    ReactionVisualizerState.Finished(runId, cancelled: true, reactionEnabled: false);
    var snapshot = ReactionVisualizerState.Snapshot();
    Assert(snapshot.Active.Length == 0 && snapshot.Recent.Length == 1 &&
           snapshot.Recent[0].Status == VisualizerRunStatus.Disabled,
        "disabled run should move from its worker lane into visualizer history as stopped");
    ReactionVisualizerState.Reset();

    Console.WriteLine("PASS reaction visualizer state");
}

static void RunConfigurationUpgradeTransactionTests()
{
    var directory = Path.Combine(Path.GetTempPath(), $"PuppetMasterMigrationTests-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var sourceFixture = Path.Combine(AppContext.BaseDirectory, "TestConfigs", "PuppetMaster_v1.json");
        var originalBytes = File.ReadAllBytes(sourceFixture);
        var activePath = Path.Combine(directory, "PuppetMaster.json");
        File.WriteAllBytes(activePath, originalBytes);
        var configuration = DalamudJson.Load(File.ReadAllText(sourceFixture));
        var fixedTime = new DateTime(2026, 1, 2, 3, 4, 5, 6, DateTimeKind.Utc);

        var backupPath = ConfigurationUpgradeTransaction.Execute(
            activePath,
            configuration.Version,
            ConfigVersion.CURRENT,
            () => ConfigurationMigrator.MigrateAndNormalize(configuration),
            () => File.WriteAllText(activePath, DalamudJson.Save(configuration)),
            fixedTime);

        Assert(backupPath != null && File.Exists(backupPath), "v1 upgrade should create a recoverable backup");
        Assert(File.ReadAllBytes(backupPath!).SequenceEqual(originalBytes),
            "backup should be byte-for-byte identical to the original v1 file");
        var activeConfiguration = DalamudJson.Load(File.ReadAllText(activePath));
        Assert(activeConfiguration.Version == ConfigVersion.CURRENT,
            "successful transaction should save the migrated configuration at the current version");

        var collisionPath = Path.Combine(directory, "Collision.json");
        var collisionOriginal = "{\"Version\":1,\"Marker\":\"original\"}";
        File.WriteAllText(collisionPath, collisionOriginal);
        var expectedCollisionBackup = Path.Combine(
            directory,
            "Collision.v1.20260102030405006.backup.json");
        File.WriteAllText(expectedCollisionBackup, "existing backup");
        var collisionBackup = ConfigurationUpgradeTransaction.Execute(
            collisionPath,
            1,
            ConfigVersion.CURRENT,
            () => { },
            () => { },
            fixedTime);
        Assert(collisionBackup != null && collisionBackup != expectedCollisionBackup && File.Exists(collisionBackup),
            "backup name collision should pick a fresh name instead of failing the load");
        Assert(File.ReadAllText(expectedCollisionBackup) == "existing backup",
            "backup name collision should never overwrite an earlier backup");
        Assert(File.ReadAllText(collisionBackup!) == collisionOriginal,
            "collision backup should hold the original source");

        var failurePath = Path.Combine(directory, "MigrationFailure.json");
        var failureOriginal = "{\"Version\":1,\"Marker\":\"untouched\"}";
        File.WriteAllText(failurePath, failureOriginal);
        var failureSaved = false;
        string? reportedFailureBackup = null;
        AssertThrows<InvalidOperationException>(() => ConfigurationUpgradeTransaction.Execute(
                failurePath,
                1,
                ConfigVersion.CURRENT,
                () => throw new InvalidOperationException("simulated migration failure"),
                () => failureSaved = true,
                fixedTime.AddSeconds(1),
                backupCreated: path => reportedFailureBackup = path),
            "migration failure should propagate");
        Assert(!failureSaved, "migration failure should prevent save");
        Assert(File.ReadAllText(failurePath) == failureOriginal,
            "migration failure should leave the active source file untouched");
        Assert(Directory.GetFiles(directory, "MigrationFailure.v1.*.backup.json").Length == 1,
            "migration failure should still leave the original recoverable backup");
        Assert(reportedFailureBackup != null && File.Exists(reportedFailureBackup),
            "backup path should be reported before migration preparation begins");

        var saveFailurePath = Path.Combine(directory, "SaveFailure.json");
        var saveFailureOriginal = "{\"Version\":1,\"Marker\":\"save-failure-source\"}";
        File.WriteAllText(saveFailurePath, saveFailureOriginal);
        var savePreparationCompleted = false;
        string? reportedSaveFailureBackup = null;
        AssertThrows<IOException>(() => ConfigurationUpgradeTransaction.Execute(
                saveFailurePath,
                1,
                ConfigVersion.CURRENT,
                () => savePreparationCompleted = true,
                () => throw new IOException("simulated save failure"),
                fixedTime.AddSeconds(2),
                backupCreated: path => reportedSaveFailureBackup = path),
            "save failure should propagate");
        Assert(savePreparationCompleted, "save failure test should complete migration preparation first");
        Assert(File.ReadAllText(saveFailurePath) == saveFailureOriginal,
            "save failure before write should leave the active source untouched");
        Assert(reportedSaveFailureBackup != null && File.Exists(reportedSaveFailureBackup),
            "save failure should preserve and report the recoverable backup");
        Assert(File.ReadAllText(reportedSaveFailureBackup!) == saveFailureOriginal,
            "save-failure backup should remain byte-for-byte identical to the original source");

        Console.WriteLine("PASS configuration upgrade transaction");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void RunDalamudRoundTripTests()
{
    // H6: the user's default blacklist must load back exactly as saved, including shrunk and empty lists.
    var shrunk = new Configuration();
    shrunk.DefaultCommandBlacklist = ["/groundsit"];
    Assert(DalamudJson.Load(DalamudJson.Save(shrunk)).DefaultCommandBlacklist.SequenceEqual(["/groundsit"]),
        "a shrunk default blacklist should not regain the built-in defaults on load");
    var emptied = new Configuration();
    emptied.DefaultCommandBlacklist = [];
    Assert(DalamudJson.Load(DalamudJson.Save(emptied)).DefaultCommandBlacklist.Count == 0,
        "an emptied default blacklist should stay empty on load");
    Assert(new Configuration().DefaultCommandBlacklist.SequenceEqual(["/sit", "/groundsit", "/lounge"]),
        "a brand-new configuration should still start with the safe defaults");

    // H1: compiled regexes are runtime state. They must not be written, and an old file that has them must load
    // without them (so they're rebuilt with the match timeout from the current pattern text).
    var withRegex = new Configuration();
    var reaction = Reaction.CreateDefault();
    reaction.CustomPhrase = "hello";
    reaction.CustomRx = new Regex("(a+)+$", RegexOptions.None, TimeSpan.FromMilliseconds(250));
    reaction.Rx = new Regex("x", RegexOptions.None, TimeSpan.FromMilliseconds(250));
    withRegex.Reactions.Add(reaction);
    var saved = DalamudJson.Save(withRegex);
    Assert(!saved.Contains("\"Rx\"") && !saved.Contains("\"CustomRx\""), "compiled regexes should not be saved");

    var legacyJson = saved.Replace(
        "\"CustomPhrase\": \"hello\"",
        "\"CustomPhrase\": \"hello\", \"CustomRx\": { \"Pattern\": \"(a+)+$\", \"Options\": 0 }, \"Rx\": { \"Pattern\": \"(a+)+$\", \"Options\": 0 }");
    Assert(legacyJson != saved, "legacy regex fixture should have been built");
    var legacy = DalamudJson.Load(legacyJson);
    Assert(legacy.Reactions[0].CustomRx == null && legacy.Reactions[0].Rx == null,
        "a regex saved by an older version should be dropped on load, not revived without its timeout");
    Assert(legacy.Reactions[0].CustomPhrase == "hello", "the saved pattern text stays the source of truth");

    Console.WriteLine("PASS Dalamud serializer round trips");
}

static void RunWaitParsingTests()
{
    var culture = System.Globalization.CultureInfo.CurrentCulture;
    try
    {
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        Assert(ReactionCommandMatcher.TryParseWaitSeconds("1.5", out var seconds) && seconds == 1.5,
            "/wait should read a dot decimal the same on every client language");
        Assert(ReactionCommandMatcher.TryParseWaitSeconds("90", out seconds) && seconds == 60,
            "/wait should clamp to 60 seconds");
        Assert(ReactionCommandMatcher.TryParseWaitSeconds("-3", out seconds) && seconds == 0,
            "/wait should clamp negatives to zero");
        Assert(!ReactionCommandMatcher.TryParseWaitSeconds("NaN", out _) &&
               !ReactionCommandMatcher.TryParseWaitSeconds("Infinity", out _) &&
               !ReactionCommandMatcher.TryParseWaitSeconds("soon", out _),
            "/wait should reject NaN, infinity and words");
    }
    finally
    {
        System.Globalization.CultureInfo.CurrentCulture = culture;
    }
    Console.WriteLine("PASS /wait parsing");
}

static void RunCommandPolicyTests()
{
    var catalog = new CommandCatalog(
        [
            ["/shout", "/sh"],
            ["/echo", "/e"],
            ["/logout"],
            ["/action", "/ac"],
            ["/say", "/s"],
        ],
        [
            ["/dance"],
            ["/wave"],
        ]);

    Assert(catalog.Canonicalize("/SH") == "/shout", "aliases should resolve to their command, ignoring case");
    Assert(catalog.Canonicalize("/unknownthing") == "/unknownthing", "unknown commands should pass through lower-cased");
    Assert(catalog.Classify("/dance") == CommandKind.Emote, "emotes should classify as emotes");
    Assert(catalog.Classify("/sh") == CommandKind.Chat, "shout (by alias) should classify as chat");
    Assert(catalog.Classify("/ac") == CommandKind.Game, "action should classify as a game command");
    Assert(catalog.Classify("/hello", cmd => cmd == "/hello") == CommandKind.Plugin, "registered plugin commands should classify as plugin");
    Assert(catalog.Classify("/hello") == CommandKind.Unknown, "unregistered commands should be unknown");
    Assert(catalog.Classify("/logout") == CommandKind.Blocked && catalog.Classify("/xlsettings", _ => true) == CommandKind.Blocked,
        "logout and Dalamud's commands should classify as always blocked");

    // A German client: the row's own name plus its English name. The English block must still hold.
    var german = new CommandCatalog([["/abmelden", "/logout"], ["/sagen", "/say", "/s"]], []);
    Assert(german.Classify("/abmelden") == CommandKind.Blocked && german.Classify("/logout") == CommandKind.Blocked,
        "the always-blocked commands should be blocked by their client name too");
    Assert(german.Classify("/sagen") == CommandKind.Chat, "chat commands should be recognized by their client name");
    Assert(!CommandPolicy.IsAllowed(german.Canonicalize("/abmelden"), german.Classify("/abmelden"),
            german.CanonicalSet(["/abmelden"]), german.CanonicalSet([]), true, out _),
        "a localized logout should never run, even when listed and with any game command allowed");

    var none = catalog.CanonicalSet([]);
    bool Allowed(string command, IEnumerable<string> allow, IEnumerable<string> block, bool allowAll, Func<string, bool>? plugin = null) =>
        CommandPolicy.IsAllowed(catalog.Canonicalize(command), catalog.Classify(command, plugin),
            catalog.CanonicalSet(allow), catalog.CanonicalSet(block), allowAll, out _);

    Assert(Allowed("/dance", [], [], false), "emotes should be allowed without listing them");
    Assert(!Allowed("/dance", [], ["/dance"], false), "a blocked emote should stay blocked");
    Assert(!Allowed("/ac", [], [], false), "game commands need an allow entry by default");
    Assert(Allowed("/ac", ["/action"], [], false), "an allow entry should cover the command's aliases");
    Assert(Allowed("/ac", [], [], true), "allow-all should cover game commands");
    Assert(!Allowed("/sh", [], [], true), "allow-all should never cover chat commands");
    Assert(!Allowed("/sh", [], ["/shout"], true) && !Allowed("/shout", [], ["/sh"], false),
        "blocking any form of a command should block every form of it");
    Assert(Allowed("/sh", ["/shout"], [], false), "chat commands can still be allowed one by one");
    Assert(!Allowed("/hello", [], [], true, cmd => cmd == "/hello"), "allow-all should never cover plugin commands");
    Assert(Allowed("/hello", ["/hello"], [], false, cmd => cmd == "/hello"), "plugin commands can be allowed one by one");
    Assert(!Allowed("/logout", ["/logout"], [], true), "/logout should be blocked even when allowed");
    Assert(!Allowed("/puppetmaster", ["/puppetmaster"], [], true), "/puppetmaster should be blocked even when allowed");
    Assert(!Allowed("/xlplugins", ["/xlplugins"], [], true, _ => true), "/xl commands should be blocked even when allowed");
    Assert(!Allowed("/nonsense", [], [], true), "allow-all should not cover unknown commands");

    Assert(CommandPolicy.ConvertPlaceholders("at [t] and [me]") == "at <t> and <me>", "target placeholders should convert");
    Assert(CommandPolicy.ConvertPlaceholders("I am at [pos] [flag]") == "I am at [pos] [flag]",
        "location placeholders should stay literal");
    Assert(CommandPolicy.ConvertPlaceholders("[se.1] [2] [") == "[se.1] <2> [", "only safe placeholders convert, stray brackets stay");

    Assert(ReactionCommandMatcher.EscapeTriggerPhrase("please.do") == @"please\.do", "trigger phrases should be literal text");
    Assert(ReactionCommandMatcher.EscapeTriggerPhrase("hey (you|simon says") == @"hey\ \(you|simon\ says",
        "alternatives should survive escaping");
    var trigger = new Regex(@"(?i)\b(?:" + ReactionCommandMatcher.EscapeTriggerPhrase("please.do") + @")\s+(?:\((.*?)\)|(\w+))");
    Assert(!trigger.IsMatch("pleaseXdo wave") && trigger.IsMatch("please.do wave"), "a dot in the trigger should be literal");

    Console.WriteLine("PASS command policy");
}

static void RunSenderFilterTests()
{
    var stranger = new SenderInfo("Some Body", "Ultros", false, false, false, false);
    var friend = stranger with { IsFriend = true };
    var fc = stranger with { IsFreeCompany = true };
    var party = stranger with { IsParty = true };

    var defaults = new SenderFilter();
    Assert(!defaults.Allows(stranger), "new filters should not allow strangers");
    Assert(defaults.Allows(friend) && defaults.Allows(fc) && defaults.Allows(party), "new filters should allow friends, FC and party");
    Assert(!defaults.Allows(SenderInfo.Unknown), "an unknown sender should only pass Anyone");
    Assert(SenderFilter.AnyoneFilter().Allows(SenderInfo.Unknown), "Anyone should allow everyone");

    var named = new SenderFilter { Friends = false, FreeCompany = false, Party = false, Named = ["some body@ultros", "Other Person"] };
    Assert(named.Allows(stranger), "Name@World entries should match case-insensitively");
    Assert(!named.Allows(stranger with { World = "Cactuar" }), "Name@World should not match another world");
    Assert(named.Allows(new SenderInfo("Other Person", "Cactuar", false, false, false, false)), "a bare name should match any world");

    var clone = named.Clone();
    clone.Named.Add("Third One");
    Assert(named.Named.Count == 2, "Clone should copy the named list");
    Assert(defaults.Describe() == "Friends, Free Company, Party", "Describe should list the groups");
    Assert(SenderFilter.AnyoneFilter().Describe() == "Anyone", "Describe should say Anyone");

    Console.WriteLine("PASS sender filter");
}

static void RunRateLimiterTests()
{
    var limiter = new CommandRateLimiter(3, TimeSpan.FromSeconds(1));
    var now = 1_000_000 * System.Diagnostics.Stopwatch.Frequency;
    Assert(limiter.Reserve(now) == TimeSpan.Zero && limiter.Reserve(now) == TimeSpan.Zero && limiter.Reserve(now) == TimeSpan.Zero,
        "a burst of three should go out at once");
    var fourth = limiter.Reserve(now);
    var fifth = limiter.Reserve(now);
    Assert(Math.Abs(fourth.TotalSeconds - 1) < 0.001 && Math.Abs(fifth.TotalSeconds - 2) < 0.001,
        "after the burst, sends should be one second apart");
    var later = now + 60 * System.Diagnostics.Stopwatch.Frequency;
    Assert(limiter.Reserve(later) == TimeSpan.Zero, "credit should come back after a quiet minute");
    Assert(limiter.Reserve(later) == TimeSpan.Zero && limiter.Reserve(later) == TimeSpan.Zero && limiter.Reserve(later) > TimeSpan.Zero,
        "credit should never exceed the burst size");

    var skipper = new CommandRateLimiter(2, TimeSpan.FromSeconds(1));
    Assert(skipper.TryAcquire(now) && skipper.TryAcquire(now), "TryAcquire should take the free burst");
    Assert(!skipper.TryAcquire(now), "TryAcquire should refuse rather than wait once the burst is spent");
    Assert(skipper.TryAcquire(now + System.Diagnostics.Stopwatch.Frequency), "TryAcquire should succeed once a send is free again");
    Console.WriteLine("PASS command rate limiter");
}
