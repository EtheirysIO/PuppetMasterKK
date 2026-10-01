using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using phys1ksUI;

namespace PuppetMaster.Windows;

internal sealed partial class MainWindow
{
    private static readonly string[] TriggerModes = ["Phrase", "Regex pattern"];
    private static readonly string[] CommandModes = ["Only listed commands", "Any game command"];
    private static readonly string[] NotificationModes = PluginUiLogic.NotificationSettingLabels;
    private static readonly string[] NewLineSeparators = ["\r\n", "\r", "\n"];

    private int selected;
    private string reactionSearch = string.Empty;
    private bool confirmDelete;
    private bool confirmAllowAll;
    private Reaction? allowAllTarget;
    private string allowInput = string.Empty;
    private string blockInput = string.Empty;
    private string namedInput = string.Empty;

    // The test preview is worked out when something it depends on changes, not every frame (a slow pattern would
    // otherwise run on the game thread 60 times a second).
    private readonly List<PreviewLine> preview = [];
    private Reaction? previewFor;
    private bool previewDirty = true;
    private string previewMatched = string.Empty;
    private string? previewError;
    private bool previewMatchedAny;

    private readonly record struct PreviewLine(string Command, bool Allowed, string Reason);

    private Reaction? SelectedReaction
        => selected >= 0 && selected < Config.Reactions.Count ? Config.Reactions[selected] : null;

    private void Select(int index)
    {
        selected = index;
        Config.CurrentReactionEdit = index;
        if (Service.IsValidReactionIndex(index))
            Service.InitializeRegex(index);
        allowInput = blockInput = namedInput = string.Empty;
        previewDirty = true;
        Changed();
    }

    /// <summary>A trigger, the replacement or the mode changed: rebuild the pattern and drop runs built from the old one.</summary>
    private void TriggerChanged(Reaction reaction)
    {
        Service.InitializeRegex(selected, true);
        ChatHandler.InvalidateReaction(reaction, false);
        previewDirty = true;
        Changed();
    }

    /// <summary>Something that decides what may run changed: stop what's running under the old rules.</summary>
    private void RulesChanged(Reaction reaction)
    {
        ChatHandler.InvalidateReaction(reaction, true);
        previewDirty = true;
        Changed();
    }

    // ───────────────────────── Page ─────────────────────────

    private void DrawReactionsPage()
    {
        var reactions = Config.Reactions;
        if (reactions.Count == 0 || SelectedReaction == null)
            Select(PluginUiLogic.EnsureReactionSelection(Config, selected));

        var avail = ImGui.GetContentRegionAvail();
        var listW = Theme.S(250f);
        var gap = Theme.Space.Gutter;

        if (ImGui.BeginChild("##reactionList", new Vector2(listW, avail.Y), false))
            DrawReactionList();
        ImGui.EndChild();

        ImGui.SameLine(0f, gap);
        if (ImGui.BeginChild("##reactionEditor", new Vector2(MathF.Max(1f, avail.X - listW - gap), avail.Y), false))
        {
            if (SelectedReaction is { } reaction)
                DrawReactionEditor(reaction);
        }
        ImGui.EndChild();
    }

