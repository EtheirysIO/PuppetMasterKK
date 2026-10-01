using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

using PuppetMasterKK.UI;

namespace PuppetMasterKK
{
    public class Plugin : IDalamudPlugin
    {
        // Its own commands, so it can be installed next to the old Puppet Master without the two fighting over one.
        private const String CommandName = "/puppetmasterkk";
        private const String ShortCommandName = "/pmkk";
        public WindowSystem windowSystem = new("PuppetMasterKK");
        internal MainWindow? mainWindow;
        internal EmoteReplies? emoteReplies;

        private bool commandRegistered;
        private bool shortCommandRegistered;
        private bool chatSubscribed;
        private bool logoutSubscribed;
        private bool uiSubscribed;
        private bool chatHandlerStarted;
        private bool kitInitialized;
        private bool pluginsWatched;
        private static readonly HashSet<string> WarnedRivals = new(StringComparer.OrdinalIgnoreCase);

        public Plugin(IDalamudPluginInterface pluginInterface)
        {
            try
            {
                pluginInterface.Create<Service>();
                Service.plugin = this;

                Service.InitializeConfig();
                Service.InitializeCommands();

                // The UI kit hooks UiBuilder.Draw before our own handler, so its per-frame work runs first.
                var config = Service.configuration!;
                kitInitialized = true; // before the call: Kit.Dispose also cleans up after a partial Initialize
                phys1ksUI.Kit.Initialize(pluginInterface, Service.PluginLog, Service.TextureProvider, Service.DataManager,
                                         config.TextScale, config.Accent, config.Colorblind);
                mainWindow = new MainWindow();
                windowSystem.AddWindow(mainWindow);

                // Start work and subscribe to events last, so a failure above leaves nothing running.
                ChatHandler.Initialize();
                chatHandlerStarted = true;
                emoteReplies = new EmoteReplies();
                if (Service.CopycatImportedEnabled)
                {
                    Service.Notify("Emote replies are now part of PuppetMasterKK, and your Right Back At You settings were brought over.\n" +
                                   "Disable or remove Right Back At You so emotes aren't answered twice.", NotificationType.Info);
                }
                // Only a command we added is removed on unload (a name another plugin already took stays theirs).
                commandRegistered = Service.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
                {
                    HelpMessage = @"Open the PuppetMasterKK window (short: /pmkk)
/pmkk on|off - turn every trigger on or off (off also stops walking and mimicking)
/pmkk on|off <TriggerName> - turn triggers with that name on or off
/pmkk logging on|off - capture chat messages in the message log (this session)
/pmkk logging clear - clear the message log and the Activity counts
/pmkk logging save - save the message log to a file
/pmkk viz - show what's running (Activity)
/pmkk practice on|off - practice mode: triggers run but send nothing (this session)"
                });
                shortCommandRegistered = Service.CommandManager.AddHandler(ShortCommandName, new CommandInfo(OnCommand)
                {
                    HelpMessage = "Short for /puppetmasterkk",
                    ShowInHelp = false,
                });
                if (!commandRegistered || !shortCommandRegistered)
                    Service.PluginLog.Warning("Another plugin already uses {Long} or {Short}.", CommandName, ShortCommandName);
                WarnIfOldPluginLoaded();
                Service.PluginInterface.ActivePluginsChanged += OnActivePluginsChanged;
                pluginsWatched = true;
                ShowLoadNotices();
                Service.ChatGui.ChatMessage += ChatHandler.OnChatMessage;
                chatSubscribed = true;
                Service.ClientState.Logout += OnLogout;
                logoutSubscribed = true;
                Service.PluginInterface.UiBuilder.Draw += DrawUI;
                Service.PluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
                Service.PluginInterface.UiBuilder.OpenMainUi += OpenMain;
                uiSubscribed = true;
            }
            catch
            {
                // Dalamud never calls Dispose when the constructor throws, so undo whatever was set up.
                Dispose();
                throw;
            }
        }

        // A mimic leader or walk from one character must not carry over to the next.
        private static void OnLogout(int type, int code) => Safe(FollowMode.Reset);

