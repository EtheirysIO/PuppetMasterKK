using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using phys1ksUI;

namespace PuppetMasterKK.UI;

internal sealed partial class MainWindow
{
    private static readonly string[] RosterFilters = ["All", "Busy", "Ready", "Needs attention", "Off"];
    private int rosterFilter;

    // "Test all triggers": rebuilt when an input changes, and every couple of seconds (plugin commands come and go).
    private string testAllMessage = string.Empty;
    private int testAllChannel;
    private int testAllSender;
    private string testAllName = string.Empty;
    private List<TriggerTestResult> testAllResults = [];
    private bool testAllDirty = true;
    private long testAllBuiltAt;

    private void DrawActivityPage()
    {
        var activity = Activity;

        DrawPracticeCard();

        using (W.Card("activityStats", null))
        {
            var totals = ReactionVisualizerState.TotalCounters();
            W.Stat(activity.Active.Length.ToString(), "Running", activity.Active.Length > 0 ? Theme.Accent : null);
            ImGui.SameLine(0f, Theme.S(40f));
            W.Stat(activity.Queued.Length.ToString(), "Waiting", activity.Queued.Length > 0 ? Theme.Warning : null);
            ImGui.SameLine(0f, Theme.S(40f));
            W.Stat(totals.Ignored.ToString(), "Ignored");
            if (ImGui.IsItemHovered())
                W.Tooltip($"Requests ignored this session:\n{totals.IgnoredBusy} while busy\n{totals.IgnoredCooldown} while cooling down");
            ImGui.SameLine(0f, Theme.S(40f));
            W.Stat(totals.Replaced.ToString(), "Replaced");
            if (ImGui.IsItemHovered())
                W.Tooltip("Waiting requests dropped for a newer one (Queue latest trigger, Restart immediately, one waiting request per person).");
            ImGui.SameLine(0f, Theme.S(40f));
            var droppedMessages = ChatHandler.DroppedMessageCount;
            var droppedRequests = ChatHandler.DroppedRetriggerCount;
            W.Stat((droppedMessages + droppedRequests).ToString(), "Discarded");
            if (ImGui.IsItemHovered())
                W.Tooltip($"Dropped under load:\n{droppedMessages} messages (too many at once)\n{droppedRequests} waiting requests (over 16 for one trigger)");
        }

        using (W.Card("running", "Running now"))
        {
            if (activity.Active.Length == 0)
                ImGui.TextColored(Theme.Faint, "Nothing is running.");
            else
            {
                using var table = W.Table("##runningTable", 4);
                if (table.Open)
                {
                    ImGui.TableSetupColumn("Trigger", ImGuiTableColumnFlags.WidthStretch, 1f);
                    ImGui.TableSetupColumn("Command", ImGuiTableColumnFlags.WidthStretch, 2f);
                    W.FixedColumn("Started", 80f);
                    W.FixedColumn("##stop", 40f);
                    W.TableHeaders(trackedCaps: true);
                    foreach (var run in activity.Active)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted(run.ReactionName);
                        PracticeChip(run);
                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextColored(Theme.Dim, W.Fit(FirstLine(run.Command), ImGui.GetContentRegionAvail().X));
                        if (ImGui.IsItemHovered())
                            W.Tooltip(RunTooltip(run));
                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextColored(Theme.Dim, run.StartedAt.ToString("HH:mm:ss"));
                        ImGui.TableNextColumn();
                        ImGui.PushID((int)run.Id);
                        if (W.IconButton(FontAwesomeIcon.Stop, "##stop", "Stop this trigger", danger: true))
                            StopReaction(run.ReactionId);
                        ImGui.PopID();
                    }
                }
            }
        }