    private void DrawReactionList()
    {
        if (W.IconTextButton(FontAwesomeIcon.Plus, "New reaction", ButtonKind.Primary, new Vector2(-1f, 0f)))
        {
            Config.Reactions.Add(Reaction.CreateDefault(
                commandWhitelist: Config.DefaultCommandWhitelist,
                commandBlacklist: Config.DefaultCommandBlacklist,
                allowAllCommands: Config.DefaultAllowAllCommands,
                motionOnly: Config.DefaultMotionOnly,
                enabledChannels: Config.DefaultEnabledChannels));
            Select(Config.Reactions.Count - 1);
        }
        Gap(2f);
        W.SearchBox("##reactionSearch", ref reactionSearch, "Search reactions", 0f, 100);
        Gap(2f);

        var activity = Activity;
        var shown = 0;
        var reactions = Config.Reactions;
        for (var index = 0; index < reactions.Count; index++)
        {
            var reaction = reactions[index];
            if (!PluginUiLogic.MatchesSearch(reaction, reactionSearch))
                continue;
            shown++;

            var running = IsRunning(activity, reaction);
            var (statusText, statusColor) = StatusOf(reaction);
            var trigger = reaction.UseRegex ? "Regex" : $"\"{reaction.TriggerPhrase}\"";
            var channels = reaction.EnabledChannels.Count == 1 ? "1 channel" : $"{reaction.EnabledChannels.Count} channels";
            ImGui.PushID(index);
            var clicked = W.ListRow("reaction", DisplayName(reaction), index == selected, $"{trigger} · {channels}",
                                    running ? Theme.Accent : statusColor, running, reaction.Enabled ? null : "Off",
                                    running ? "Running" : statusText);
            ImGui.PopID();
            if (clicked)
                Select(index);
        }
        if (shown == 0)
            Hint(reactions.Count == 0 ? "No reactions yet." : "No reactions match.");
    }

    private static bool IsRunning(ReactionVisualizerSnapshot activity, Reaction reaction)
    {
        if (activity.Active.Length == 0)
            return false;
        var id = ChatHandler.GetVisualizerId(reaction);
        foreach (var run in activity.Active)
        {
            if (run.ReactionId == id)
                return true;
        }
        return false;
    }

    private static (string Text, Vector4 Color) StatusOf(Reaction reaction) => PluginUiLogic.GetStatus(reaction) switch
    {
        ReactionUiStatus.Disabled => ("Off", Theme.Faint),
        ReactionUiStatus.InvalidTrigger => ("The trigger isn't valid", Theme.Negative),
        ReactionUiStatus.NoChannels => ("No channels picked", Theme.Warning),
        ReactionUiStatus.Unsafe => ("Anyone in a public channel can trigger it", Theme.Warning),
        _ => ("Ready", Theme.Positive),
    };

    // ───────────────────────── Header ─────────────────────────

    private float ReactionHeaderWidth()
    {
        if (SelectedReaction == null)
            return 0f;
        var gap = Theme.Space.Tight;
        return W.ToggleWidth("On##reactionOn") + gap * 2f + ImGui.GetFrameHeight() * 2f + gap;
    }

    private void DrawReactionHeader(HeaderSlot slot)
    {
        if (SelectedReaction is not { } reaction)
            return;
        var gap = Theme.Space.Tight;
        var h = ImGui.GetFrameHeight();
        ImGui.SetCursorScreenPos(new Vector2(slot.Max.X - ReactionHeaderWidth(), slot.CenterY - h * 0.5f));

        var enabled = reaction.Enabled;
        if (W.Toggle("On##reactionOn", ref enabled, tooltip: enabled ? "Turn this reaction off" : "Turn this reaction on"))
        {
            PluginUiLogic.SetReactionEnabled(reaction, enabled, ChatHandler.CancelReaction);
            Changed();
        }
        ImGui.SameLine(0f, gap * 2f);
        if (W.IconButton(FontAwesomeIcon.Copy, "##duplicate", "Duplicate this reaction"))
        {
            Config.Reactions.Insert(selected + 1, PluginUiLogic.CloneReaction(reaction));
            Select(selected + 1);
        }
        ImGui.SameLine(0f, gap);
        var canDelete = Config.Reactions.Count > 1;
        if (W.IconButton(FontAwesomeIcon.Trash, "##delete", canDelete ? "Delete this reaction" : "The last reaction can't be deleted",
                         danger: true, enabled: canDelete))
            confirmDelete = true;
    }

