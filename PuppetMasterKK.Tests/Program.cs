using System.Text.RegularExpressions;
using PuppetMasterKK;

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

Run("PuppetMaster_v3.json", configuration =>
{
    Assert(configuration.Version == ConfigVersion.CURRENT, "v3 should migrate to the current version");
    Assert(configuration.Reactions.Count == 2, "v3 reactions should all come over");
    var ami = configuration.Reactions[0];
    Assert(ami.Name == "Ami" && ami.TriggerPhrase == "Ami" && ami.Enabled && ami.EnabledChannels.SequenceEqual([13]),
        "a v3 reaction should keep its name, phrase, state and channels");
    Assert(ami.Rx == null, "a saved compiled regex should be dropped on load");
    Assert(ami.Senders.Anyone && configuration.Reactions[1].Senders.Anyone, "v3 reactions should react to anyone, as before");
    Assert(ami.ExecutionPolicy == ReactionExecutionPolicy.IgnoreWhileRunning &&
           configuration.Reactions[1].ExecutionPolicy == ReactionExecutionPolicy.QueueLatestTrigger &&
           configuration.Reactions[1].CooldownSeconds == 5,
        "v3 policies and cooldowns should be kept");
    Assert(configuration.Reactions[1].ProgressNotifications == ReactionNotificationSetting.Enabled &&
           configuration.Reactions[1].SuppressedNotifications == ReactionNotificationSetting.Disabled,
        "v3 notification choices should be kept");
    Assert(configuration.IgnoreOwnMessages, "v3 configs should ignore your own messages from now on");
    Assert(configuration.EmoteReplies is { Enabled: false, PerPlayerCooldownSeconds: 10 } &&
           configuration.EmoteReplies.BlockedEmotes.SequenceEqual(["/sit", "/groundsit", "/lounge", "/doze"]),
        "v3 configs should get emote replies off, with the defaults");
    Assert(configuration.ShowReactionNotifications && !configuration.ShowSuppressedReactionNotifications,
        "v3 global notification choices should be kept");
});

