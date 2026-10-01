using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using phys1ksUI;

namespace PuppetMasterKK.UI;

internal sealed partial class MainWindow
{
    // Channel colors in the log: categorical, so a row's channel can be told apart at a glance.
    private static readonly Vector4[] LogChannelColors =
    [
        Theme.Hex(0x73bfff), Theme.Hex(0x73e0a3), Theme.Hex(0xf0ad61), Theme.Hex(0xcc94f5),
        Theme.Hex(0xf58aa3), Theme.Hex(0x80d6db), Theme.Hex(0xe0d66b), Theme.Hex(0xadb3f5),
    ];

    private bool colorLogs = true;
    private bool autoScrollLogs = true;
    private long shownLogRevision = -1;
    private long cachedLogRevision = -1;
    private DebugLogEntry[] logEntries = [];

    private float LogsHeaderWidth() => W.ButtonWidth("Save to file") + W.ButtonWidth("Clear") + Theme.Space.Tight;

    private void DrawLogsHeader(HeaderSlot slot)
    {
        var h = Theme.Space.ButtonHeight;
        ImGui.SetCursorScreenPos(new Vector2(slot.Max.X - LogsHeaderWidth(), slot.CenterY - h * 0.5f));
        var any = logEntries.Length > 0;
        if (W.SecondaryButton("Save to file", enabled: any, tooltip: "Save the captured messages to a text file"))
            SaveLogs();
        ImGui.SameLine(0f, Theme.Space.Tight);
        var anyDiscarded = ChatHandler.DroppedMessageCount > 0 || ChatHandler.DroppedRetriggerCount > 0;
        if (W.SecondaryButton("Clear", enabled: any || anyDiscarded, tooltip: "Clear the captured messages and the discarded counts"))
        {
            DebugLogBuffer.Clear();
            ChatHandler.ResetDroppedMessageCount();
        }
    }

    private static void SaveLogs()
    {
        try
        {
            var export = Service.SaveDebugLogs();
            Service.Notify($"Saved {export.EntryCount} log entries.\n{export.Path}", NotificationType.Success, 6);
        }
        catch (Exception exception)
        {
            Service.PluginLog.Error(exception, "Failed to save PuppetMasterKK message logs.");
            Service.Notify($"Failed to save logs.\n{exception.Message}", NotificationType.Error, 6);
        }
    }

    private void DrawLogsPage()
    {
        // The snapshot is taken only when something new was logged.
        var revision = DebugLogBuffer.Revision;
        if (revision != cachedLogRevision)
            (logEntries, cachedLogRevision) = DebugLogBuffer.SnapshotWithRevision();

        using (W.Card("logOptions", null, $"{logEntries.Length} captured"))
        {
            var capture = Config.DebugLogTypes;
            if (W.Toggle("Capture messages##capture", ref capture,
                         tooltip: "Record every chat message with its channel number and sender (this session only)"))
                Config.DebugLogTypes = capture;
            ImGui.SameLine(0f, Theme.S(24f));
            W.Toggle("Color by channel##colorLogs", ref colorLogs);
            ImGui.SameLine(0f, Theme.S(24f));
            if (W.Toggle("Follow new messages##autoScroll", ref autoScrollLogs) && autoScrollLogs)
                shownLogRevision = -1;

            var droppedMessages = ChatHandler.DroppedMessageCount;
            var droppedRequests = ChatHandler.DroppedRetriggerCount;
            if (droppedMessages > 0 || droppedRequests > 0)
            {
                Gap(2f);
                W.Banner($"Discarded under load: {droppedMessages} messages, {droppedRequests} waiting requests.", Theme.Warning);
            }
            if (!string.IsNullOrWhiteSpace(Service.LastDebugLogExportPath))
                Hint($"Last saved: {Service.LastDebugLogExportPath}");
        }

        var avail = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##logList", new Vector2(avail.X, MathF.Max(Theme.S(120f), avail.Y)), false))
            DrawLogRows();
        ImGui.EndChild();
    }

