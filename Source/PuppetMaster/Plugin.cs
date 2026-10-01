using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using System;

using ECommons;
using PuppetMaster.Windows;

namespace PuppetMaster
{
    public class Plugin : IDalamudPlugin
    {
        public static String Name => "PuppetMaster";
        private const String CommandName = "/puppetmaster";
        public WindowSystem windowSystem = new("PuppetMaster");
        internal MainWindow? mainWindow;
        internal EmoteReplies? emoteReplies;

        private bool commandRegistered;
        private bool chatSubscribed;
        private bool uiSubscribed;
        private bool chatHandlerStarted;
        private bool ecommonsInitialized;
        private bool kitInitialized;

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
                phys1ksUI.Kit.Initialize(pluginInterface, Service.PluginLog, Service.TextureProvider, Service.DataManager,
                                         config.TextScale, config.Accent, config.Colorblind);
                kitInitialized = true;
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
                        Title = "Puppet Master",
                        Content = "Emote replies are now part of Puppet Master, and your Right Back At You settings were brought over.\nDisable or remove Right Back At You so emotes aren't answered twice.",
                        Type = Dalamud.Interface.ImGuiNotification.NotificationType.Info,
                        InitialDuration = TimeSpan.FromSeconds(20),
                    });
                }
                Service.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
                {
                    HelpMessage = @"Open the Puppet Master window
/puppetmaster on|off - turn every reaction on or off
/puppetmaster on|off <ReactionName> - turn reactions with that name on or off
/puppetmaster logging on|off - capture chat messages in the message log (this session)
/puppetmaster logging clear - clear the message log and the discarded counts
/puppetmaster logging save - save the message log to a file
/puppetmaster viz - show what's running (Activity)"
                });
                commandRegistered = true;
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

        // One failing teardown step must not skip the ones after it.
        private static void Safe(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Service.PluginLog?.Error(ex, "PuppetMaster teardown step failed.");
            }
        }

        private void OnCommand(String command, String args)
        {
            if (string.IsNullOrEmpty(args))
                mainWindow?.Toggle(Page.Reactions);
            else
            {
                var ptc = Service.FormatCommand($"/{args}");
#if DEBUG
                Service.ChatGui.Print($"[PuppetMaster][Debug] PARSED TEXT COMMAND: {ptc}");
#endif
                void enableReactions(bool enable)
                {
                    if (string.IsNullOrEmpty(ptc.Args))
                        Service.SetEnabledAll(enable);
                    else
                        Service.SetEnabled(ptc.Args, enable);
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
                    Service.ChatGui.Print("[PuppetMaster] Message logging enabled for this session.");
                    break;
                case "off":
                    Service.configuration!.DebugLogTypes = false;
                    Service.ChatGui.Print("[PuppetMaster] Message logging disabled.");
                    break;
                case "clear":
                    var clearedCount = DebugLogBuffer.Snapshot().Length;
                    DebugLogBuffer.Clear();
                    ChatHandler.ResetDroppedMessageCount();
                    Service.ChatGui.Print($"[PuppetMaster] Cleared {clearedCount} captured log entr{(clearedCount == 1 ? "y" : "ies")} and overload counters.");
                    break;
                case "save":
                    try
                    {
                        var export = Service.SaveDebugLogs();
                        Service.ChatGui.Print($"[PuppetMaster] Saved {export.EntryCount} log entries to: {export.Path}");
                    }
                    catch (Exception exception)
                    {
                        Service.PluginLog.Error(exception, "Failed to save PuppetMaster message logs from command.");
                        Service.ChatGui.PrintError($"[PuppetMaster] Failed to save logs: {exception.Message}");
                    }
                    break;
                default:
                    Service.ChatGui.Print(
                        $"[PuppetMaster] Logging is {(Service.configuration!.DebugLogTypes ? "enabled" : "disabled")}. " +
                        "Use /puppetmaster logging on|off|clear|save.");
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