        // Undoes the constructor in reverse order.
        public void Dispose()
        {
            if (uiSubscribed)
            {
                Safe(() => Service.PluginInterface.UiBuilder.OpenMainUi -= OpenMain);
                Safe(() => Service.PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings);
                Safe(() => Service.PluginInterface.UiBuilder.Draw -= DrawUI);
                uiSubscribed = false;
            }
            if (logoutSubscribed)
            {
                Safe(() => Service.ClientState.Logout -= OnLogout);
                logoutSubscribed = false;
            }
            if (chatSubscribed)
            {
                Safe(() => Service.ChatGui.ChatMessage -= ChatHandler.OnChatMessage);
                chatSubscribed = false;
            }
            if (pluginsWatched)
            {
                Safe(() => Service.PluginInterface.ActivePluginsChanged -= OnActivePluginsChanged);
                pluginsWatched = false;
            }
            if (shortCommandRegistered)
            {
                Safe(() => Service.CommandManager.RemoveHandler(ShortCommandName));
                shortCommandRegistered = false;
            }
            if (commandRegistered)
            {
                Safe(() => Service.CommandManager.RemoveHandler(CommandName));
                commandRegistered = false;
            }
            if (emoteReplies != null)
            {
                Safe(emoteReplies.Dispose);
                emoteReplies = null;
            }
            if (chatHandlerStarted)
            {
                Safe(ChatHandler.Shutdown);
                chatHandlerStarted = false;
            }
            Safe(windowSystem.RemoveAllWindows);
            Safe(() => mainWindow?.Dispose());
            mainWindow = null;
            Safe(ConfigSaver.Flush);
            // The kit last among the UI pieces: no window may draw with the fonts it releases.
            if (kitInitialized)
            {
                Safe(phys1ksUI.Kit.Dispose);
                kitInitialized = false;
            }
            Service.plugin = null;
            Service.configuration = null;
        }