        using (W.Card("waiting", "Waiting to run", activity.Queued.Length > 0 ? $"{activity.Queued.Length}" : null))
        {
            if (activity.Queued.Length == 0)
                ImGui.TextColored(Theme.Faint, "Nothing is waiting.");
            else
            {
                using var table = W.Table("##queuedTable", 4);
                if (table.Open)
                {
                    ImGui.TableSetupColumn("Trigger", ImGuiTableColumnFlags.WidthStretch, 1f);
                    ImGui.TableSetupColumn("Command", ImGuiTableColumnFlags.WidthStretch, 2f);
                    ImGui.TableSetupColumn("From", ImGuiTableColumnFlags.WidthStretch, 1f);
                    W.FixedColumn("Since", 80f);
                    W.TableHeaders(trackedCaps: true);
                    foreach (var queued in activity.Queued)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(queued.ReactionName);
                        ImGui.TableNextColumn();
                        ImGui.TextColored(Theme.Dim, W.Fit(FirstLine(queued.Command), ImGui.GetContentRegionAvail().X));
                        if (ImGui.IsItemHovered())
                            W.Tooltip(queued.Command);
                        ImGui.TableNextColumn();
                        if (queued.From.Length == 0)
                            ImGui.TextColored(Theme.Faint, "Unknown");
                        else
                            ImGui.TextColored(Theme.Dim, W.Fit(queued.From, ImGui.GetContentRegionAvail().X));
                        ImGui.TableNextColumn();
                        ImGui.TextColored(Theme.Dim, queued.QueuedAt.ToString("HH:mm:ss"));
                    }
                }
            }
        }

        using (W.Card("recent", "Recently finished"))
        {
            if (activity.Recent.Length == 0)
                ImGui.TextColored(Theme.Faint, "Finished triggers show up here.");
            else
            {
                using var table = W.Table("##recentTable", 4);
                if (table.Open)
                {
                    W.FixedColumn("Result", 110f);
                    ImGui.TableSetupColumn("Trigger", ImGuiTableColumnFlags.WidthStretch, 1f);
                    W.FixedColumn("Took", 80f);
                    W.FixedColumn("At", 80f);
                    W.TableHeaders(trackedCaps: true);
                    foreach (var run in activity.Recent)
                    {
                        var (text, color, why) = run.Status switch
                        {
                            VisualizerRunStatus.Cancelled => ("Stopped", Theme.Negative, "Stopped before it finished."),
                            VisualizerRunStatus.Disabled => ("Turned off", Theme.Faint, "Stopped because the trigger was turned off."),
                            VisualizerRunStatus.Interrupted => ("Interrupted", Theme.Warning, "Stopped by a newer request (Restart immediately)."),
                            VisualizerRunStatus.Replaced => ("Replaced", Theme.Faint, "Never ran: a newer request took its place while it waited."),
                            _ => ("Done", Theme.Positive, null),
                        };
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        W.Chip(text, color, status: true);
                        if (why != null && ImGui.IsItemHovered())
                            W.Tooltip(why);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(run.ReactionName);
                        if (ImGui.IsItemHovered())
                            W.Tooltip(RunTooltip(run));
                        PracticeChip(run);
                        ImGui.TableNextColumn();
                        var took = (run.FinishedAt ?? run.StartedAt) - run.StartedAt;
                        ImGui.TextColored(Theme.Dim, took.TotalSeconds >= 1 ? $"{took.TotalSeconds:0.0} s" : $"{took.TotalMilliseconds:0} ms");
                        ImGui.TableNextColumn();
                        ImGui.TextColored(Theme.Dim, run.StartedAt.ToString("HH:mm:ss"));
                    }
                }
            }
        }

        DrawTestAllCard();
        DrawRoster(activity);
    }

    private static void DrawPracticeCard()
    {
        using (W.Card("practice", null))
        {
            var practice = Config.PracticeMode;
            if (W.Toggle("Practice mode##practiceMode", ref practice,
                         tooltip: "Triggers match, wait and queue as usual, but nothing is sent to the game"))
                ChatHandler.SetPracticeMode(Config, practice);
            Hint("Nothing is sent: runs show what they would have sent. Follow mode, Mimic and Emote replies still work. Turning it on or off stops every trigger. Off when the plugin starts.");
        }
    }

    private static void PracticeChip(VisualizerRunSnapshot run)
    {
        if (!run.Practice)
            return;
        ImGui.SameLine();
        W.Chip("Practice", Theme.Accent);
        if (ImGui.IsItemHovered())
            W.Tooltip(RunTooltip(run));
    }

    private static string RunTooltip(VisualizerRunSnapshot run)
    {
        if (!run.Practice)
            return run.Command;
        var text = new StringBuilder(run.Command).Append("\n\nPractice mode, nothing was sent.");
        if (run.WouldSend.Length > 0)
        {
            text.Append(" Would have sent:");
            foreach (var line in run.WouldSend)
                text.Append('\n').Append(line);
        }
        return text.ToString();
    }

    private void DrawTestAllCard()
    {
        using (W.FoldCard("testAll", "Test all triggers", null, out var open, defaultOpen: false))
        {
            if (!open)
                return;
            var channels = Config.Reactions.SelectMany(reaction => reaction.EnabledChannels ?? []).Distinct().Order().ToArray();
            if (channels.Length == 0)
            {
                Hint("No trigger is listening to any channel yet.");
                return;
            }
            testAllChannel = Math.Clamp(testAllChannel, 0, channels.Length - 1);

            if (W.TextInput("##testAllMessage", ref testAllMessage, "Type a chat message, e.g. please do (dance)", 0f, 500))
                testAllDirty = true;
            Gap(2f);
            if (W.Combo("##testAllChannel", channels.Select(ChannelName).ToArray(), ref testAllChannel, Theme.S(180f)))
                testAllDirty = true;
            ImGui.SameLine(0f, Theme.Space.Tight);
            if (W.Combo("##testAllSender", PluginUiLogic.TestSenderLabels, ref testAllSender, Theme.S(180f)))
                testAllDirty = true;
            ImGui.SameLine(0f, Theme.Space.Tight);
            if (W.TextInput("##testAllName", ref testAllName, NameHint, 0f, 100))
                testAllDirty = true;

            if (string.IsNullOrWhiteSpace(testAllMessage))
            {
                Hint("See which triggers a message would set off, and what each would run. Nothing is sent.");
                return;
            }
            if (testAllDirty || Environment.TickCount64 - testAllBuiltAt > 2000)
            {
                testAllResults = PluginUiLogic.TestAllTriggers(Config.Reactions, testAllMessage, channels[testAllChannel],
                    PluginUiLogic.TestSender(testAllSender, testAllName), Service.Commands.IsEmote, Service.IsCommandAllowed,
                    ChatHandler.GetChoiceTurn, PreviewRandom(testAllMessage));
                testAllDirty = false;
                testAllBuiltAt = Environment.TickCount64;
            }

            Gap(2f);
            if (testAllResults.Count == 0)
            {
                W.Chip("No trigger matches", Theme.Warning);
                return;
            }
            foreach (var result in testAllResults)
            {
                if (result.Index >= Config.Reactions.Count)
                    continue;
                var reaction = Config.Reactions[result.Index];
                Gap(4f);
                ImGui.PushID(result.Index);
                if (W.Link(DisplayName(reaction), "Open in the editor"))
                {
                    Select(result.Index);
                    page = Page.Reactions;
                }
                ImGui.PopID();
                var (text, color) = result.Outcome switch
                {
                    TriggerTestOutcome.Off => ("Off", Theme.Faint),
                    TriggerTestOutcome.NotOnChannel => ("Not listening here", Theme.Faint),
                    TriggerTestOutcome.SenderNotAllowed => ("Not from this sender", Theme.Faint),
                    _ => ("Fires", Theme.Positive),
                };
                ImGui.SameLine();
                W.Chip(text, color, status: true);
                DrawPreview(result.Preview, false);
            }
            Gap(2f);
            Hint("Follow mode and Mimic take their own lines first, and your own messages are ignored if that's on in Settings.");
        }
    }

    private void DrawRoster(ReactionVisualizerSnapshot activity)
    {
        using (W.Card("roster", "All triggers", $"{Config.Reactions.Count}"))
        {
            W.Segmented("##rosterFilter", RosterFilters, ref rosterFilter, 0f);
            Gap(4f);
            using var table = W.Table("##rosterTable", 5);
            if (!table.Open)
                return;
            ImGui.TableSetupColumn("Trigger", ImGuiTableColumnFlags.WidthStretch, 1f);
            W.FixedColumn("State", 150f);
            W.FixedColumn("Running", 64f);
            W.FixedColumn("Waiting", 64f);
            W.FixedColumn("Ignored", 64f);
            W.TableHeaders(trackedCaps: true);
            var reactions = Config.Reactions;
            for (var index = 0; index < reactions.Count; index++)
            {
                var reaction = reactions[index];
                var id = ChatHandler.GetVisualizerId(reaction);
                var running = 0;
                var waiting = 0;
                foreach (var run in activity.Active)
                {
                    if (run.ReactionId == id)
                        running++;
                }
                foreach (var queued in activity.Queued)
                {
                    if (queued.ReactionId == id)
                        waiting++;
                }

                var status = PluginUiLogic.GetStatus(reaction);
                var busy = running > 0 || waiting > 0;
                var show = rosterFilter switch
                {
                    1 => busy,
                    2 => !busy && status == ReactionUiStatus.Ready,
                    3 => status is ReactionUiStatus.InvalidTrigger or ReactionUiStatus.NoChannels or ReactionUiStatus.Unsafe or ReactionUiStatus.NoProtections,
                    4 => status == ReactionUiStatus.Disabled,
                    _ => true,
                };
                if (!show)
                    continue;

                var (text, color) = busy ? (running > 0 ? "Running" : "Waiting", Theme.Accent) : StatusOf(reaction);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.PushID(index);
                if (W.Link(DisplayName(reaction), "Open in the editor"))
                {
                    Select(index);
                    page = Page.Reactions;
                }
                ImGui.PopID();
                ImGui.TableNextColumn();
                W.Chip(W.Fit(text, Theme.S(140f)), color);
                if (ImGui.IsItemHovered())
                    W.Tooltip(text);
                ImGui.TableNextColumn();
                ImGui.TextColored(running > 0 ? Theme.Ink : Theme.Faint, running.ToString());
                ImGui.TableNextColumn();
                ImGui.TextColored(waiting > 0 ? Theme.Ink : Theme.Faint, waiting.ToString());
                ImGui.TableNextColumn();
                var counts = ReactionVisualizerState.Counters(id);
                ImGui.TextColored(counts.Ignored > 0 ? Theme.Ink : Theme.Faint, counts.Ignored.ToString());
                if (ImGui.IsItemHovered())
                    W.Tooltip(CountsTooltip(counts));
            }
        }
    }

    private static string CountsTooltip(VisualizerCounts c) =>
        $"This session:\n" +
        $"{c.Started} started, {c.Completed} done, {c.Stopped} stopped, {c.Interrupted} interrupted\n" +
        $"Ignored: {c.IgnoredBusy} while busy, {c.IgnoredCooldown} while cooling down\n" +
        $"{c.Replaced} replaced by a newer request, {c.DiscardedFull} discarded (queue full)\n" +
        $"{c.BlockedLines} blocked lines, {c.TimedOut} pattern timeouts";

    private static void StopReaction(long visualizerId)
    {
        foreach (var reaction in Config.Reactions)
        {
            if (ChatHandler.GetVisualizerId(reaction) == visualizerId)
            {
                ChatHandler.CancelReaction(reaction);
                return;
            }
        }
    }
}
