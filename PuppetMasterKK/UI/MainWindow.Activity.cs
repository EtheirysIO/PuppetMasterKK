using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using phys1ksUI;

namespace PuppetMasterKK.UI;

internal sealed partial class MainWindow
{
    private static readonly string[] RosterFilters = ["All", "Busy", "Ready", "Needs attention", "Off"];
    private int rosterFilter;

    private void DrawActivityPage()
    {
        var activity = Activity;

        using (W.Card("activityStats", null))
        {
            W.Stat(activity.Active.Length.ToString(), "Running", activity.Active.Length > 0 ? Theme.Accent : null);
            ImGui.SameLine(0f, Theme.S(40f));
            W.Stat(activity.Queued.Length.ToString(), "Waiting", activity.Queued.Length > 0 ? Theme.Warning : null);
            ImGui.SameLine(0f, Theme.S(40f));
            W.Stat((ChatHandler.DroppedMessageCount + ChatHandler.DroppedRetriggerCount).ToString(), "Discarded");
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
                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextColored(Theme.Dim, W.Fit(FirstLine(run.Command), ImGui.GetContentRegionAvail().X));
                        if (ImGui.IsItemHovered())
                            W.Tooltip(run.Command);
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
                using var table = W.Table("##queuedTable", 3);
                if (table.Open)
                {
                    ImGui.TableSetupColumn("Trigger", ImGuiTableColumnFlags.WidthStretch, 1f);
                    ImGui.TableSetupColumn("Command", ImGuiTableColumnFlags.WidthStretch, 2f);
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
                        var (text, color) = run.Status switch
                        {
                            VisualizerRunStatus.Cancelled => ("Stopped", Theme.Negative),
                            VisualizerRunStatus.Disabled => ("Turned off", Theme.Faint),
                            _ => ("Done", Theme.Positive),
                        };
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        W.Chip(text, color, status: true);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(run.ReactionName);
                        if (ImGui.IsItemHovered())
                            W.Tooltip(run.Command);
                        ImGui.TableNextColumn();
                        var took = (run.FinishedAt ?? run.StartedAt) - run.StartedAt;
                        ImGui.TextColored(Theme.Dim, took.TotalSeconds >= 1 ? $"{took.TotalSeconds:0.0} s" : $"{took.TotalMilliseconds:0} ms");
                        ImGui.TableNextColumn();
                        ImGui.TextColored(Theme.Dim, run.StartedAt.ToString("HH:mm:ss"));
                    }
                }
            }
        }

        DrawRoster(activity);
    }

    private void DrawRoster(ReactionVisualizerSnapshot activity)
    {
        using (W.Card("roster", "All triggers", $"{Config.Reactions.Count}"))
        {
            W.Segmented("##rosterFilter", RosterFilters, ref rosterFilter, 0f);
            Gap(4f);
            using var table = W.Table("##rosterTable", 4);
            if (!table.Open)
                return;
            ImGui.TableSetupColumn("Trigger", ImGuiTableColumnFlags.WidthStretch, 1f);
            W.FixedColumn("State", 150f);
            W.FixedColumn("Running", 64f);
            W.FixedColumn("Waiting", 64f);
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
            }
        }
    }

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