        // Other puppet-master plugins (the original Puppet Master, forks of it, Right Back At You) answer the same chat
        // and emotes: with two running, every trigger fires twice. There can be only one.
        private static void WarnIfOldPluginLoaded()
        {
            try
            {
                var rivals = new List<string>();
                foreach (var plugin in Service.PluginInterface.InstalledPlugins)
                {
                    if (!plugin.IsLoaded || plugin.InternalName.Equals(Service.PluginInterface.InternalName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    // Each rival is announced once per session, not on every plugin list change.
                    if ((IsRival(plugin.InternalName) || IsRival(plugin.Name)) && WarnedRivals.Add(plugin.InternalName))
                        rivals.Add(plugin.Name);
                }
                if (rivals.Count == 0)
                    return;

                var names = string.Join(", ", rivals);
                Service.NotificationManager.AddNotification(new Dalamud.Interface.ImGuiNotification.Notification
                {
                    Title = "There can be only one PuppetMasterKK",
                    Content = $"Also running: {names}.\nIt reacts to the same chat and emotes, so everything would fire twice. " +
                              "Disable or uninstall it and let PuppetMasterKK pull the strings.",
                    Type = NotificationType.Warning,
                    InitialDuration = TimeSpan.FromSeconds(30),
                });
                Service.ChatGui.PrintError($"[PuppetMasterKK] Another puppet master is loaded ({names}). Disable it, or every trigger fires twice.");
            }
            catch (Exception ex)
            {
                Service.PluginLog.Warning(ex, "Could not check for other puppet master plugins.");
            }
        }

        // A rival loaded after us (or still loading when we started) is caught here.
        private static void OnActivePluginsChanged(IActivePluginsChangedEventArgs args)
        {
            Service.Framework.RunOnFrameworkThread(WarnIfOldPluginLoaded);
        }

        private static void ShowLoadNotices()
        {
            var config = Service.configuration!;
            if (Service.LegacyConfigImported)
            {
                Service.Notify("Your Puppet Master triggers and settings were brought over. The old settings file wasn't changed.",
                       NotificationType.Info);
            }
            else if (Service.LegacyUnreadable.Count > 0)
            {
                Service.Notify($"Old Puppet Master settings were found but couldn't be read ({string.Join(", ", Service.LegacyUnreadable)}), " +
                       "so PuppetMasterKK started fresh. They weren't changed.",
                       NotificationType.Warning);
            }
            if (config.ReviewAfterMigration.Count > 0)
            {
                Service.Notify($"Please review: {string.Join(", ", config.ReviewAfterMigration)}.\nThese can run any game command and react " +
                       "to anyone. Chat commands, other plugins' commands and things like teleporting now have to be allowed one by one.",
                       NotificationType.Warning);
                config.ReviewAfterMigration.Clear();
            }
        }

        private static bool IsRival(string name)
        {
            var squashed = name.Replace(" ", string.Empty);
            return squashed.Contains("puppetmaster", StringComparison.OrdinalIgnoreCase) ||
                   squashed.Equals("Copycat", StringComparison.OrdinalIgnoreCase) ||
                   squashed.Contains("RightBackAtYou", StringComparison.OrdinalIgnoreCase);
        }

        // One failing teardown step must not skip the ones after it.
        private static void Safe(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Service.PluginLog?.Error(ex, "PuppetMasterKK teardown step failed.");
            }
        }

        private void OnCommand(String command, String args)
        {
            if (string.IsNullOrWhiteSpace(args))
                mainWindow?.Toggle(Page.Reactions);
            else
            {
                // The verb, then the rest as typed (a trigger name is matched as written, brackets included, ignoring case).
                var trimmed = args.Trim();
                var space = trimmed.IndexOf(' ');
                var ptc = new ParsedTextCommand
                {
                    Main = "/" + (space < 0 ? trimmed : trimmed[..space]).ToLowerInvariant(),
                    Args = space < 0 ? string.Empty : trimmed[(space + 1)..].Trim(),
                };
                void enableReactions(bool enable)
                {
                    if (string.IsNullOrEmpty(ptc.Args))
                    {
                        Service.SetEnabledAll(enable);
                        // "Off" with no name is the panic switch: stop any walk or mimic too.
                        if (!enable)
                            FollowMode.Reset();
                    }
                    else
                        Service.SetEnabled(ptc.Args, enable);
                    if (!enable)
                        CommandRateLimiter.Shared.Reset();
                }
                if (ptc.Main.Equals("/on"))
                {
                    enableReactions(true);
                }
                else if (ptc.Main.Equals("/off"))
                {
                    enableReactions(false);
                }
                else if (ptc.Main.Equals("/logging"))
                {
                    HandleLoggingCommand(ptc.Args);
                }
                else if (ptc.Main.Equals("/practice"))
                {
                    HandlePracticeCommand(ptc.Args);
                }
                else if (ptc.Main.Equals("/viz") || ptc.Main.Equals("/visualizer"))
                {
                    mainWindow?.Show(Page.Activity);
                }
                else
                {
                    Service.ChatGui.PrintError("[PuppetMasterKK] Unknown option. Use /pmkk, /pmkk on|off [name], /pmkk logging, /pmkk practice or /pmkk viz.");
                }
            }
        }

        private static void HandlePracticeCommand(string args)
        {
            var config = Service.configuration!;
            switch (args.Trim().ToLowerInvariant())
            {
                case "on":
                    ChatHandler.SetPracticeMode(config, true);
                    Service.ChatGui.Print("[PuppetMasterKK] Practice mode on: triggers run but nothing is sent.");
                    break;
                case "off":
                    ChatHandler.SetPracticeMode(config, false);
                    Service.ChatGui.Print("[PuppetMasterKK] Practice mode off.");
                    break;
                default:
                    Service.ChatGui.Print($"[PuppetMasterKK] Practice mode is {(config.PracticeMode ? "on" : "off")}. Use /pmkk practice on|off.");
                    break;
            }
        }

        private static void HandleLoggingCommand(string args)
        {
            switch (args.Trim().ToLowerInvariant())
            {
                case "on":
                    Service.configuration!.DebugLogTypes = true;
                    Service.ChatGui.Print("[PuppetMasterKK] Message logging enabled for this session.");
                    break;
                case "off":
                    Service.configuration!.DebugLogTypes = false;
                    Service.ChatGui.Print("[PuppetMasterKK] Message logging disabled.");
                    break;
                case "clear":
                    var clearedCount = DebugLogBuffer.Snapshot().Length;
                    DebugLogBuffer.Clear();
                    ChatHandler.ResetDroppedMessageCount();
                    Service.ChatGui.Print($"[PuppetMasterKK] Cleared {clearedCount} captured log entr{(clearedCount == 1 ? "y" : "ies")} and the Activity counts.");
                    break;
                case "save":
                    try
                    {
                        var export = Service.SaveDebugLogs();
                        Service.ChatGui.Print($"[PuppetMasterKK] Saved {export.EntryCount} log entries to: {export.Path}");
                    }
                    catch (Exception exception)
                    {
                        Service.PluginLog.Error(exception, "Failed to save PuppetMasterKK message logs from command.");
                        Service.ChatGui.PrintError($"[PuppetMasterKK] Failed to save logs: {exception.Message}");
                    }
                    break;
                default:
                    Service.ChatGui.Print(
                        $"[PuppetMasterKK] Logging is {(Service.configuration!.DebugLogTypes ? "enabled" : "disabled")}. " +
                        "Use /pmkk logging on|off|clear|save.");
                    break;
            }
        }

        private void DrawUI()
        {
            this.windowSystem.Draw();
            ConfigSaver.Tick();
        }

        // The plugin installer's Open and gear buttons.
        private void OpenMain() => mainWindow?.Toggle(Page.Reactions);

        private void OpenSettings() => mainWindow?.Toggle(Page.Settings);
    }
}