    private void DrawReactionDialogs()
    {
        if (SelectedReaction is { } reaction &&
            Modal.Confirm("Delete reaction##confirmDelete", ref confirmDelete, $"Delete \"{DisplayName(reaction)}\"?",
                          "Delete", danger: true, detail: "This can't be undone.") &&
            PluginUiLogic.TryDeleteReaction(Config.Reactions, selected, out var next, ChatHandler.CancelReaction))
            Select(next);

        if (Modal.Confirm("Allow any game command##confirmAllowAll", ref confirmAllowAll,
                          allowAllTarget == null
                              ? "Let new reactions run any game command that isn't blocked?"
                              : "Let this reaction run any game command that isn't blocked?", "Allow",
                          detail: "Chat and plugin commands still have to be listed one by one, and /logout, /shutdown, " +
                                  "/puppetmaster and /xl… never run. Use this only with senders and channels you trust.") &&
            true)
        {
            if (allowAllTarget != null)
            {
                allowAllTarget.AllowAllCommands = true;
                RulesChanged(allowAllTarget);
            }
            else
            {
                Config.DefaultAllowAllCommands = true;
                Changed();
            }
        }
        if (!confirmAllowAll)
            allowAllTarget = null;
    }

    // ───────────────────────── Editor ─────────────────────────

    private void DrawReactionEditor(Reaction reaction)
    {
        using (W.Card("name", "Name"))
        {
            var name = reaction.Name;
            if (W.TextInput("##reactionName", ref name, "Name this reaction", 0f, 100))
            {
                reaction.Name = name;
                Changed();
            }
            var (statusText, statusColor) = StatusOf(reaction);
            if (PluginUiLogic.GetStatus(reaction) is not ReactionUiStatus.Ready and not ReactionUiStatus.Disabled)
            {
                Gap(2f);
                W.Chip(statusText, statusColor);
            }
        }

        DrawTriggerCard(reaction);
        DrawSendersCard("reactionSenders", reaction.Senders, () => RulesChanged(reaction),
                        PluginUiLogic.ListensToStrangers(reaction));
        DrawChannelsCard(reaction);
        DrawCommandsCard(reaction);
        DrawTestCard(reaction);
        DrawTimingCard(reaction);
        DrawNotificationsCard(reaction);
    }

    private void DrawTriggerCard(Reaction reaction)
    {
        using (W.Card("trigger", "Trigger", reaction.UseRegex ? "Regex pattern" : "Phrase"))
        {
            var mode = reaction.UseRegex ? 1 : 0;
            if (W.Segmented("##triggerMode", TriggerModes, ref mode, W.SegmentedWidth(TriggerModes)))
            {
                PluginUiLogic.SetRegexMode(reaction, mode == 1);
                TriggerChanged(reaction);
            }
            Gap();

            if (!reaction.UseRegex)
            {
                var phrase = reaction.TriggerPhrase;
                if (W.TextInput("##phrase", ref phrase, "please do", 0f, Config.MaxRegexLength,
                                error: string.IsNullOrWhiteSpace(phrase)))
                {
                    reaction.TriggerPhrase = phrase;
                    TriggerChanged(reaction);
                }
                Hint("Reacts to the phrase followed by a command in brackets or a single word: \"please do (dance)\" or " +
                     "\"please do wave\". Separate several phrases with |.");
                return;
            }

            var pattern = reaction.CustomPhrase;
            var invalid = !string.IsNullOrWhiteSpace(pattern) && reaction.CustomRx == null;
            if (W.TextInput("##pattern", ref pattern, "Regular expression", 0f, Config.MaxRegexLength, error: invalid))
            {
                reaction.CustomPhrase = pattern;
                TriggerChanged(reaction);
            }
            if (invalid)
                W.TextWrapped("This isn't a valid regular expression.", Theme.Negative);

            Gap();
            Label("Commands to run");
            var replacement = reaction.ReplaceMatch;
            if (W.TextArea("##replacement", ref replacement, 0f, 0f, "/$1", 500))
            {
                reaction.ReplaceMatch = replacement;
                TriggerChanged(reaction);
            }
            Hint("$1, $2… are the pattern's groups. One command per line; \"/wait 2\" pauses for two seconds.");
            Gap(2f);
            if (W.SecondaryButton("Restore defaults##regexDefaults", tooltip: "Use the pattern and commands the phrase mode would use"))
            {
                PluginUiLogic.EnsureRegexRestoreTrigger(reaction);
                reaction.CustomPhrase = Service.GetDefaultRegex(selected);
                reaction.ReplaceMatch = Service.GetDefaultReplaceMatch();
                TriggerChanged(reaction);
            }
        }
    }