    private void DrawLogRows()
    {
        if (logEntries.Length == 0)
        {
            ImGui.TextColored(Theme.Faint, Config.DebugLogTypes
                ? "Waiting for chat messages…"
                : "Turn on Capture messages to see chat lines here, with the channel number each one came in on.");
            return;
        }

        var rowH = ImGui.GetFrameHeight() + ImGui.GetStyle().CellPadding.Y * 2f;
        using (var table = W.Table("##logTable", 3, W.TableFlags | ImGuiTableFlags.SizingFixedFit))
        {
            if (table.Open)
            {
                W.FixedColumn("##dot", 14f);
                ImGui.TableSetupColumn("Message", ImGuiTableColumnFlags.WidthStretch);
                W.FixedColumn("##actions", 70f);

                var clipper = ImGui.ImGuiListClipper();
                try
                {
                    clipper.Begin(logEntries.Length, rowH);
                    while (clipper.Step())
                    {
                        var start = Math.Max(0, clipper.DisplayStart);
                        var end = Math.Min(logEntries.Length, clipper.DisplayEnd);
                        for (var i = start; i < end; i++)
                            DrawLogRow(logEntries[i], rowH);
                    }
                    clipper.End();
                }
                finally
                {
                    // While the kit unwinds a throw, its recovery ends the table: an item count of -1 makes the
                    // clipper's End skip the cursor work, so it can still be freed.
                    if (KitRecovery.Unwinding)
                        clipper.ItemsCount = -1;
                    clipper.Destroy();
                }
            }
        }

        if (autoScrollLogs && cachedLogRevision != shownLogRevision)
            ImGui.SetScrollHereY(1f);
        shownLogRevision = cachedLogRevision;
    }

    private void DrawLogRow(DebugLogEntry entry, float rowH)
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowH);
        ImGui.PushID(entry.Sequence.GetHashCode());
        var color = colorLogs ? LogChannelColors[(int)((uint)entry.ChatTypeId % (uint)LogChannelColors.Length)] : Theme.Dim;

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        W.StatusDot(color, false);
        if (ImGui.IsItemHovered())
            W.Tooltip($"{ChannelName(entry.ChatTypeId)} (log type {entry.ChatTypeId})");

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        // One line per row (the clipper counts on equal rows); the tooltip has the whole message.
        ImGui.TextColored(colorLogs ? color : Theme.Ink, W.Fit(FirstLine(entry.Text), ImGui.GetContentRegionAvail().X));
        if (ImGui.IsItemHovered())
            W.Tooltip(entry.Text);

        ImGui.TableNextColumn();
        if (W.IconButton(FontAwesomeIcon.Plus, "##newReaction", "Make a trigger from this message"))
            CreateReactionFromLog(entry);
        if (!IsOfficialChannel(entry.ChatTypeId) && !IsConfiguredCustomChannel(entry.ChatTypeId))
        {
            ImGui.SameLine(0f, Theme.S(4f));
            if (W.IconButton(FontAwesomeIcon.Hashtag, "##addChannel", $"Add log type {entry.ChatTypeId} as a custom channel"))
                AddCustomChannel(entry.ChatTypeId);
        }
        ImGui.PopID();
    }

    private void CreateReactionFromLog(DebugLogEntry entry)
    {
        var reaction = PluginUiLogic.CreateReactionFromLog(entry.ChatTypeId, entry.TriggerText, ChannelName(entry.ChatTypeId), Config);
        Config.Reactions.Add(reaction);
        Select(Config.Reactions.Count - 1);
        page = Page.Reactions;
    }

    private static bool IsConfiguredCustomChannel(int chatTypeId)
    {
        foreach (var channel in Config.CustomChannels)
        {
            if (channel.ChatType == chatTypeId)
                return true;
        }
        return false;
    }

    private static void AddCustomChannel(int chatTypeId)
    {
        if (IsOfficialChannel(chatTypeId) || IsConfiguredCustomChannel(chatTypeId))
            return;
        Config.CustomChannels.Add(new ChannelSetting { ChatType = chatTypeId, Name = $"Custom {chatTypeId}" });
        Changed();
    }
}
