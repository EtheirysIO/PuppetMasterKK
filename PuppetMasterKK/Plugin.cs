using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

using ECommons;
using PuppetMasterKK.UI;

namespace PuppetMasterKK
{
    public class Plugin : IDalamudPlugin
    {
        public static String Name => "PuppetMasterKK";
        // Its own commands, so it can be installed next to the old Puppet Master without the two fighting over one.
        private const String CommandName = "/puppetmasterkk";
        private const String ShortCommandName = "/pmkk";
        public WindowSystem windowSystem = new("PuppetMasterKK");
        internal MainWindow? mainWindow;
        internal EmoteReplies? emoteReplies;

        private bool commandRegistered;
        private bool chatSubscribed;
        private bool uiSubscribed;
        private bool chatHandlerStarted;
        private bool ecommonsInitialized;
        private bool kitInitialized;
        private bool pluginsWatched;
        private static readonly HashSet<string> WarnedRivals = new(StringComparer.OrdinalIgnoreCase);

        public Plugin(IDalamudPluginInterface pluginInterface)
        {
            try
            {
                pluginInterface.Create<Service>();
                Service.plugin = this;

                // ECommons first (its convention), with no optional modules: Chat.SendMessage needs none of them, and
                // Module.All would install object-life hooks and Dalamud reflection that can break on a game patch.
                ECommonsMain.Init(pluginInterface, this);
                ecommonsInitialized = true;

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
                    Service.NotificationManager.AddNotification(new Dalamud.Interface.ImGuiNotification.Notification
                    {
                        Title = "PuppetMasterKK",
                        Content = "Emote replies are now part of PuppetMasterKK, and your Right Back At You settings were brought over.\nDisable or remove Right Back At You so emotes aren't answered twice.",
                        Type = Dalamud.Interface.ImGuiNotification.NotificationType.Info,
                        InitialDuration = TimeSpan.FromSeconds(20),
                    });
                }
                Service.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
                {
                    HelpMessage = @"Open the PuppetMasterKK window (short: /pmkk)
/pmkk on|off - turn every reaction on or off
/pmkk on|off <ReactionName> - turn reactions with that name on or off
/pmkk logging on|off - capture chat messages in the message log (this session)
/pmkk logging clear - clear the message log and the discarded counts
/pmkk logging save - save the message log to a file
/pmkk viz - show what's running (Activity)"
                });
                Service.CommandManager.AddHandler(ShortCommandName, new CommandInfo(OnCommand)
                {
                    HelpMessage = "Short for /puppetmasterkk",
                    ShowInHelp = false,
                });
                commandRegistered = true;
                WarnIfOldPluginLoaded();
                Service.PluginInterface.ActivePluginsChanged += OnActivePluginsChanged;
                pluginsWatched = true;
                ShowLoadNotices();
                Service.ChatGui.ChatMessage += ChatHandler.OnChatMessage;
                chatSubscribed = true;
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

        public void Dispose()
        {
            if (pluginsWatched)
            {
                Safe(() => Service.PluginInterface.ActivePluginsChanged -= OnActivePluginsChanged);
                pluginsWatched = false;
            }
            if (uiSubscribed)
            {
                Safe(() =>
                {
                    Service.PluginInterface.UiBuilder.Draw -= DrawUI;
                    Service.PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
                    Service.PluginInterface.UiBuilder.OpenMainUi -= OpenMain;
                });
                uiSubscribed = false;
            }
            if (chatSubscribed)
            {
                Safe(() => Service.ChatGui.ChatMessage -= ChatHandler.OnChatMessage);
                chatSubscribed = false;
            }
            if (commandRegistered)
            {
                Safe(() => Service.CommandManager.RemoveHandler(CommandName));
                Safe(() => Service.CommandManager.RemoveHandler(ShortCommandName));
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
            if (ecommonsInitialized)
            {
                Safe(ECommonsMain.Dispose);
                ecommonsInitialized = false;
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
                    if (!plugin.IsLoaded || plugin.InternalName.Equals("PuppetMasterKK", StringComparison.OrdinalIgnoreCase))
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
                    Type = Dalamud.Interface.ImGuiNotification.NotificationType.Warning,
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
                Notify("Your Puppet Master reactions and settings were brought over. The old settings file wasn't changed.",
                       Dalamud.Interface.ImGuiNotification.NotificationType.Info);
            }
            else if (Service.LegacyUnreadable.Count > 0)
            {
                Notify($"Old Puppet Master settings were found but couldn't be read ({string.Join(", ", Service.LegacyUnreadable)}), " +
                       "so PuppetMasterKK started fresh. They weren't changed.",
                       Dalamud.Interface.ImGuiNotification.NotificationType.Warning);
            }
            if (config.ReviewAfterMigration.Count > 0)
            {
                Notify($"Please review: {string.Join(", ", config.ReviewAfterMigration)}.\nThese can run any game command and react " +
                       "to anyone. Chat commands, other plugins' commands and things like teleporting now have to be allowed one by one.",
                       Dalamud.Interface.ImGuiNotification.NotificationType.Warning);
                config.ReviewAfterMigration.Clear();
            }
        }

        private static void Notify(string content, Dalamud.Interface.ImGuiNotification.NotificationType type)
        {
            Service.NotificationManager.AddNotification(new Dalamud.Interface.ImGuiNotification.Notification
            {
                Title = "PuppetMasterKK",
                Content = content,
                Type = type,
                InitialDuration = TimeSpan.FromSeconds(20),
            });
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
            if (string.IsNullOrEmpty(args))
                mainWindow?.Toggle(Page.Reactions);
            else
            {
                // The verb, then the rest as typed (a reaction name is matched exactly as written, brackets included).
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
                        Service.SetEnabledAll(enable);
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
                else if (ptc.Main.Equals("/viz") || ptc.Main.Equals("/visualizer"))
                {
                    mainWindow?.Show(Page.Activity);
                }
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
                    Service.ChatGui.Print($"[PuppetMasterKK] Cleared {clearedCount} captured log entr{(clearedCount == 1 ? "y" : "ies")} and overload counters.");
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