    private void DrawSendersCard(string id, SenderFilter senders, Action changed, bool strangerWarning)
    {
        using (W.Card(id, "Who can trigger it", senders.Describe()))
        {
            var anyone = senders.Anyone;
            if (W.Toggle("Anyone##anyone", ref anyone, tooltip: "Every player who can talk in the picked channels"))
            {
                senders.Anyone = anyone;
                // Leaving Anyone with nothing else picked would let nobody trigger it: start from the safe groups.
                if (!anyone && !senders.Friends && !senders.FreeCompany && !senders.Party && senders.Named.Count == 0)
                    senders.Friends = senders.FreeCompany = senders.Party = true;
                changed();
            }

            if (!senders.Anyone)
            {
                Gap(2f);
                var friends = senders.Friends;
                if (W.Toggle("Friends##friends", ref friends))
                {
                    senders.Friends = friends;
                    changed();
                }
                ImGui.SameLine(0f, Theme.S(20f));
                var fc = senders.FreeCompany;
                if (W.Toggle("Free Company##fc", ref fc,
                             tooltip: "Your FC chat, and FC members the game has listed this session (open the FC member list once)"))
                {
                    senders.FreeCompany = fc;
                    changed();
                }
                ImGui.SameLine(0f, Theme.S(20f));
                var party = senders.Party;
                if (W.Toggle("Party and alliance##party", ref party))
                {
                    senders.Party = party;
                    changed();
                }

                Gap();
                Label("Also these players");
                if (StringListEditor("named", senders.Named, ref namedInput, "Name@World (or just Name for any world)",
                                     "Nobody else.", AddNamed))
                    changed();
            }

            if (strangerWarning)
            {
                Gap(2f);
                W.Banner("Anyone who can talk in Say, Shout, Yell, Tell, Party or Novice Network can trigger this reaction.",
                         Theme.Warning, icon: FontAwesomeIcon.ExclamationTriangle);
            }
        }

        bool AddNamed(string input)
        {
            var name = input.Trim();
            if (name.Length == 0)
                return false;
            foreach (var existing in senders.Named)
            {
                if (existing.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            senders.Named.Add(name);
            return true;
        }
    }

    private void DrawCommandsCard(Reaction reaction)
    {
        var note = reaction.AllowAllCommands ? "Any game command" : $"{reaction.CommandWhitelist.Count} allowed";
        using (W.Card("commands", "Commands", note))
        {
            var motionOnly = reaction.MotionOnly;
            if (W.Toggle("Hide emote text##motionOnly", ref motionOnly,
                         tooltip: "The animation still plays, but the emote's chat line isn't posted"))
            {
                reaction.MotionOnly = motionOnly;
                RulesChanged(reaction);
            }

            Gap();
            Label("Which commands can run?");
            var mode = reaction.AllowAllCommands ? 1 : 0;
            if (W.Segmented("##commandMode", CommandModes, ref mode, W.SegmentedWidth(CommandModes)))
            {
                if (mode == 1)
                {
                    allowAllTarget = reaction;
                    confirmAllowAll = true;
                }
                else
                {
                    reaction.AllowAllCommands = false;
                    RulesChanged(reaction);
                }
            }

            // Shown in both modes: with "Any game command", chat and plugin commands still have to be listed here.
            Gap();
            W.Heading(reaction.AllowAllCommands ? "Also allowed (chat and plugin commands)" : "Allowed");
            if (StringListEditor("allow", reaction.CommandWhitelist, ref allowInput, "/command",
                                 reaction.AllowAllCommands ? "None." : "None. Emotes still run.",
                                 input => PluginUiLogic.AddCommandRule(reaction.CommandWhitelist, reaction.CommandBlacklist, input)))
                RulesChanged(reaction);

            Gap();
            W.Heading("Blocked");
            if (StringListEditor("block", reaction.CommandBlacklist, ref blockInput, "/command", "Nothing blocked.",
                                 input => PluginUiLogic.AddCommandRule(reaction.CommandBlacklist, reaction.CommandWhitelist, input)))
                RulesChanged(reaction);

            Gap(2f);
            Hint("Emotes always run unless blocked. Chat commands (say, shout, tell, party, FC…) and other plugins' commands " +
                 "only run when listed as allowed. /logout, /shutdown, /puppetmaster and /xl… never run.");
        }
    }

    private void DrawTestCard(Reaction reaction)
    {
        using (W.Card("test", "Try it"))
        {
            var test = reaction.TestInput;
            if (W.TextInput("##testInput", ref test, "Type a chat message, e.g. please do (dance)", 0f, 500))
            {
                reaction.TestInput = test;
                previewDirty = true;
                Changed();
            }

            if (previewDirty || !ReferenceEquals(previewFor, reaction))
                BuildPreview(reaction);

            if (string.IsNullOrWhiteSpace(reaction.TestInput))
            {
                Hint("See what a message would run before anyone sends it.");
                return;
            }
            Gap(2f);
            if (previewError != null)
            {
                W.Chip("Commands can't be built", Theme.Negative, status: true);
                W.TextWrapped(previewError, Theme.Dim);
                return;
            }
            if (!previewMatchedAny)
            {
                W.Chip("No match", Theme.Warning);
                return;
            }

            foreach (var line in preview)
            {
                W.Chip(line.Allowed ? "Runs" : "Blocked", line.Allowed ? Theme.Positive : Theme.Negative, status: true);
                if (ImGui.IsItemHovered())
                    W.Tooltip(line.Reason);
                ImGui.SameLine();
                ImGui.TextUnformatted(line.Command);
            }
            if (reaction.UseRegex)
                Hint($"Matched: {previewMatched}");
        }
    }

    private void BuildPreview(Reaction reaction)
    {
        previewFor = reaction;
        previewDirty = false;
        preview.Clear();
        previewError = null;
        previewMatched = string.Empty;
        previewMatchedAny = false;
        if (string.IsNullOrWhiteSpace(reaction.TestInput))
            return;

        var status = ReactionCommandMatcher.TryGenerateCommand(
            ReactionCommandMatcher.SelectPattern(reaction),
            reaction.TestInput,
            reaction.UseRegex ? reaction.ReplaceMatch : Service.GetDefaultReplaceMatch(),
            out var command,
            out var matched,
            out var error);
        if (status == ReactionMatchStatus.InvalidReplacement)
        {
            previewError = error ?? "The commands can't be built from this pattern.";
            return;
        }
        if (status == ReactionMatchStatus.TimedOut)
        {
            previewError = "The pattern took too long on this message.";
            return;
        }
        if (status != ReactionMatchStatus.Success)
            return;

        previewMatchedAny = true;
        previewMatched = matched;
        foreach (var line in command.Split(NewLineSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var parsed = Service.FormatCommand(line);
            if (string.IsNullOrWhiteSpace(parsed.Main))
                continue;
            if (reaction.MotionOnly && Service.Commands.IsEmote(parsed.Main))
                parsed.Args = "motion";
            var allowed = Service.IsCommandAllowed(reaction, parsed.Main, out var reason);
            preview.Add(new PreviewLine(parsed.ToString(), allowed, Capitalize(reason)));
        }
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private void DrawTimingCard(Reaction reaction)
    {
        var policyIndex = Array.FindIndex(PluginUiLogic.ExecutionPolicyOptions, option => option.Policy == reaction.ExecutionPolicy);
        var note = policyIndex >= 0 ? PluginUiLogic.ExecutionPolicyLabels[policyIndex] : string.Empty;
        using (W.FoldCard("timing", "Repeats and cooldown", note, out var open, defaultOpen: false))
        {
            if (!open)
                return;
            Label("If it's triggered again while it's still running");
            var index = Math.Max(0, policyIndex);
            if (W.Combo("##policy", PluginUiLogic.ExecutionPolicyLabels, ref index, Theme.S(260f)))
            {
                reaction.ExecutionPolicy = PluginUiLogic.ExecutionPolicyOptions[index].Policy;
                ChatHandler.InvalidateReaction(reaction, false);
                Changed();
            }
            Hint(PluginUiLogic.GetExecutionPolicyDescription(reaction.ExecutionPolicy));

            Gap();
            Label("Cooldown");
            var ignores = PluginUiLogic.IgnoresCooldown(reaction.ExecutionPolicy);
            ImGui.BeginDisabled(ignores);
            var cooldown = reaction.CooldownSeconds;
            if (W.NumberInput("##cooldown", ref cooldown, 0, 86400, 1, Theme.S(160f), "seconds"))
            {
                reaction.CooldownSeconds = PluginUiLogic.ClampCooldown(cooldown);
                ChatHandler.InvalidateReaction(reaction, false);
                Changed();
            }
            ImGui.EndDisabled();
            Hint(PluginUiLogic.GetCooldownDescription(reaction.ExecutionPolicy));
        }
    }

    private void DrawNotificationsCard(Reaction reaction)
    {
        using (W.FoldCard("notifications", "Notifications", null, out var open, defaultOpen: false))
        {
            if (!open)
                return;
            var progress = (int)reaction.ProgressNotifications;
            Label("While it runs and when it ends");
            if (W.Segmented("##progressNotifications", NotificationModes, ref progress, 0f))
            {
                reaction.ProgressNotifications = (ReactionNotificationSetting)progress;
                ChatHandler.InvalidateReaction(reaction, false);
                Changed();
            }

            Gap();
            var suppressed = (int)reaction.SuppressedNotifications;
            Label("When a trigger is ignored (busy or cooling down)");
            if (W.Segmented("##suppressedNotifications", NotificationModes, ref suppressed, 0f))
            {
                reaction.SuppressedNotifications = (ReactionNotificationSetting)suppressed;
                ChatHandler.InvalidateReaction(reaction, false);
                Changed();
            }
            Hint("Default follows Settings > General.");
        }
    }

    // ───────────────────────── String list editor ─────────────────────────

    /// <summary>
    /// A list of short strings (commands, player names): one row each with a remove button, then an input and Add
    /// (Enter adds too). <paramref name="tryAdd"/> normalizes and adds, returning false for nothing to add. Returns
    /// true when the list changed.
    /// </summary>
    private static bool StringListEditor(string id, List<string> items, ref string input, string hint, string emptyText,
        Func<string, bool> tryAdd)
    {
        var changed = false;
        ImGui.PushID(id);
        {
            var remove = -1;
            if (items.Count == 0)
                ImGui.TextColored(Theme.Faint, emptyText);
            for (var i = 0; i < items.Count; i++)
            {
                ImGui.PushID(i);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(items[i]);
                W.RightAlign(ImGui.GetFrameHeight());
                if (W.IconButton(FontAwesomeIcon.Times, "##remove", "Remove", danger: true))
                    remove = i;
                ImGui.PopID();
            }
            if (remove >= 0)
            {
                items.RemoveAt(remove);
                changed = true;
            }

            var addW = W.ButtonWidth("Add");
            var submitted = W.TextInput("##add", ref input, hint, -(addW + Theme.Space.Tight), 100,
                                        flags: ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine(0f, Theme.Space.Tight);
            var clicked = W.SecondaryButton("Add##addButton", new Vector2(addW, ImGui.GetFrameHeight()),
                                            enabled: !string.IsNullOrWhiteSpace(input));
            if ((submitted || clicked) && !string.IsNullOrWhiteSpace(input))
            {
                if (tryAdd(input))
                    changed = true;
                input = string.Empty;
            }
        }
        // Not in a finally: after a throw the kit's recovery pops the ID stack itself.
        ImGui.PopID();
        return changed;
    }
}
