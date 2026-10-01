using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using phys1ksUI;

namespace PuppetMasterKK.UI;

internal enum Page
{
    Reactions,
    EmoteReplies,
    Follow,
    Activity,
    Logs,
    Settings,
}

/// <summary>
/// The PuppetMasterKK window (phys1ksUI shell). The sidebar picks the page: Reactions (list and editor), Emote replies,
/// Activity (what's running and waiting), Logs (captured chat) and Settings. Each page lives in its own partial file.
/// </summary>
internal sealed partial class MainWindow : KitWindow, IDisposable
{
    private Page page = Page.Reactions;

    private static Configuration Config => Service.configuration!;

    // One activity snapshot per frame, shared by the sidebar, the status block and the pages.
    private ReactionVisualizerSnapshot? activity;
    private int activityFrame = -1;

    private ReactionVisualizerSnapshot Activity
    {
        get
        {
            var frame = ImGui.GetFrameCount();
            if (activity == null || activityFrame != frame)
            {
                activity = ReactionVisualizerState.Snapshot();
                activityFrame = frame;
            }
            return activity;
        }
    }

    public MainWindow() : base("PuppetMasterKK###PuppetMasterKKWindow", "PuppetMasterKK", FontAwesomeIcon.TheaterMasks, new Vector2(620, 440))
    {
        Size = new Vector2(780, 580);
        SizeCondition = ImGuiCond.FirstUseEver;
        selected = Math.Clamp(Config.CurrentReactionEdit, 0, Math.Max(0, Config.Reactions.Count - 1));
    }

    public void Dispose()
    {
        ConfigSaver.Flush();
    }

    public override void OnClose()
    {
        ConfigSaver.Flush();
    }

    /// <summary>Opens the window on a page (or closes it when it's already showing that page).</summary>
    public void Toggle(Page target)
    {
        if (IsOpen && page == target && !Compact)
        {
            IsOpen = false;
            return;
        }
        Show(target);
    }

    public void Show(Page target)
    {
        page = target;
        IsOpen = true;
        Expand();
    }

    // ───────────────────────── Shell ─────────────────────────

    protected override AccentColor Accent => Config.Accent;

    protected override bool Colorblind => Config.Colorblind;

    // The plugin's icon (images\icon.png next to the dll) in the sidebar's brand tile.
    protected override string? BrandImagePath { get; } = System.IO.Path.Combine(
        Service.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty, "images", "icon.png");

    protected override string PageTitle => page switch
    {
        Page.Reactions => SelectedReaction is { } reaction ? DisplayName(reaction) : "Triggers",
        Page.EmoteReplies => "Emote replies",
        Page.Follow => "Follow mode",
        Page.Activity => "Activity",
        Page.Logs => "Message log",
        _ => "Settings",
    };

    protected override string PageKey => page switch
    {
        Page.Reactions => SelectedReaction is { } shown ? $"triggers/{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(shown)}" : "triggers",
        Page.Settings => $"settings/{settingsTab}",
        _ => page.ToString(),
    };

    protected override void DrawSidebarNav()
    {
        DrawReactionNav();

        W.Spacer();
        var activity = Activity;
        if (W.NavRow("emotes", FontAwesomeIcon.Smile, "Emote replies", page == Page.EmoteReplies,
                     Config.EmoteReplies.Enabled ? "On" : "Off"))
            page = Page.EmoteReplies;
        var followSubtitle = !Config.Follow.Enabled ? "Off" : FollowMode.Following is { } following ? $"Following {following}" : "On";
        if (W.NavRow("follow", FontAwesomeIcon.Walking, "Follow mode", page == Page.Follow, followSubtitle))
            page = Page.Follow;
        var running = activity.Active.Length;
        var waiting = activity.Queued.Length;
        var activitySubtitle = running > 0 ? $"{running} running" : waiting > 0 ? $"{waiting} waiting" : "Idle";
        if (W.NavRow("activity", FontAwesomeIcon.Stream, "Activity", page == Page.Activity, activitySubtitle))
            page = Page.Activity;
        if (W.NavRow("logs", FontAwesomeIcon.ListAlt, "Message log", page == Page.Logs,
                     Config.DebugLogTypes ? "Capturing" : "Not capturing"))
            page = Page.Logs;
        if (W.NavRow("settings", FontAwesomeIcon.Cog, "Settings", page == Page.Settings))
            page = Page.Settings;
    }

    protected override RunningOperation? GetRunningOperation()
    {
        var active = Activity.Active;
        if (active.Length == 0)
            return null;
        var first = active[0];
        var label = active.Length == 1 ? $"Running {first.ReactionName}" : $"{active.Length} triggers running";
        return new RunningOperation(label, FirstLine(first.Command), Cancel: CancelAllRunning);
    }

    protected override IReadOnlyList<StatusLine> GetStatusLines()
    {
        var lines = new List<StatusLine>(3);
        if (Config.EmoteReplies.Enabled && Service.plugin?.emoteReplies is { Available: false } replies)
            lines.Add(new StatusLine("Emote replies unavailable", Theme.Warning, Tooltip: replies.UnavailableReason));
        if (Config.DebugLogTypes)
            lines.Add(new StatusLine("Capturing messages", Theme.Accent, Tooltip: "Message log capture is on for this session."));
        if (Service.ObjectTable.LocalPlayer is { } player)
            lines.Add(new StatusLine(player.Name.TextValue, Theme.Faint, FontAwesomeIcon.User));
        return lines;
    }

    protected override float HeaderRightWidth => page switch
    {
        Page.Reactions => ReactionHeaderWidth(),
        Page.Logs => LogsHeaderWidth(),
        _ => 0f,
    };

    protected override void DrawHeaderRight(HeaderSlot slot)
    {
        if (page == Page.Reactions)
            DrawReactionHeader(slot);
        else if (page == Page.Logs)
            DrawLogsHeader(slot);
    }

    protected override void DrawBodyTop()
    {
        if (page == Page.Settings)
            DrawSettingsTabs();
    }

    protected override void DrawBody()
    {
        switch (page)
        {
            case Page.Reactions:
                DrawReactionsPage();
                break;
            case Page.EmoteReplies:
                DrawEmoteRepliesPage();
                break;
            case Page.Follow:
                DrawFollowPage();
                break;
            case Page.Activity:
                DrawActivityPage();
                break;
            case Page.Logs:
                DrawLogsPage();
                break;
            default:
                DrawSettingsPage();
                break;
        }
    }

    protected override void DrawOverlays()
    {
        DrawReactionDialogs();
        DrawChannelPicker();
    }

    // ───────────────────────── Shared helpers ─────────────────────────

    private static void Changed() => ConfigSaver.MarkDirty();

    private static string DisplayName(Reaction reaction)
        => string.IsNullOrWhiteSpace(reaction.Name) ? "Unnamed trigger" : reaction.Name;

    private static string FirstLine(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        return end < 0 ? text : text[..end] + " …";
    }

    private static void CancelAllRunning()
    {
        foreach (var reaction in Config.Reactions)
            ChatHandler.CancelReaction(reaction);
    }

    /// <summary>A short line of help text under a control.</summary>
    private static void Hint(string text) => W.TextWrapped(text, Theme.Faint);

    /// <summary>A Dim label above a control.</summary>
    private static void Label(string text) => ImGui.TextColored(Theme.Dim, text);

    private static void Gap(float px = 6f) => ImGui.Dummy(new Vector2(0f, Theme.S(px)));
}