var reviewed = DalamudJson.Load(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestConfigs", "PuppetMaster_v3.json")));
ConfigurationMigrator.MigrateAndNormalize(reviewed);
Assert(reviewed.ReviewAfterMigration.SequenceEqual(["Ami", "Wave"]),
    "every trigger the upgrade opens to anyone should be listed for review");

var repaired = new Configuration();
repaired.EmoteReplies.PerPlayerCooldownSeconds = 0;
repaired.EmoteReplies.BlockedEmotes = null!;
repaired.Reactions.Add(new Reaction { Senders = null! });
ConfigurationMigrator.MigrateAndNormalize(repaired);
Assert(repaired.EmoteReplies.PerPlayerCooldownSeconds == EmoteReplySettings.MinimumCooldownSeconds &&
       repaired.EmoteReplies.BlockedEmotes != null && repaired.Reactions[0].Senders.Anyone,
    "repair should raise a too-short reply wait, restore lists and give a reaction without senders Anyone");

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

// Someone else's hand-edited (or corrupted) file: nulls everywhere, out-of-range numbers, unknown enum values and
// "$type" entries. It must load, repair to safe values, and stay that way.
Run("PuppetMaster_v5_hostile.json", configuration =>
{
    Assert(configuration.Reactions.Count == 2, "null trigger entries should be dropped");
    var first = configuration.Reactions[0];
    Assert(first.Name == string.Empty && first.TriggerPhrase == Reaction.DefaultTriggerPhrase,
        "null trigger text should be repaired");
    Assert(first.CooldownSeconds is >= 0 and <= 86400 && configuration.Reactions[1].CooldownSeconds == 0,
        "huge and negative cooldowns should be clamped");
    Assert(first.ExecutionPolicy == ReactionExecutionPolicy.QueueEveryTrigger, "an unknown repeat policy should be reset");
    Assert(first.EnabledChannels.SequenceEqual([13]), "out-of-range and duplicate channels should be dropped");
    Assert(first.CommandWhitelist.SequenceEqual(["/logout"]) && first.CommandBlacklist.Count == 0,
        "null, blank and duplicate commands should be dropped, and a null list repaired");
    Assert(!first.Senders.Anyone && first.Senders.Named.SequenceEqual(["Nova Ral'veth@Exodus"]),
        "null and blank player names should be dropped from a sender filter");
    Assert(first.Protections.OpenChat.SequenceEqual(["say"]) && first.Protections.OpenRisky.Count == 0 &&
           first.Protections.OpenPlugins.Count == 0,
        "null entries in the protection lists should be dropped and null lists repaired");
    Assert(first.Protections.IsOpen(CommandKind.Chat, "say") && !first.Protections.IsOpen(CommandKind.Chat, "tell") &&
           !first.Protections.IsOpen(CommandKind.Plugin, null),
        "repaired protections should still work: only the listed group is open");
    var second = configuration.Reactions[1];
    Assert(second.Senders.Anyone && second.Protections.Chat && second.Protections.Risky && second.Protections.Plugins,
        "a trigger with no senders keeps reacting to anyone (as before filters existed), with every protection on");
    Assert(configuration.DefaultProtections.OpenChat.Count == 0 && configuration.DefaultCommandWhitelist.Count == 0 &&
           configuration.DefaultEnabledChannels.Count == 0,
        "the defaults for new triggers should be repaired too");
    Assert(configuration.CustomChannels.Count == 1 && configuration.CustomChannels[0].ChatType == 77 &&
           configuration.CustomChannels[0].Name == string.Empty,
        "null and out-of-range custom channels should be dropped");
    Assert(configuration.MaxRegexLength > 0, "a negative pattern length limit should be reset");
    Assert(Enum.IsDefined(configuration.Accent), "an unknown accent color should be reset");

    var replies = configuration.EmoteReplies;
    Assert(replies.Senders != null && !replies.Senders.Anyone && replies.Senders.Named != null,
        "missing emote reply senders should get the safe default, not Anyone");
    Assert(replies.PerPlayerCooldownSeconds >= EmoteReplySettings.MinimumCooldownSeconds,
        "a negative reply wait should be raised to the minimum");
    Assert(replies.BlockedEmotes.SequenceEqual(["/sit"]), "null and duplicate blocked emotes should be dropped");
    Assert(replies.Overrides.Count == 1 && replies.Overrides[0].When == "/dote" && replies.Overrides[0].Reply == "/joy",
        "null overrides, and overrides with no emote to match, should be dropped");
    string Canon(string command) => command.Trim().ToLowerInvariant();
    Assert(EmoteReplySettings.ReplyFor(replies.Overrides, "/dote", Canon) == "/joy" &&
           EmoteReplySettings.ReplyFor(replies.Overrides, "/wave", Canon) == "/wave",
        "an override with no emote should never match");

    var follow = configuration.Follow;
    Assert(follow.CallNames == string.Empty && follow.FollowWords.Length > 0 && follow.StopWords.Length > 0 &&
           follow.ComeWords != null && follow.NotNearbyMessage != null,
        "null Follow mode words should be repaired");
    Assert(follow.Channels.SequenceEqual([13]), "Follow mode channels should be deduplicated and kept in range");
    Assert(follow.Senders.Named != null && follow.NeverFrom.Count == 0 && follow.OnlyFollow.Count == 0 &&
           follow.NeverFollow.Count == 0 && follow.StopCommands.SequenceEqual(["/echo stopped"]),
        "Follow mode lists should be repaired, without null or blank entries");
    Assert(FollowParser.Parse("Ami follow me", follow.CallNames, follow.FollowWords, follow.StopWords, follow.ComeWords!).Kind ==
           FollowRequestKind.None,
        "with no call name, repaired Follow settings should take no requests (and not throw)");

    var mimic = configuration.Mimic;
    Assert(mimic.CallNames != null && mimic.MimicWords != null && mimic.StopWords != null && mimic.NotNearbyMessage != null &&
           mimic.Channels != null && mimic.Senders?.Named != null && mimic.NeverFrom != null && mimic.OnlyMimic.Count == 0 &&
           mimic.NeverMimic != null,
        "null Mimic settings should be repaired");
    Assert(mimic.DelaySeconds == MimicSettings.MaxDelaySeconds && mimic.RepeatGuardSeconds == 0f,
        "a huge delay and a negative repeat guard should be clamped");
});

// Per-person limits, choices and final actions from a hand-edited (or hostile) file.
Run("PuppetMaster_v6_hostile.json", configuration =>
{
    var first = configuration.Reactions[0];
    Assert(first.PerSenderCooldownSeconds == Reaction.MaxPerSenderCooldownSeconds && first.OneWaitingPerSender,
        "a huge per-person cooldown should be clamped");
    Assert(first.ChoiceMode == ChoiceMode.Off, "an unknown choice mode should be turned off");
    Assert(first.Choices.Count == Reaction.MaxChoices && first.Choices.All(choice => choice.Word != null && choice.Commands != null) &&
           first.Choices[1] is { Word: "hug", Commands: "/hug" } && first.Choices[1].GetType() == typeof(ReactionChoice),
        "null choices should be dropped, null text repaired, \"$type\" ignored and the list cut to the limit");
    Assert(first.FinalCommands.SequenceEqual(["/echo one", "/echo three", "/echo four", "/echo five", "/echo six"]),
        "blank and multi-line final lines should be dropped, and at most five kept");
    Assert(first.FinalWhen == FinalActionWhen.AfterEachRun, "an unknown final-action timing should be reset");
    var second = configuration.Reactions[1];
    Assert(second.PerSenderCooldownSeconds == 0 && second.Choices.Count == 0 && second.FinalCommands.Count == 0 &&
           second.FinalWhen == FinalActionWhen.WhenNothingWaiting && !ChoiceSelector.IsActive(second),
        "null lists should be repaired, and By word with no choices does nothing");
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
RunActivityCounterTests();
RunPracticeAndTestAllTests();
RunConfigurationUpgradeTransactionTests();
RunDalamudRoundTripTests();
RunWaitParsingTests();
RunCommandPolicyTests();
RunSenderFilterTests();
RunRateLimiterTests();
RunFollowTests();
RunCommandPolicyBypassTests();
RunHostileCaptureTests();
RunHostileConfigTests();
RunHostileFollowTests();
RunPerSenderLimitTests();
RunChoiceTests();
RunFinalActionTests();
RunV6MigrationTests();

Console.WriteLine("All PuppetMasterKK tests passed.");
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

    reaction.UseRegex = true;
    Assert(reaction.UseRegex && ReactionCommandMatcher.SelectPattern(reaction) == null,
        "regex toggle should select only the empty custom pattern without throwing");
    reaction.UseRegex = false;
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
    Assert(PluginUiLogic.GetExecutionPolicyDescription(ReactionExecutionPolicy.QueueEveryTrigger)
               .Contains("up to 16 can wait") &&
           PluginUiLogic.GetExecutionPolicyDescription(ReactionExecutionPolicy.QueueLatestTrigger)
               .Contains("newest request") &&
           PluginUiLogic.GetExecutionPolicyDescription(ReactionExecutionPolicy.RestartImmediately)
               .Contains("starts over with the new request") &&
           PluginUiLogic.GetExecutionPolicyDescription(ReactionExecutionPolicy.IgnoreWhileRunning)
               .Contains("Ignores the new message"),
        "repeat-behavior choices should use clear user-facing descriptions");
    Assert(PluginUiLogic.GetCooldownDescription(ReactionExecutionPolicy.RestartImmediately)
               .Contains("Cooldown doesn't apply") &&
           PluginUiLogic.GetCooldownDescription(ReactionExecutionPolicy.QueueLatestTrigger)
               .Contains("minimum time between runs"),
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

    var directory = Path.Combine(Path.GetTempPath(), $"PuppetMasterKK-LogTests-{Guid.NewGuid():N}");
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

static void RunActivityCounterTests()
{
    ReactionVisualizerState.Reset();
    Assert(ReactionVisualizerState.ResolveFinishedStatus(cancelled: true, reactionEnabled: true, interrupted: true) ==
           VisualizerRunStatus.Interrupted, "a run stopped by Restart immediately should be Interrupted, not Stopped");
    Assert(ReactionVisualizerState.ResolveFinishedStatus(cancelled: false, reactionEnabled: true, interrupted: true) ==
           VisualizerRunStatus.Completed, "a run that finished is Completed even if a restart was asked for late");

    var done = ReactionVisualizerState.Started(7, "Wave", "/wave");
    ReactionVisualizerState.Finished(done, cancelled: false, reactionEnabled: true);
    var stopped = ReactionVisualizerState.Started(7, "Wave", "/wave");
    ReactionVisualizerState.Finished(stopped, cancelled: true, reactionEnabled: true);
    var interrupted = ReactionVisualizerState.Started(7, "Wave", "/wave");
    ReactionVisualizerState.Finished(interrupted, cancelled: true, reactionEnabled: true, interrupted: true);
    ReactionVisualizerState.Count(7, VisualizerCounter.IgnoredBusy);
    ReactionVisualizerState.Count(7, VisualizerCounter.IgnoredCooldown, 2);
    ReactionVisualizerState.Count(7, VisualizerCounter.BlockedLines);
    ReactionVisualizerState.Count(7, VisualizerCounter.TimedOut);
    ReactionVisualizerState.Count(7, VisualizerCounter.DiscardedFull, 0);
    ReactionVisualizerState.Count(7, VisualizerCounter.DiscardedFull, -5);
    ReactionVisualizerState.Count(8, VisualizerCounter.IgnoredBusy, 4);
    var counts = ReactionVisualizerState.Counters(7);
    Assert(counts.Started == 3 && counts.Completed == 1 && counts.Stopped == 1 && counts.Interrupted == 1,
        $"runs should be counted by how they ended ({counts})");
    Assert(counts.IgnoredBusy == 1 && counts.IgnoredCooldown == 2 && counts.Ignored == 3,
        "ignored requests should be split into busy and cooldown");
    Assert(counts.BlockedLines == 1 && counts.TimedOut == 1, "blocked lines and timeouts should be counted");
    Assert(counts.DiscardedFull == 0, "zero or negative amounts should not change a counter");
    Assert(ReactionVisualizerState.Counters(99) == default, "a trigger with no activity should have zero counts");
    Assert(ReactionVisualizerState.TotalCounters().Ignored == 7, "totals should add up every trigger");
    Assert(ReactionVisualizerState.Snapshot().Recent[0].Status == VisualizerRunStatus.Interrupted,
        "Recent should show an interrupted run as Interrupted");

    // Queue latest: the waiting request replaced by a newer one is listed in Recent as Replaced.
    ReactionVisualizerState.QueuedRun(7, "Wave", "/wave 1", ReactionExecutionPolicy.QueueLatestTrigger);
    ReactionVisualizerState.QueuedRun(7, "Wave", "/wave 2", ReactionExecutionPolicy.QueueLatestTrigger);
    var snapshot = ReactionVisualizerState.Snapshot();
    Assert(snapshot.Queued.Length == 1 && snapshot.Queued[0].Command == "/wave 2", "only the newest request should wait");
    Assert(snapshot.Recent[0].Status == VisualizerRunStatus.Replaced && snapshot.Recent[0].Command == "/wave 1",
        "the replaced request should show as Replaced");
    ReactionVisualizerState.ClearQueued(7);
    Assert(ReactionVisualizerState.Snapshot().Recent[0].Command == "/wave 1",
        "a plain Stop should not list the dropped request as Replaced");

    // The scheduler reports Queue latest replacements separately from Queue every overflow.
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var acquiring = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var executed = new List<string>();
    var scheduler = new BoundedRetriggerScheduler<string>(
        16,
        async (_, token) =>
        {
            acquiring.TrySetResult();
            await gate.Task.WaitAsync(token);
            return new CancellationTokenSource();
        },
        (item, lease) =>
        {
            lease.Dispose();
            executed.Add(item);
            return Task.CompletedTask;
        },
        dropped => ReactionVisualizerState.Count(7, VisualizerCounter.DiscardedFull, dropped),
        reportReplaced: replaced => ReactionVisualizerState.Count(7, VisualizerCounter.Replaced, replaced));
    var drainer = scheduler.Enqueue(ReactionExecutionPolicy.QueueLatestTrigger, "A", CancellationToken.None)!;
    acquiring.Task.GetAwaiter().GetResult();
    scheduler.Enqueue(ReactionExecutionPolicy.QueueLatestTrigger, "B", CancellationToken.None);
    scheduler.Enqueue(ReactionExecutionPolicy.QueueLatestTrigger, "C", CancellationToken.None);
    gate.TrySetResult();
    drainer.GetAwaiter().GetResult();
    counts = ReactionVisualizerState.Counters(7);
    Assert(executed.SequenceEqual(["C"]) && counts.Replaced == 2 && counts.DiscardedFull == 0,
        $"Queue latest replacements should be counted as Replaced, not Discarded ({counts})");

    var neverOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var stuck = new BoundedRetriggerScheduler<string>(
        16,
        async (_, token) =>
        {
            await neverOpen.Task.WaitAsync(token);
            return new CancellationTokenSource();
        },
        (_, lease) =>
        {
            lease.Dispose();
            return Task.CompletedTask;
        });
    var stuckDrainer = stuck.Enqueue(ReactionExecutionPolicy.QueueEveryTrigger, "A", CancellationToken.None)!;
    stuck.Enqueue(ReactionExecutionPolicy.QueueEveryTrigger, "B", CancellationToken.None);
    Assert(stuck.Cancel() == 2, "cancelling should report how many waiting requests it dropped");
    stuckDrainer.GetAwaiter().GetResult();
    Assert(stuck.Cancel() == 0, "cancelling an empty scheduler drops nothing");

    // Practice runs keep what they would have sent, bounded.
    var practiceRun = ReactionVisualizerState.Started(9, "Dance", "/dance", practice: true);
    for (var i = 0; i < 1000; i++)
        ReactionVisualizerState.WouldSend(practiceRun, $"/echo {i}");
    var active = ReactionVisualizerState.Snapshot().Active.Single(run => run.Id == practiceRun);
    Assert(active.Practice && active.WouldSend.Length == 32 && active.WouldSend[0] == "/echo 0",
        "a practice run should keep at most 32 would-send lines");
    ReactionVisualizerState.Finished(practiceRun, cancelled: false, reactionEnabled: true);
    Assert(ReactionVisualizerState.Snapshot().Recent[0] is { Practice: true, WouldSend.Length: 32 },
        "a finished practice run should keep its would-send lines in Recent");
    ReactionVisualizerState.WouldSend(practiceRun, "/late");
    Assert(ReactionVisualizerState.Snapshot().Recent[0].WouldSend.Length == 32, "a finished run takes no more lines");

    ReactionVisualizerState.ResetCounters();
    Assert(ReactionVisualizerState.TotalCounters() == default, "Clear should reset every counter");
    Assert(ReactionVisualizerState.Snapshot().Recent.Length > 0, "resetting counters should keep the run history");
    ReactionVisualizerState.Count(7, VisualizerCounter.Started);
    ReactionVisualizerState.Reset();
    Assert(ReactionVisualizerState.TotalCounters() == default && ReactionVisualizerState.Snapshot().Recent.Length == 0,
        "a full reset (plugin start) should clear counters and history");

    Console.WriteLine("PASS activity counters");
}

static void RunPracticeAndTestAllTests()
{
    var configuration = new Configuration { PracticeMode = true };
    var saved = DalamudJson.Save(configuration);
    Assert(!saved.Contains("PracticeMode"), "practice mode should never be saved");
    Assert(!DalamudJson.Load(saved).PracticeMode, "practice mode should be off after a reload");
    var hostile = DalamudJson.Load("{\"Version\":" + ConfigVersion.CURRENT + ",\"PracticeMode\":true}");
    Assert(!hostile.PracticeMode, "a config file can't turn practice mode on");

    static bool Check(Reaction reaction, string command, bool templateWait, out string reason)
    {
        var allowed = command != "/logout";
        reason = allowed ? "allowed" : "never runs";
        return allowed;
    }
    static bool IsEmote(string command) => command is "/wave" or "/dance";

    var wave = new Reaction
    {
        Name = "Wave",
        Enabled = true,
        MotionOnly = true,
        UseRegex = true,
        CustomRx = new Regex(@"^please (\w+)(.*)$", RegexOptions.None, TimeSpan.FromMilliseconds(250)),
        ReplaceMatch = "/$1\n/echo$2",
        EnabledChannels = [10],
        Senders = new SenderFilter { Anyone = false, Friends = true, FreeCompany = false, Party = false, Named = ["Nova Ral'veth@Exodus"] },
    };
    var preview = PluginUiLogic.BuildPreview(wave, "please wave hi", IsEmote, Check);
    Assert(preview.Status == PreviewStatus.Matched && preview.Lines.Count == 2, "a match should list each command line");
    Assert(preview.Lines[0] is { Command: "/wave motion", Allowed: true, Reason: "Allowed" },
        $"hide emote text and the reason should show as they would run ({preview.Lines[0]})");
    Assert(PluginUiLogic.BuildPreview(wave, "hello", IsEmote, Check).Status == PreviewStatus.NoMatch, "no match should say so");
    Assert(PluginUiLogic.BuildPreview(wave, "  ", IsEmote, Check).Status == PreviewStatus.Empty, "an empty message is not tested");
    var injected = PluginUiLogic.BuildPreview(wave, "please wave x\n/logout", IsEmote, Check);
    Assert(injected.Lines.Count == 2 && injected.Lines.All(line => line.Allowed),
        "a line break in the message must not add a command line");
    var logout = PluginUiLogic.BuildPreview(wave, "please logout", IsEmote, Check);
    Assert(!logout.Lines[0].Allowed && logout.Lines[0].Reason == "Never runs", "a blocked line should say why");

    var slow = new Reaction
    {
        Name = "Slow",
        Enabled = true,
        UseRegex = true,
        CustomRx = new Regex("^(a+)+$", RegexOptions.None, TimeSpan.FromMilliseconds(50)),
        ReplaceMatch = "/wave",
        EnabledChannels = [10],
        Senders = SenderFilter.AnyoneFilter(),
    };
    var timedOut = PluginUiLogic.BuildPreview(slow, new string('a', 5000) + "!", IsEmote, Check);
    Assert(timedOut.Status == PreviewStatus.Error, "a pattern that times out should show an error, not hang or throw");

    var off = new Reaction { Name = "Off", Enabled = false, Rx = new Regex("please"), EnabledChannels = [10], Senders = SenderFilter.AnyoneFilter() };
    var party = new Reaction { Name = "Party only", Enabled = true, Rx = new Regex("please"), EnabledChannels = [14], Senders = SenderFilter.AnyoneFilter() };
    var none = new Reaction { Name = "Other", Enabled = true, Rx = new Regex("goodbye"), EnabledChannels = [10], Senders = SenderFilter.AnyoneFilter() };
    var noPattern = new Reaction { Name = "Broken", Enabled = true, EnabledChannels = [10], Senders = SenderFilter.AnyoneFilter() };
    List<Reaction> reactions = [wave, off, party, none, noPattern];

    var stranger = PluginUiLogic.TestSender(0, "");
    var results = PluginUiLogic.TestAllTriggers(reactions, "please wave", 10, stranger, IsEmote, Check);
    Assert(results.Select(result => result.Index).SequenceEqual([0, 1, 2]), "only triggers whose pattern matches are listed");
    Assert(results[0].Outcome == TriggerTestOutcome.SenderNotAllowed, "a stranger can't set off a Friends-only trigger");
    Assert(results[1].Outcome == TriggerTestOutcome.Off, "a trigger that's off says so");
    Assert(results[2].Outcome == TriggerTestOutcome.NotOnChannel, "a trigger on another channel says so");

    var friend = PluginUiLogic.TestSender(1, "");
    Assert(PluginUiLogic.TestAllTriggers(reactions, "please wave", 10, friend, IsEmote, Check)[0].Outcome == TriggerTestOutcome.Fires,
        "a friend sets off the Friends trigger");
    var named = PluginUiLogic.TestSender(0, " nova ral'veth @ exodus ");
    Assert(named is { Name: "nova ral'veth", World: "exodus", IsFriend: false } &&
           PluginUiLogic.TestAllTriggers(reactions, "please wave", 10, named, IsEmote, Check)[0].Outcome == TriggerTestOutcome.Fires,
        "a named player (any case) sets off a trigger that lists them");
    Assert(PluginUiLogic.TestSender(3, null) is { IsParty: true, IsFriend: false, IsFreeCompany: false, Name: "" },
        "sender kinds map to one group each");
    Assert(PluginUiLogic.TestAllTriggers(reactions, "", 10, stranger, IsEmote, Check).Count == 0, "an empty message tests nothing");

    Console.WriteLine("PASS practice mode and test all triggers");
}

static void RunConfigurationUpgradeTransactionTests()
{
    var directory = Path.Combine(Path.GetTempPath(), $"PuppetMasterKK-MigrationTests-{Guid.NewGuid():N}");
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

    Assert(catalog.Classify("/pmkk") == CommandKind.Blocked && catalog.Classify("/PuppetMasterKK") == CommandKind.Blocked &&
           catalog.Classify("/puppetmaster") == CommandKind.Blocked,
        "this plugin's commands (and the old plugin's) should always be blocked");
    Assert(!Allowed("/follow", ["/follow"], [], true) && catalog.Classify("/follow") == CommandKind.FollowOnly,
        "triggers should never follow, even when /follow is listed: that is Follow mode's job");
    bool Unprotected(string command, IEnumerable<string> block, Func<string, bool>? plugin = null) =>
        CommandPolicy.IsAllowed(catalog.Canonicalize(command), catalog.Classify(command, plugin), catalog.CanonicalSet([]),
            catalog.CanonicalSet(block), false, out _, noProtections: true);
    Assert(Unprotected("/logout", []) && Unprotected("/sh", []) && Unprotected("/hello", [], _ => true) &&
           Unprotected("/pmkk", []) && Unprotected("/nonsense", []),
        "a trigger without protections should run everything, unlisted");
    Assert(Unprotected("/sh", ["/shout"]) && !Unprotected("/follow", []),
        "without protections, the Blocked list is off too, but /follow stays Follow mode's");
    var chatOff = new ProtectionSettings();
    chatOff.OpenChat.Add("say");
    chatOff.OpenRisky.Add("teleport");
    chatOff.OpenPlugins.Add("Lifestream");
    bool Open(ProtectionSettings p, string command, string? owner = null)
    {
        var kind = catalog.Classify(command, owner != null ? _ => true : null);
        var canonical = catalog.Canonicalize(command);
        return CommandPolicy.IsAllowed(canonical, kind, catalog.CanonicalSet([]), catalog.CanonicalSet([]), false, out _,
            open: p.IsOpen(kind, kind == CommandKind.Plugin ? owner : catalog.GroupOf(canonical)));
    }
    Assert(catalog.GroupOf(catalog.Canonicalize("/s")) == "say" && catalog.GroupOf(catalog.Canonicalize("/cwl3")) == "cwls" &&
           catalog.GroupOf(catalog.Canonicalize("/tp")) == "teleport" && catalog.Classify("/hotbar") == CommandKind.Sensitive,
        "chat and risky commands should map to their protection groups");
    Assert(catalog.GroupOf(catalog.Canonicalize("/dice")) == "dice" && catalog.GroupOf(catalog.Canonicalize("/random")) == "random" &&
           catalog.GroupOf(catalog.Canonicalize("/qchat")) == "quickchat" && catalog.Classify("/random") == CommandKind.Chat,
        "dice, random and quick chat should be protected chat commands");
    Assert(!CommandPolicy.IsAllowed(catalog.Canonicalize("/random"), catalog.Classify("/random"), catalog.CanonicalSet([]),
               catalog.CanonicalSet([]), true, out _) &&
           Open(new ProtectionSettings { OpenChat = ["random"] }, "/random") && !Open(new ProtectionSettings { OpenChat = ["random"] }, "/dice"),
        "\"Any game command\" shouldn't cover /random; unticking it alone should open only it");
    Assert(Open(chatOff, "/s") && !Open(chatOff, "/sh") && Open(chatOff, "/tp") && !Open(chatOff, "/leave") &&
           Open(chatOff, "/li", "Lifestream") && !Open(chatOff, "/glamour", "Glamourer"),
        "an unticked channel, risky group or plugin should run unlisted; ticked ones still need Allowed");
    var masterOff = new ProtectionSettings { Chat = false };
    Assert(Open(masterOff, "/sh") && Open(masterOff, "/cwl1") && !Open(masterOff, "/tp") && !Open(masterOff, "/glamour", "Glamourer"),
        "switching off chat protection should open every chat command, and nothing else");
    Assert(!Open(new ProtectionSettings { Chat = false, Risky = false, Plugins = false }, "/logout") &&
           !Open(new ProtectionSettings { Chat = false, Risky = false, Plugins = false }, "/follow"),
        "switched-off protections never open /logout or /follow");
    Assert(!CommandPolicy.IsAllowed(catalog.Canonicalize("/s"), CommandKind.Chat, catalog.CanonicalSet([]), catalog.CanonicalSet(["/say"]),
            false, out _, open: true),
        "a blocked command stays blocked even with its protection off");
    Assert(CommandPolicy.IsWaitAllowed(catalog, false, catalog.CanonicalSet([]), catalog.CanonicalSet(["/wait"]), out _, noProtections: true),
        "without protections, a sender's /wait runs too, even when blocked");
    Assert(!Allowed("/pmkk", ["/pmkk"], [], true) && !Allowed("/puppetmasterkk", ["/puppetmasterkk"], [], true),
        "this plugin's commands should never run, even when listed and with any game command allowed");

    var travel = new CommandCatalog([["/teleport", "/tp"], ["/partycmd", "/pcmd"], ["/emote", "/em"]], []);
    Assert(travel.Classify("/tp") == CommandKind.Sensitive && travel.Classify("/pcmd") == CommandKind.Sensitive,
        "teleporting and party commands should be sensitive");
    Assert(!CommandPolicy.IsAllowed(travel.Canonicalize("/tp"), travel.Classify("/tp"), travel.CanonicalSet([]),
            travel.CanonicalSet([]), true, out _),
        "any game command should not cover teleporting");
    Assert(CommandPolicy.IsAllowed(travel.Canonicalize("/tp"), travel.Classify("/tp"), travel.CanonicalSet(["/teleport"]),
            travel.CanonicalSet([]), false, out _),
        "teleporting can still be allowed one by one");

    var noWaitRules = catalog.CanonicalSet([]);
    Assert(CommandPolicy.IsWaitAllowed(catalog, true, noWaitRules, noWaitRules, out _),
        "/wait in the reaction's own commands should pause without an allow entry");
    Assert(!CommandPolicy.IsWaitAllowed(catalog, false, noWaitRules, noWaitRules, out _),
        "/wait from a sender's text should need an allow entry");
    Assert(CommandPolicy.IsWaitAllowed(catalog, false, catalog.CanonicalSet(["/wait"]), noWaitRules, out _),
        "/wait from a sender's text should run once allowed");
    Assert(!CommandPolicy.IsWaitAllowed(catalog, true, catalog.CanonicalSet(["/wait"]), catalog.CanonicalSet(["/wait"]), out _),
        "a /wait block entry should always win");
    Assert(ReactionCommandMatcher.TemplateWaitLines("/wave\n/WAIT 2\n/$1").SequenceEqual([false, true, false]) &&
           !ReactionCommandMatcher.TemplateWaitLines(ReactionCommandMatcher.PhraseReplacement).Any(wait => wait),
        "only a template line that is itself /wait is the trigger's own pause");
    Assert(!ReactionCommandMatcher.IsTemplateWait([true], 1) && !ReactionCommandMatcher.IsTemplateWait([true], -1),
        "lines past the template's end are never the trigger's own pause");
    Assert(ReactionCommandMatcher.SplitLines("/a\r\n/b\r/c\n/d").Length == 4, "every kind of line break splits commands");

    Assert(ReactionCommandMatcher.EscapeTriggerPhrase("please.do") == @"please\.do", "trigger phrases should be literal text");
    Assert(ReactionCommandMatcher.EscapeTriggerPhrase("hey (you|simon says") == @"hey\ \(you|simon\ says",
        "alternatives should survive escaping");
    Assert(ReactionCommandMatcher.EscapeTriggerPhrase("please do | simon says") == @"please\ do|simon\ says",
        "spaces around | should not become part of a phrase");
    Assert(ReactionCommandMatcher.BuildPhrasePattern("please do|") == ReactionCommandMatcher.BuildPhrasePattern("please do") &&
           ReactionCommandMatcher.BuildPhrasePattern("||") == string.Empty && ReactionCommandMatcher.BuildPhrasePattern(" ") == string.Empty,
        "an empty alternative must never match every message");

    var trigger = new Regex(ReactionCommandMatcher.BuildPhrasePattern("please.do"));
    Assert(!trigger.IsMatch("pleaseXdo wave") && trigger.IsMatch("please.do wave"), "a dot in the trigger should be literal");
    Assert(!new Regex(ReactionCommandMatcher.BuildPhrasePattern("please do|")).IsMatch("lol sit"),
        "a trailing | should not make ordinary chat match");

    string Run(string phrase, string message)
    {
        var pattern = new Regex(ReactionCommandMatcher.BuildPhrasePattern(phrase), RegexOptions.None, TimeSpan.FromMilliseconds(250));
        return ReactionCommandMatcher.TryGenerateCommand(pattern, ReactionCommandMatcher.SanitizeIncoming(message),
                   ReactionCommandMatcher.PhraseReplacement, out var command, out _) == ReactionMatchStatus.Success
            ? command : "(no match)";
    }
    Assert(ReactionCommandMatcher.FormatCommand(Run("please do", "please do (ac Vercure [t])")).ToString() == "/ac Vercure <t>",
        "the guide's example should produce /ac Vercure <t>");
    Assert(Run("please do", "please do wave") == "/wave", "a single word should become the command");
    Assert(!Run("please do", "please do (wave\r/wait 60\r/wait 60)").Contains('\r') &&
           !Run("please do", "please do (wave\n/sh hi)").Contains('\n'),
        "a sender's line breaks must never split one message into several commands");
    var leaked = ReactionCommandMatcher.FormatCommand(Run("please do", "please do (p I am at <pos> [pos] [flag])"));
    Assert(!leaked.Args.Contains('<') && !leaked.Args.Contains('>'),
        "a sender's text must never carry a game placeholder like <pos>");
    Assert(ReactionCommandMatcher.SanitizeIncoming("a<b>c\td") == "a\uFF1Cb\uFF1Ec d",
        "angle brackets become look-alikes and control characters become spaces");

    var parsed = ReactionCommandMatcher.FormatCommand("/SH\u3000hello [me]");
    Assert(parsed.Main == "/sh" && parsed.Args == "hello <me>", "a full-width space should end the command name");
    Assert(ReactionCommandMatcher.FormatCommand("  ").Main.Length == 0 && ReactionCommandMatcher.FormatCommand("hello").Main == "hello",
        "blank lines and plain text should parse without a command");

    // Static setup runs in file order: these fail here (not in game) if a static is used before it's set.
    Assert(CommandCatalog.Empty.Classify("/logout") == CommandKind.Blocked && CommandRateLimiter.Shared != null,
        "the shared catalog and rate limiter should initialize");

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

    Assert(!new SenderFilter { Friends = false, FreeCompany = false, Party = false, Named = ["Some Body@"] }.Allows(stranger),
        "\"Name@\" names no world and should match nobody, not everyone with that name");
    Assert(PluginUiLogic.IsPublicChannel(37) && PluginUiLogic.IsPublicChannel(107) && PluginUiLogic.IsPublicChannel(10) &&
           !PluginUiLogic.IsPublicChannel(24),
        "cross-world linkshells and Say count as public; FC chat doesn't");

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
    var second = System.Diagnostics.Stopwatch.Frequency;
    Assert(limiter.TimeUntilFree(now) == TimeSpan.Zero && limiter.TryAcquire(now) && limiter.TryAcquire(now) && limiter.TryAcquire(now),
        "a burst of three should go out at once");
    Assert(!limiter.TryAcquire(now) && Math.Abs(limiter.TimeUntilFree(now).TotalSeconds - 1) < 0.001,
        "after the burst, the next send should be a second away");
    for (var i = 0; i < 100; i++)
        limiter.TimeUntilFree(now); // runs that wait and get cancelled
    Assert(Math.Abs(limiter.TimeUntilFree(now).TotalSeconds - 1) < 0.001,
        "waiting (and giving up) must never book slots: no backlog");
    Assert(limiter.TryAcquire(now + second) && !limiter.TryAcquire(now + second),
        "after a second, exactly one more send should be free");
    var later = now + 60 * second;
    Assert(limiter.TryAcquire(later) && limiter.TryAcquire(later) && limiter.TryAcquire(later) && !limiter.TryAcquire(later),
        "credit should come back after a quiet minute, but never beyond the burst");
    limiter.Reset();
    Assert(limiter.TryAcquire(later), "Reset should free the limiter");

    var skipper = new CommandRateLimiter(2, TimeSpan.FromSeconds(1));
    Assert(skipper.TryAcquire(now) && skipper.TryAcquire(now), "TryAcquire should take the free burst");
    Assert(!skipper.TryAcquire(now), "TryAcquire should refuse rather than wait once the burst is spent");
    Assert(skipper.TryAcquire(now + System.Diagnostics.Stopwatch.Frequency), "TryAcquire should succeed once a send is free again");
    Console.WriteLine("PASS command rate limiter");
}

static void RunFollowTests()
{
    {
        var start = new System.Numerics.Vector3(0, 0, 0);
        var corner = new System.Numerics.Vector3(10, 0, 0);
        var end = new System.Numerics.Vector3(10, 0, 10);
        var smooth = PathSmoothing.RoundCorners(start, [corner, end]);
        var maxCut = 0f;
        foreach (var point in smooth)
            maxCut = Math.Max(maxCut, Math.Min(Math.Abs(point.Z), Math.Abs(point.X - 10)));
        Assert(smooth[^1] == end && smooth.Count > 2 && !smooth.Contains(corner) && maxCut <= PathSmoothing.CornerCut + 0.001f,
            "a corner should be rounded into a short curve that stays near the original path and still ends at the destination");
        Assert(PathSmoothing.RoundCorners(start, [end]).SequenceEqual([end]), "a straight path stays as it is");
        var tiny = PathSmoothing.RoundCorners(start, [new(0.5f, 0, 0), new(0.5f, 0, 0.5f)]);
        Assert(tiny.TrueForAll(p => p.X >= -0.001f && p.X <= 0.501f && p.Z >= -0.001f && p.Z <= 0.501f),
            "short legs cut at most half of each leg");

        var heading = new System.Numerics.Vector3(1, 0, 0);
        var path = new List<System.Numerics.Vector3> { new(-2, 0, 0), new(1, 0, 0), new(8, 0, 0), new(8, 0, 9) };
        Assert(PathSmoothing.TrimBehind(path, start, heading).SequenceEqual([new(8, 0, 0), new(8, 0, 9)]),
            "waypoints behind us or right beside us are dropped from a swapped-in path");
        Assert(PathSmoothing.TrimBehind([new(-2, 0, 0)], start, heading).Count == 1, "the destination is never dropped");
    }

    FollowRequest Parse(string message, string call = "Ami", string follow = "follow", string stop = "stop", string come = "come",
                        string mimic = "mimic")
        => FollowParser.Parse(ReactionCommandMatcher.SanitizeIncoming(message), call, follow, stop, come, mimic);

    Assert(Parse("Ami mimic me") == new FollowRequest(FollowRequestKind.Mimic, "") &&
           Parse("Ami, mimic Nova!") == new FollowRequest(FollowRequestKind.Mimic, "Nova") &&
           Parse("Ami mimic").Kind == FollowRequestKind.None && Parse("Ami mimic me", mimic: "").Kind == FollowRequestKind.None,
        "mimic needs a player or \"me\", and no mimic word means no mimic requests");
    {
        string Canon(string command) => command.Trim().ToLowerInvariant();
        var overrides = new List<EmoteOverride> { new() { When = "/Dote", Reply = "/joy" }, new() { When = "/slap", Reply = "" } };
        Assert(EmoteReplySettings.ReplyFor(overrides, "/dote", Canon) == "/joy" &&
               EmoteReplySettings.ReplyFor(overrides, "/slap", Canon) == "" &&
               EmoteReplySettings.ReplyFor(overrides, "/wave", Canon) == "/wave",
            "an override replaces the reply (empty: no reply); other emotes are answered in kind");
    }

    Assert(Parse("Ami follow").Kind == FollowRequestKind.None && Parse("Ami follow!").Kind == FollowRequestKind.None,
        "a bare \"Ami follow\" names nobody, so it isn't a request");
    Assert(Parse("ami FOLLOW me!") == new FollowRequest(FollowRequestKind.Follow, ""), "case and trailing punctuation shouldn't matter");
    Assert(Parse("Ami, follow me") == new FollowRequest(FollowRequestKind.Follow, ""), "\"me\" should mean the sender");
    Assert(Parse("Ami come") == new FollowRequest(FollowRequestKind.Come, "") && Parse("Ami, come here!").Kind == FollowRequestKind.Come &&
           Parse("Ami come", come: "").Kind == FollowRequestKind.None,
        "the come word should come to the sender, and no come word means no come requests");
    Assert(Parse("Ami follow Nova Ral'veth@Exodus") == new FollowRequest(FollowRequestKind.Follow, "Nova Ral'veth@Exodus"),
        "a named player should be the target");
    Assert(Parse("hey Ami follow Nova.") == new FollowRequest(FollowRequestKind.Follow, "Nova"), "the request can follow other words");
    Assert(Parse("Ami stop") == new FollowRequest(FollowRequestKind.Stop, "") && Parse("ok Ami, STOP now").Kind == FollowRequestKind.Stop,
        "the stop word should stop");
    Assert(Parse("Amity follow me").Kind == FollowRequestKind.None && Parse("Ami followers me").Kind == FollowRequestKind.None &&
           Parse("Ami comes").Kind == FollowRequestKind.None,
        "call names and words must be whole words");
    Assert(Parse("follow Ami").Kind == FollowRequestKind.None, "the call name comes first");
    Assert(Parse("Ami trail Nova", follow: "follow|trail").Kind == FollowRequestKind.Follow &&
           Parse("Kitty follow me", call: "Ami|Kitty").Kind == FollowRequestKind.Follow &&
           Parse("Ami here", come: "come|here").Kind == FollowRequestKind.Come,
        "several call names and words should work");
    Assert(Parse("Ami follow me", call: "").Kind == FollowRequestKind.None && Parse("Ami come", call: " | ").Kind == FollowRequestKind.None,
        "no call name means no requests");
    Assert(Parse("Ami.* follow me", call: "Ami.*").Kind == FollowRequestKind.Follow && Parse("Amixx follow me", call: "Ami.*").Kind == FollowRequestKind.None,
        "call names are plain text, not patterns");
    Assert(!Parse("Ami follow Nova\r/sh hi").Target.Contains('\r'), "a line break can't sneak into the target");

    Assert(FollowParser.SplitName("Nova Ral'veth@Exodus") == new PlayerName("Nova Ral'veth", "Exodus") &&
           FollowParser.SplitName(" Nova ") == new PlayerName("Nova", ""),
        "Name@World should split into name and world");

    PlayerName[] nearby =
    [
        new("Nova Ral'veth", "Exodus"),
        new("Bob Smith", "Ultros"),
        new("Bob Jones", "Cactuar"),
        new("Nova Ral'veth", "Ultros"),
    ];
    Assert(FollowParser.FindNearby(new("nova ral'veth", "Exodus"), nearby) == 0 &&
           FollowParser.FindNearby(new("Nova Ral'veth", "Ultros"), nearby) == 3,
        "a full name and world should find that exact player, ignoring case");
    Assert(FollowParser.FindNearby(new("Bob Smith", ""), nearby) == 1, "a full name without a world should find the player");
    Assert(FollowParser.FindNearby(new("Bob", ""), nearby) == -1, "a first name two players share is ambiguous");
    Assert(FollowParser.FindNearby(new("Nova Ral'veth", "Cactuar"), nearby) == -1, "the wrong world should not match");
    Assert(FollowParser.FindNearby(new("Nova", ""), [new("Nova Ral'veth", "Exodus"), new("Bob Smith", "Ultros")]) == 0,
        "a first name only one nearby player has should find them");
    Assert(FollowParser.FindNearby(new("Nobody", ""), nearby) == -1 && FollowParser.FindNearby(new("", ""), nearby) == -1,
        "nobody nearby means no match");

    var nova = new PlayerName("Nova Ral'veth", "Exodus");
    Assert(FollowParser.MayFollow(nova, [], []), "with empty lists anyone may be followed");
    Assert(!FollowParser.MayFollow(nova, [], ["nova ral'veth"]), "the never list should block, on any world for a bare name");
    Assert(!FollowParser.MayFollow(nova, ["Bob Smith"], []) && FollowParser.MayFollow(nova, ["Nova Ral'veth@Exodus"], []),
        "when the only list has names, only they may be followed");
    Assert(!FollowParser.MayFollow(nova, ["Nova Ral'veth"], ["Nova Ral'veth@Exodus"]), "never beats only");

    Assert(FollowParser.FormatReply("uwu I'm sorry master I don't see <target> near me :c", "Nova") ==
           "uwu I'm sorry master I don't see Nova near me :c", "<target> should become the requested name");
    Assert(FollowParser.FormatReply("I don't see <TARGET>", "<pos>\uFF1Cflag\uFF1E") == "I don't see posflag",
        "a requested name can't carry a game placeholder into the reply");

    var config = new Configuration();
    Assert(!config.Follow.Enabled && config.Follow.FollowWords == "follow" && config.Follow.StopWords == "stop" &&
           config.Follow.Channels.SequenceEqual([13, 14, 24]) && !config.Follow.Senders.Anyone && config.Follow.StopMoves,
        "Follow mode should start off, with safe defaults");
    static bool FullyProtected(ProtectionSettings p) =>
        p.Chat && p.Risky && p.Plugins && p.OpenChat.Count == 0 && p.OpenRisky.Count == 0 && p.OpenPlugins.Count == 0;
    {
        var v4 = DalamudJson.Load("{\"Version\": 4, \"Reactions\": [{}], \"Follow\": {\"Enabled\": true, \"CallNames\": \"Ami\", " +
                                  "\"MimicWords\": \"copy\", \"MimicMotionOnly\": false, \"Channels\": [13], \"OnlyFollow\": [\"Nova\"]}}");
        ConfigurationMigrator.MigrateAndNormalize(v4);
        var moved = v4.Mimic;
        Assert(v4.Version == ConfigVersion.CURRENT && moved.Enabled && moved.CallNames == "Ami" && moved.MimicWords == "copy" && !moved.MotionOnly &&
               moved.Channels.SequenceEqual([13]) && moved.OnlyMimic.SequenceEqual(["Nova"]) && moved.DelaySeconds == 0f &&
               moved.RepeatGuardSeconds == 3f && v4.Follow.MimicWords == null &&
               !Newtonsoft.Json.JsonConvert.SerializeObject(v4.Follow).Contains("MimicWords"),
            "v4's mimic settings (inside Follow mode) should move to their own settings, starting as copies of Follow mode's");
        var fresh = new Configuration();
        Assert(!fresh.Mimic.Enabled && fresh.Mimic.MimicWords == "mimic" && fresh.Mimic.DelaySeconds == 0f,
            "Mimic should start off, with no delay");
    }
    var loaded = DalamudJson.Load("{\"Version\": 4, \"Reactions\": [{}, {\"Protections\": null}]}");
    ConfigurationMigrator.MigrateAndNormalize(loaded);
    Assert(FullyProtected(Reaction.CreateDefault().Protections) && !Reaction.CreateDefault().NoProtections &&
           FullyProtected(loaded.Reactions[0].Protections) && FullyProtected(loaded.Reactions[1].Protections) &&
           FullyProtected(loaded.DefaultProtections),
        "triggers should start with every protection on, new or loaded (a null one is repaired)");
    var trusted = new Reaction { NoProtections = true };
    trusted.Protections.OpenChat.Add("say");
    var copy = PluginUiLogic.CloneReaction(trusted);
    Assert(!copy.NoProtections && copy.Protections.OpenChat.SequenceEqual(["say"]) &&
           !ReferenceEquals(copy.Protections.OpenChat, trusted.Protections.OpenChat),
        "a duplicate should keep its own copy of the protections but never start without them");
    var defaults = new Configuration();
    defaults.DefaultProtections.Plugins = false;
    Assert(!PluginUiLogic.CreateReactionFromLog(13, "hi", "Tell", defaults).Protections.Plugins,
        "triggers made from the message log should start with the default protections");
    var shrunk = new Configuration();
    shrunk.Follow.Channels = [13];
    Assert(DalamudJson.Load(DalamudJson.Save(shrunk)).Follow.Channels.SequenceEqual([13]),
        "follow channels should load back exactly as saved");
    var broken = new Configuration { Follow = null! };
    ConfigurationMigrator.MigrateAndNormalize(broken);
    Assert(broken.Follow != null && broken.Follow.Channels != null, "a missing follow section should be repaired");

    Console.WriteLine("PASS follow mode");
}

// Ways a sender (or a careless template) might try to get a forbidden command past the protections. Each line goes
// the way a run sends it: parsed by FormatCommand, then classified and checked by its command name.
static void RunCommandPolicyBypassTests()
{
    var catalog = new CommandCatalog(
        [
            ["/logout"],
            ["/shutdown"],
            ["/say", "/s"],
            ["/teleport", "/tp"],
            ["/action", "/ac"],
            ["/follow"],
        ],
        [
            ["/wave"],
            ["/dance"],
        ]);
    var none = catalog.CanonicalSet([]);

    // Worst case by default: any game command allowed, and the command's own protection switched off.
    bool Runs(CommandCatalog commands, string line, IEnumerable<string> allow, bool allowAll = true, bool noProtections = false,
              bool open = true, Func<string, bool>? plugin = null)
    {
        var parsed = ReactionCommandMatcher.FormatCommand(line);
        if (parsed.Main.Length == 0)
            return false;
        return CommandPolicy.IsAllowed(commands.Canonicalize(parsed.Main), commands.Classify(parsed.Main, plugin),
            commands.CanonicalSet(allow), none, allowAll, out _, noProtections, open);
    }

    string[] alwaysBlocked =
    [
        "/LOGOUT", "/LoGoUt", "  /logout  ", "\t/logout", "/logout\u3000now", "/logout\u00A0now", "/logout\tnow",
        "/SHUTDOWN", "/XLPLUGINS", "/xlSettings", "/xldev", "/PMKK", "/PuppetMasterKK", "/puppetmaster",
    ];
    foreach (var line in alwaysBlocked)
    {
        Assert(!Runs(catalog, line, [line.Trim(), "/logout", "/shutdown", "/xlplugins", "/pmkk"], plugin: _ => true),
            $"\"{line}\" should never run, whatever its case or spacing, even listed under Allowed, with any game command " +
            "allowed and its protection off");
    }
    var german = new CommandCatalog([["/folgen", "/follow"], ["/abmelden", "/logout"]], []);
    Assert(!Runs(german, "/ABMELDEN", ["/abmelden"], plugin: _ => true),
        "the client's own name for /logout should be blocked in any case, even listed");

    // Look-alikes the catalog doesn't know: never commands while protections are on.
    string[] lookAlikes =
    [
        "\uFF0Flogout", "/\uFF4C\uFF4F\uFF47\uFF4F\uFF55\uFF54", "\uFF0Ffollow", "/\uFF46\uFF4F\uFF4C\uFF4C\uFF4F\uFF57",
        "/log\u200Bout", "/fol\u200Blow", "/logout\u0301",
    ];
    foreach (var line in lookAlikes)
    {
        Assert(!Runs(catalog, line, [], plugin: command => command == "/hello"),
            $"the look-alike \"{line}\" is not a known command, so it should never run while protections are on");
    }
    Assert(ReactionCommandMatcher.FormatCommand("\uFF0Flogout").Main == "\uFF0Flogout",
        "a full-width slash doesn't start a command: the whole line is plain text");
    Assert(catalog.Classify("\uFF0F\uFF4C\uFF4F\uFF47\uFF4F\uFF55\uFF54") == CommandKind.Blocked &&
           catalog.Classify("\u200B/logout") == CommandKind.Blocked,
        "look-alikes of an always-blocked command are blocked too, in case the game reads them as the real one");
    Assert(catalog.Classify("\uFF0F\uFF46\uFF4F\uFF4C\uFF4C\uFF4F\uFF57") == CommandKind.FollowOnly &&
           catalog.Classify("\u200B/follow") == CommandKind.FollowOnly,
        "look-alikes of /follow belong to Follow mode only");
    Assert(catalog.Classify("/follow\u200Bme") == CommandKind.FollowOnly && catalog.Classify("/logout\u200Bnow") == CommandKind.Blocked,
        "an invisible character right after the name is caught too, in case the game ends the name there");
    Assert(catalog.Classify("\uFF0F\uFF44\uFF41\uFF4E\uFF43\uFF45") == CommandKind.Unknown,
        "a look-alike is never allowed as the real command (a full-width /dance is not an emote)");
    Assert(!Runs(catalog, "\uFF0Ffollow", [], noProtections: true),
        "a full-width /follow should stay blocked even with protections off");

    Assert(!Runs(catalog, "/nonsense", []) && !Runs(catalog, "/nonsense now", []),
        "an unknown command should never run with protections on, even with any game command allowed");
    Assert(!Runs(catalog, "hello everyone", []) && !Runs(catalog, "logout", []) && !Runs(catalog, "s hi", []),
        "a plain text line (no leading /) is not a command and should never run with protections on");
    Assert(ReactionCommandMatcher.FormatCommand("hello everyone").Main == "hello everyone" &&
           ReactionCommandMatcher.FormatCommand("hello everyone").Args.Length == 0,
        "plain text should not be split into a command name and arguments");
    Assert(Runs(catalog, "/s hi", [], allowAll: false) && !Runs(catalog, "/s hi", [], allowAll: true, open: false),
        "a chat command runs only when its protection is off (or it's listed), never because any game command is allowed");

    string[] follows = ["/follow", "/FOLLOW", " /follow <t>", "/follow\u3000<t>", "/Follow\t[t]"];
    foreach (var line in follows)
    {
        Assert(!Runs(catalog, line, ["/follow"], noProtections: true),
            $"\"{line}\" belongs to Follow mode: a trigger never sends it, even listed and without protections");
    }
    Assert(german.Classify("/folgen") == CommandKind.FollowOnly && german.Classify("/FOLLOW") == CommandKind.FollowOnly &&
           !Runs(german, "/folgen <t>", ["/folgen"], noProtections: true),
        "the client's own name for /follow should be Follow mode's too, even without protections");
    Assert(!CommandPolicy.IsAllowed("/follow", CommandKind.Unknown, none, none, true, out _, noProtections: true, open: true),
        "/follow should be refused by name even if it was classified as something else");

    var allOff = new ProtectionSettings { Chat = false, Risky = false, Plugins = false };
    foreach (var command in new[] { "/logout", "/shutdown", "/xlplugins", "/pmkk", "/follow" })
    {
        var kind = catalog.Classify(command, _ => true);
        Assert(!CommandPolicy.IsAllowed(catalog.Canonicalize(command), kind, catalog.CanonicalSet([command]), none, true, out _,
                open: allOff.IsOpen(kind, "anything")),
            $"{command} should stay blocked with every protection switched off and it listed under Allowed");
    }
    Assert(!new ProtectionSettings().IsOpen(CommandKind.Plugin, null) && !allOff.IsOpen(CommandKind.Unknown, null) &&
           !allOff.IsOpen(CommandKind.Game, null) && !allOff.IsOpen(CommandKind.Blocked, null),
        "a plugin command whose owner isn't known stays protected; switches never open unknown, game or blocked commands");

    Assert(CommandPolicy.ConvertPlaceholders("[pos] [flag] [se.1] [hp] [Me] [[t]] [t") == "[pos] [flag] [se.1] [hp] <Me> [[t]] [t",
        "only the safe target placeholders convert; everything else stays literal");

    Console.WriteLine("PASS command policy bypass attempts");
}

// A regex trigger's captures are someone else's text, substituted into the trigger's commands.
static void RunHostileCaptureTests()
{
    var catalog = new CommandCatalog([["/logout"], ["/say", "/s"], ["/echo", "/e"], ["/follow"]], [["/wave"]]);
    var none = catalog.CanonicalSet([]);
    var order = new Regex(@"^order (.+)$", RegexOptions.None, TimeSpan.FromMilliseconds(250));

    // What a run does with a message: sanitize it, build the commands, split them into lines.
    string[] Lines(Regex pattern, string replacement, string message)
    {
        var status = ReactionCommandMatcher.TryGenerateCommand(pattern, ReactionCommandMatcher.SanitizeIncoming(message),
            replacement, out var command, out _);
        return status == ReactionMatchStatus.Success ? Regex.Split(command, "\r\n|\r|\n") : Array.Empty<string>();
    }

    // Whether any line would run, with any game command allowed and every protection switch off.
    bool AnyRuns(string[] lines, bool noProtections = false)
    {
        foreach (var line in lines)
        {
            var parsed = ReactionCommandMatcher.FormatCommand(line);
            if (parsed.Main.Length > 0 &&
                CommandPolicy.IsAllowed(catalog.Canonicalize(parsed.Main), catalog.Classify(parsed.Main), none, none, true, out _,
                    noProtections, open: true))
                return true;
        }
        return false;
    }

    foreach (var breaker in new[] { "\n", "\r", "\r\n", "\u0085", "\u000B", "\u000C", "\0" })
    {
        var lines = Lines(order, "/say $1", $"order hi{breaker}/logout");
        Assert(lines.Length == 1 && ReactionCommandMatcher.FormatCommand(lines[0]).Main == "/say",
            $"a line break (U+{(int)breaker[^1]:X4}) in a capture must not start a second command");
    }
    var separated = Lines(order, "/say $1", "order hi\u2028/logout\u2029/shutdown");
    Assert(separated.Length == 1 && ReactionCommandMatcher.FormatCommand(separated[0]).Main == "/say",
        "Unicode line and paragraph separators must not split a message into several commands either");

    var named = Lines(order, "/$1", "order LOGOUT");
    Assert(named.SequenceEqual(["/LOGOUT"]) && !AnyRuns(named) && AnyRuns(Lines(order, "/$1", "order wave")),
        "a capture that names /logout should be refused like the command itself (an emote still runs)");
    Assert(!AnyRuns(Lines(order, "/$1", "order follow me"), noProtections: true),
        "a capture that names /follow should be refused even without protections");
    Assert(!AnyRuns(Lines(order, "/$1", "order \uFF4C\uFF4F\uFF47\uFF4F\uFF55\uFF54")) &&
           !AnyRuns(Lines(order, "$1", "order \uFF0Flogout")),
        "full-width look-alikes from a capture are not commands");
    Assert(Lines(order, "/echo $1", "order $0 $1 ${1} $$ $+ $_").SequenceEqual(["/echo $0 $1 ${1} $$ $+ $_"]),
        "substitution tokens inside a capture are text: they're never expanded again");
    var placed = ReactionCommandMatcher.FormatCommand(Lines(order, "/echo $1", "order <pos> <flag> [pos] <se.1>")[0]);
    Assert(!placed.Args.Contains('<') && !placed.Args.Contains('>') && placed.Args.Contains("[pos]"),
        "a capture can never carry a game placeholder (only the safe [t]-style ones convert)");

    var phrase = new Regex(ReactionCommandMatcher.BuildPhrasePattern("please do"), RegexOptions.None, TimeSpan.FromMilliseconds(250));
    var replacement = ReactionCommandMatcher.PhraseReplacement;
    Assert(Lines(phrase, replacement, "please do (wave)\n/logout").SequenceEqual(["/wave"]),
        "text after the command in a phrase message is never run");
    Assert(!AnyRuns(Lines(phrase, replacement, "please do (logout)")) && !AnyRuns(Lines(phrase, replacement, "please do xlplugins")) &&
           !AnyRuns(Lines(phrase, replacement, "please do (/logout)")) &&
           !AnyRuns(Lines(phrase, replacement, "please do (follow <t>)"), noProtections: true),
        "phrase mode should refuse blocked commands and /follow like any other");

    var evil = new Regex(@"^(\w+\s?)*$", RegexOptions.None, TimeSpan.FromMilliseconds(50));
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var evilStatus = ReactionCommandMatcher.TryGenerateCommand(evil, new string('a', 64) + "!", "/$1", out _, out _);
    Assert((evilStatus == ReactionMatchStatus.TimedOut || evilStatus == ReactionMatchStatus.NoMatch) &&
           watch.Elapsed < TimeSpan.FromSeconds(5),
        "a pattern that backtracks forever should time out, not hang or throw");

    Console.WriteLine("PASS hostile captures");
}

static void RunHostileConfigTests()
{
    // Numbers too big for the setting: the whole file is unreadable (the plugin keeps it aside and starts from defaults).
    AssertThrows<Newtonsoft.Json.JsonException>(
        () => DalamudJson.Load("{\"Version\": 5, \"Reactions\": [{\"CooldownSeconds\": 99999999999999999999}]}"),
        "a cooldown too big for a number should make the file unreadable, not wrap around");
    AssertThrows<Newtonsoft.Json.JsonException>(() => DalamudJson.Load("{\"Version\": 99999999999}"),
        "a version too big for a number should make the file unreadable");
    AssertThrows<Newtonsoft.Json.JsonException>(() => DalamudJson.Load("{\"Version\": null}"),
        "a null version should make the file unreadable");
    AssertThrows<Newtonsoft.Json.JsonException>(
        () => DalamudJson.Load("{\"Version\": 5, \"Unknown\": " + new string('[', 100_000) + new string(']', 100_000) + "}"),
        "a deeply nested file should be refused, not overflow the stack");
    var newest = DalamudJson.Load("{\"Version\": 2147483647}");
    AssertThrows<InvalidOperationException>(() => ConfigurationMigrator.MigrateAndNormalize(newest),
        "a version from the far future should be refused, not migrated");

    // The old Puppet Master's file is read with plain Newtonsoft defaults: "$type" must never choose what's created.
    var typed = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(
        "{\"$type\": \"System.IO.FileInfo, System.IO.FileSystem\", \"Version\": 5, \"Reactions\": " +
        "[{\"$type\": \"Evil.Payload, Evil\", \"Name\": \"x\"}]}");
    Assert(typed != null && typed.GetType() == typeof(Configuration) && typed.Reactions.Count == 1 &&
           typed.Reactions[0].GetType() == typeof(Reaction) && typed.Reactions[0].Name == "x",
        "type names in a settings file should be ignored");

    var weird = new Configuration { TextScale = float.NaN };
    weird.Mimic.DelaySeconds = float.PositiveInfinity;
    weird.Mimic.RepeatGuardSeconds = float.NaN;
    ConfigurationMigrator.MigrateAndNormalize(weird);
    Assert(float.IsFinite(weird.TextScale) && weird.Mimic.DelaySeconds == 0f && weird.Mimic.RepeatGuardSeconds == 3f,
        "NaN and infinite numbers should be replaced with safe ones");
    Assert(!ConfigurationMigrator.MigrateAndNormalize(weird), "repairing them should be idempotent");

    Console.WriteLine("PASS hostile configuration");
}

static void RunHostileFollowTests()
{
    FollowRequest Parse(string message)
        => FollowParser.Parse(ReactionCommandMatcher.SanitizeIncoming(message), "Ami", "follow", "stop", "come", "mimic");

    var placeholder = Parse("Ami follow <pos>");
    Assert(placeholder.Kind == FollowRequestKind.Follow && !placeholder.Target.Contains('<') && !placeholder.Target.Contains('>'),
        "a requested name can't hold a game placeholder");
    Assert(FollowParser.FormatReply("Sorry, I don't see <target> near me.", placeholder.Target) == "Sorry, I don't see pos near me.",
        "the not-nearby reply should never carry a placeholder from the sender's text");
    Assert(FollowParser.FormatReply("I see <target>", "<target>") == "I see target",
        "a requested name can't bring <target> (or any placeholder) back into the reply");
    var mimicked = Parse("Ami mimic <t>");
    Assert(mimicked.Kind == FollowRequestKind.Mimic && !mimicked.Target.Contains('<'),
        "a mimic request can't hold a game placeholder either");

    var injected = Parse("Ami follow Nova\n/logout\r/shutdown");
    Assert(injected.Kind == FollowRequestKind.Follow && injected.Target.IndexOfAny(['\r', '\n']) < 0,
        "a line break can't sneak a second command into a follow request");
    Assert(FollowParser.FindNearby(FollowParser.SplitName(injected.Target), [new("Nova Ral'veth", "Exodus")]) == -1,
        "extra text after a name should match nobody, not the first word");
    Assert(Parse("Ami follow ME!!") == new FollowRequest(FollowRequestKind.Follow, "") &&
           Parse("Ami follow  myself ") == new FollowRequest(FollowRequestKind.Follow, ""),
        "\"me\" in any case means the sender");

    Assert(FollowParser.SplitName("Nova@Exodus@Evil") == new PlayerName("Nova", "Exodus@Evil") &&
           FollowParser.FindNearby(FollowParser.SplitName("Nova@Exodus@Evil"), [new("Nova", "Exodus")]) == -1,
        "a second @ is part of the world, so it matches no real world");
    Assert(FollowParser.FindNearby(FollowParser.SplitName("@Exodus"), [new("Nova Ral'veth", "Exodus")]) == -1 &&
           FollowParser.FindNearby(FollowParser.SplitName("   "), [new("Nova Ral'veth", "Exodus")]) == -1,
        "a request with no name matches nobody");
    Assert(!FollowParser.MayFollow(new("Nova Ral'veth", "Ultros"), ["Nova Ral'veth@Exodus"], []) &&
           !FollowParser.MayFollow(new("Nova Ral'veth", ""), ["Nova Ral'veth@Exodus"], []),
        "the only list should not let a same-named player from another (or an unknown) world through");

    var watch = System.Diagnostics.Stopwatch.StartNew();
    var huge = Parse("Ami follow " + new string('a', 100_000));
    _ = Parse("Ami follow a" + string.Concat(Enumerable.Repeat(" .", 20_000)) + "b");
    Assert(watch.Elapsed < TimeSpan.FromSeconds(5),
        "a huge or backtracking-bait request should be answered (or given up on) quickly");
    Assert(FollowParser.FormatReply("I don't see <target>", huge.Target).Length < 100,
        "a huge requested name should be cut so the tell stays short");

    // Two players with the same full name on different worlds: picking one would be a guess.
    PlayerName[] twins = [new("Nova Ral'veth", "Exodus"), new("Nova Ral'veth", "Ultros")];
    Assert(FollowParser.FindNearby(new("Nova Ral'veth", ""), twins) == -1,
        "a full name two nearby players share (on different worlds) is ambiguous: the sender has to name the world");

    Console.WriteLine("PASS hostile follow requests");
}

static void RunPerSenderLimitTests()
{
    // Per-person cooldowns: by "Name@World", case-insensitive; everyone without a name shares one.
    var cooldowns = new SenderCooldowns();
    var nova = SenderCooldowns.KeyFor(new SenderInfo("Nova Ral'veth", "Exodus", false, true, false, false));
    var shouting = SenderCooldowns.KeyFor(new SenderInfo("NOVA RAL'VETH", "EXODUS", false, false, false, false));
    var other = SenderCooldowns.KeyFor(new SenderInfo("Test Player", "World", false, false, false, false));
    Assert(nova == "Nova Ral'veth@Exodus" && SenderCooldowns.KeyFor(new SenderInfo("Test Player", "", false, false, false, false)) == "Test Player",
        "the key is Name@World, or the name when the world isn't known");
    Assert(SenderCooldowns.KeyFor(SenderInfo.Unknown) == string.Empty, "an unknown sender has the shared empty key");
    cooldowns.Start(nova, 1000, 500);
    Assert(cooldowns.IsWaiting(shouting, 1200), "the cooldown should ignore case");
    Assert(!cooldowns.IsWaiting(other, 1200), "another person isn't held up");
    Assert(!cooldowns.IsWaiting(nova, 1500), "the cooldown ends on time");
    cooldowns.Start(other, 0, 0);
    Assert(!cooldowns.IsWaiting(other, 0), "a zero cooldown never starts");
    cooldowns.Start(other, long.MaxValue - 5, long.MaxValue);
    Assert(cooldowns.IsWaiting(other, long.MaxValue - 1), "a huge cooldown can't wrap around into the past");

    // A crowd (or made-up names on a custom channel) can't grow it without end.
    var crowd = new SenderCooldowns();
    for (var i = 0; i < 5000; i++)
        crowd.Start($"Test Player {i}@World", i, 1_000_000);
    Assert(crowd.Count <= Cooldowns.MaxEntries, $"per-person cooldowns should stay bounded ({crowd.Count})");
    Assert(crowd.IsWaiting("Test Player 4999@World", 5000), "the newest entries are kept");
    var expiring = new SenderCooldowns();
    for (var i = 0; i < 300; i++)
        expiring.Start($"Test Player {i}@World", i * 10, 5);
    Assert(expiring.Count < 300, "expired entries should be pruned");
    expiring.Clear();
    Assert(expiring.Count == 0, "Clear empties it");

    // One waiting request per person: their newer request replaces their older one, others keep their place.
    var queue = new BoundedRetriggerQueue<(string From, string Command)>(16);
    queue.Enqueue(ReactionExecutionPolicy.QueueEveryTrigger, ("Nova Ral'veth@Exodus", "/wave"));
    queue.Enqueue(ReactionExecutionPolicy.QueueEveryTrigger, ("Test Player@World", "/dance"));
    var dropped = queue.Enqueue(ReactionExecutionPolicy.QueueEveryTrigger, ("nova ral'veth@exodus", "/cheer"),
        item => item.From.Equals("nova ral'veth@exodus", StringComparison.OrdinalIgnoreCase), out var replaced);
    Assert(dropped == 0 && replaced == 1 && queue.Count == 2, "the same person's waiting request should be replaced");
    Assert(queue.TryDequeue(out var firstWaiting) && firstWaiting.Command == "/dance" &&
           queue.TryDequeue(out var secondWaiting) && secondWaiting.Command == "/cheer",
        "the other person keeps their place and the newer request goes to the back");
    var full = new BoundedRetriggerQueue<string>(3);
    foreach (var item in new[] { "a", "b", "c" })
        full.Enqueue(ReactionExecutionPolicy.QueueEveryTrigger, item);
    Assert(full.Enqueue(ReactionExecutionPolicy.QueueEveryTrigger, "d", item => item == "b", out var replacedInFull) == 0 &&
           replacedInFull == 1 && full.Count == 3, "replacing makes room, so nothing is dropped as overflow");
    full.Enqueue(ReactionExecutionPolicy.QueueEveryTrigger, "e", item => item == "nobody", out var none);
    Assert(none == 0 && full.Count == 3, "with nothing to replace, the queue still keeps its bound");

    // The scheduler counts a per-person replacement as Replaced, not as overflow.
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var acquiring = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var executed = new List<string>();
    var overflow = 0;
    var replacedCount = 0;
    var scheduler = new BoundedRetriggerScheduler<(string From, string Command)>(
        16,
        async (_, token) =>
        {
            acquiring.TrySetResult();
            await gate.Task.WaitAsync(token);
            return new CancellationTokenSource();
        },
        (item, lease) =>
        {
            lease.Dispose();
            executed.Add(item.Command);
            return Task.CompletedTask;
        },
        count => overflow += count,
        reportReplaced: count => replacedCount += count);
    Predicate<(string From, string Command)> Same(string from) => item => item.From.Equals(from, StringComparison.OrdinalIgnoreCase);
    var drainer = scheduler.Enqueue(ReactionExecutionPolicy.QueueEveryTrigger, ("Nova Ral'veth@Exodus", "/wave"), CancellationToken.None,
        Same("Nova Ral'veth@Exodus"))!;
    acquiring.Task.GetAwaiter().GetResult();
    scheduler.Enqueue(ReactionExecutionPolicy.QueueEveryTrigger, ("Test Player@World", "/dance"), CancellationToken.None, Same("Test Player@World"));
    scheduler.Enqueue(ReactionExecutionPolicy.QueueEveryTrigger, ("Nova Ral'veth@Exodus", "/cheer"), CancellationToken.None,
        Same("Nova Ral'veth@Exodus"));
    gate.TrySetResult();
    drainer.GetAwaiter().GetResult();
    Assert(executed.SequenceEqual(["/dance", "/cheer"]) && replacedCount == 1 && overflow == 0,
        $"only the person's newest request should run ({string.Join(", ", executed)}; replaced {replacedCount})");

    // Activity's Waiting list follows the same rule and shows who each request is from.
    ReactionVisualizerState.Reset();
    ReactionVisualizerState.QueuedRun(3, "Hug", "/hug 1", ReactionExecutionPolicy.QueueEveryTrigger, "Nova Ral'veth@Exodus", true);
    ReactionVisualizerState.QueuedRun(3, "Hug", "/hug 2", ReactionExecutionPolicy.QueueEveryTrigger, "Test Player@World", true);
    ReactionVisualizerState.QueuedRun(3, "Hug", "/hug 3", ReactionExecutionPolicy.QueueEveryTrigger, "NOVA RAL'VETH@EXODUS", true);
    var snapshot = ReactionVisualizerState.Snapshot();
    Assert(snapshot.Queued.Select(item => item.Command).SequenceEqual(["/hug 2", "/hug 3"]) &&
           snapshot.Queued[0].From == "Test Player@World",
        "Waiting should drop the person's older request and keep who sent each");
    Assert(snapshot.Recent[0] is { Status: VisualizerRunStatus.Replaced, Command: "/hug 1" }, "the replaced request shows as Replaced");
    ReactionVisualizerState.QueuedRun(3, "Hug", "/hug 4", ReactionExecutionPolicy.QueueEveryTrigger, "Test Player@World");
    Assert(ReactionVisualizerState.Snapshot().Queued.Length == 3, "without the option, a person can have several waiting");
    ReactionVisualizerState.Reset();

    Console.WriteLine("PASS per-person limits");
}

static void RunChoiceTests()
{
    string[] words = ["hug", "Wave", " dance "];
    Assert(ChoiceSelector.Select(ChoiceMode.ByWord, words, "WAVE", 0, _ => 0) == 1 &&
           ChoiceSelector.Select(ChoiceMode.ByWord, words, "dance", 0, _ => 0) == 2,
        "By word picks the choice whose word is $1, ignoring case and spaces");
    Assert(ChoiceSelector.Select(ChoiceMode.ByWord, words, "logout", 0, _ => 0) == -1 &&
           ChoiceSelector.Select(ChoiceMode.ByWord, words, "", 0, _ => 0) == -1 &&
           ChoiceSelector.Select(ChoiceMode.ByWord, ["", "hug"], " ", 0, _ => 0) == -1,
        "an unknown or empty word picks nothing, and an empty choice word never matches");
    Assert(ChoiceSelector.Select(ChoiceMode.InTurn, words, "", 0, _ => 0) == 0 &&
           ChoiceSelector.Select(ChoiceMode.InTurn, words, "", 4, _ => 0) == 1 &&
           ChoiceSelector.Select(ChoiceMode.InTurn, words, "", int.MinValue, _ => 0) is >= 0 and < 3,
        "In turn goes round the choices, even after the counter wraps");
    Assert(ChoiceSelector.Select(ChoiceMode.Random, words, "", 0, _ => 2) == 2 &&
           ChoiceSelector.Select(ChoiceMode.Random, words, "", 0, _ => 99) == 2 &&
           ChoiceSelector.Select(ChoiceMode.Random, words, "", 0, _ => -5) == 0,
        "Random uses the roll, kept in range");
    Assert(ChoiceSelector.Select(ChoiceMode.Random, [], "", 0, _ => 0) == -1 &&
           ChoiceSelector.Select(ChoiceMode.Off, words, "hug", 0, _ => 0) == -1, "no choices (or Off) picks nothing");
    var turn = new ChoiceTurn();
    turn.Advance();
    turn.Advance();
    Assert(turn.Current == 2, "the turn moves on once per Advance");

    var reaction = new Reaction
    {
        Name = "Hugs",
        Enabled = true,
        UseRegex = true,
        CustomRx = new Regex(@"^please (\w+)(?: (\w+))?$", RegexOptions.None, TimeSpan.FromMilliseconds(250)),
        ReplaceMatch = "/$1",
        ChoiceMode = ChoiceMode.ByWord,
        Choices =
        [
            new ReactionChoice { Word = "hug", Commands = "/hug $2\n/wait 1" },
            new ReactionChoice { Word = "wave", Commands = "/wave\n/$2" },
        ],
        EnabledChannels = [10],
        Senders = SenderFilter.AnyoneFilter(),
    };
    var set = ChoiceSet.From(reaction)!;
    var status = ReactionCommandMatcher.TryGenerateCommand(reaction.CustomRx, "please HUG friend", reaction.ReplaceMatch, set, 0, null,
        out var command, out _, out var choice, out _);
    Assert(status == ReactionMatchStatus.Success && choice == 0 && command == "/hug friend\n/wait 1",
        "the chosen block is the replacement, with $2 filled in");
    Assert(ReactionCommandMatcher.TryGenerateCommand(reaction.CustomRx, "please logout", reaction.ReplaceMatch, set, 0, null,
               out _, out _, out var noChoice, out _) == ReactionMatchStatus.NoMatch && noChoice == -1,
        "By word with an unknown word is no match, so $1 can't pick commands of its own");

    reaction.UseRegex = false;
    reaction.Rx = new Regex("please (\\w+)");
    Assert(ChoiceSet.From(reaction) == null && !ChoiceSelector.IsActive(reaction), "phrase triggers don't use choices");
    reaction.UseRegex = true;
    reaction.ChoiceMode = ChoiceMode.Off;
    Assert(ChoiceSet.From(reaction) == null, "choices that are off aren't used");
    reaction.ChoiceMode = ChoiceMode.ByWord;

    // Try it shows which choice, and treats each block's own /wait as the trigger's pause.
    var waits = new List<bool>();
    bool Check(Reaction r, string line, bool templateWait, out string reason)
    {
        if (line == "/wait")
            waits.Add(templateWait);
        var allowed = line != "/logout";
        reason = allowed ? "allowed" : "never runs";
        return allowed;
    }
    static bool IsEmote(string line) => line is "/hug" or "/wave";
    var preview = PluginUiLogic.BuildPreview(reaction, "please hug friend", IsEmote, Check);
    Assert(preview.Status == PreviewStatus.Matched && preview.Choice == "Choice 1 of 2 (word \"hug\")" &&
           preview.Lines[0].Command == "/hug motion" && waits.SequenceEqual([true]),
        $"Try it should name the choice and show its lines ({preview.Choice})");
    var waved = PluginUiLogic.BuildPreview(reaction, "please wave logout", IsEmote, Check);
    Assert(waved.Lines.Count == 2 && !waved.Lines[1].Allowed, "a capture in a choice is still checked like any command");
    Assert(PluginUiLogic.BuildPreview(reaction, "please dance", IsEmote, Check).Status == PreviewStatus.NoMatch,
        "Try it shows no match for an unknown word");

    reaction.ChoiceMode = ChoiceMode.InTurn;
    Assert(PluginUiLogic.BuildPreview(reaction, "please x", IsEmote, Check, turn: 3).Choice == "Choice 2 of 2 (next in turn)",
        "Try it shows the choice that's next in turn");
    reaction.ChoiceMode = ChoiceMode.Random;
    Assert(PluginUiLogic.BuildPreview(reaction, "please x", IsEmote, Check, random: _ => 1).Choice == "Choice 2 of 2 (picked at random)",
        "Try it says a random choice was picked");
    var all = PluginUiLogic.TestAllTriggers([reaction], "please x", 10, PluginUiLogic.TestSender(0, ""), IsEmote, Check,
        _ => 0, _ => 0);
    Assert(all.Count == 1 && all[0].Preview.Choice == "Choice 1 of 2 (picked at random)", "Test all triggers shows the choice too");

    var copy = PluginUiLogic.CloneReaction(reaction);
    copy.Choices[0].Commands = "/changed";
    Assert(reaction.Choices[0].Commands != "/changed" && copy.ChoiceMode == ChoiceMode.Random && copy.Choices.Count == 2,
        "a duplicated trigger gets its own copy of the choices");

    Console.WriteLine("PASS choices");
}

static void RunFinalActionTests()
{
    var commands = new List<string>();
    Assert(PluginUiLogic.AddFinalCommand(commands, "wave") && commands[0] == "/wave", "a final line gets its leading /");
    Assert(PluginUiLogic.AddFinalCommand(commands, "/wave"), "the same line twice is fine (a routine can repeat)");
    Assert(!PluginUiLogic.AddFinalCommand(commands, "/echo a\n/logout") && !PluginUiLogic.AddFinalCommand(commands, "  "),
        "blank or multi-line input isn't added");
    while (commands.Count < Reaction.MaxFinalCommands)
        PluginUiLogic.AddFinalCommand(commands, "/echo more");
    Assert(!PluginUiLogic.AddFinalCommand(commands, "/echo too many") && commands.Count == Reaction.MaxFinalCommands,
        "at most five final lines");

    var reaction = new Reaction
    {
        Name = "Wave",
        Enabled = true,
        UseRegex = true,
        CustomRx = new Regex(@"^please (\w+)$"),
        ReplaceMatch = "/$1",
        FinalCommands = [" ", "/echo done $1", "/logout\n/shutdown", "/wait 2", "/logout", "/a", "/b", "/c"],
        EnabledChannels = [10],
        Senders = SenderFilter.AnyoneFilter(),
    };
    Assert(PluginUiLogic.FinalCommandLines(reaction).SequenceEqual(["/echo done $1", "/wait 2", "/logout", "/a", "/b"]),
        "blank and multi-line final lines are skipped, and only five run");
    var templateWaits = new List<bool>();
    bool Check(Reaction r, string line, bool templateWait, out string reason)
    {
        if (line == "/wait")
            templateWaits.Add(templateWait);
        var allowed = line != "/logout";
        reason = allowed ? "allowed" : "never runs";
        return allowed;
    }
    var preview = PluginUiLogic.BuildPreview(reaction, "please wave", _ => false, Check);
    Assert(preview.FinalLines.Count == 5 && preview.FinalLines[0].Command == "/echo done $1",
        "the final action is sent as written: $1 isn't filled in");
    Assert(!preview.FinalLines[2].Allowed && templateWaits.SequenceEqual([true]),
        "final lines are checked like any command, and their own /wait is the trigger's pause");
    reaction.FinalCommands = null!;
    Assert(PluginUiLogic.FinalCommandLines(reaction).Length == 0, "no final action list means nothing runs");

    Console.WriteLine("PASS final action");
}

static void RunV6MigrationTests()
{
    var old = DalamudJson.Load("{\"Version\": 5, \"Reactions\": [{\"Name\": \"Old\", \"CooldownSeconds\": 5}]}");
    Assert(ConfigurationMigrator.MigrateAndNormalize(old) && old.Version == 6, "v5 should migrate to v6");
    var migrated = old.Reactions[0];
    Assert(migrated.PerSenderCooldownSeconds == 0 && !migrated.OneWaitingPerSender && migrated.ChoiceMode == ChoiceMode.Off &&
           migrated.Choices.Count == 0 && migrated.FinalCommands.Count == 0 && migrated.FinalWhen == FinalActionWhen.AfterEachRun &&
           migrated.CooldownSeconds == 5,
        "a v5 trigger should keep its settings, with the new ones off");

    var clamped = new Configuration();
    clamped.Reactions.Add(new Reaction { PerSenderCooldownSeconds = 99_999 });
    ConfigurationMigrator.MigrateAndNormalize(clamped);
    Assert(clamped.Reactions[0].PerSenderCooldownSeconds == Reaction.MaxPerSenderCooldownSeconds,
        "a too-long per-person cooldown should be clamped");

    Console.WriteLine("PASS v6 migration");
}
