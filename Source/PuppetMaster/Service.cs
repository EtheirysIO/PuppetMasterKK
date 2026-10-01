using Dalamud.Game;
using Dalamud.Game.Text;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina.Excel.Sheets;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace PuppetMaster
{
    internal class Service
    {
        public static Plugin? plugin;
        public static Configuration? configuration;
        // Every game command and emote, with aliases resolved. Empty until InitializeCommands runs.
        public static CommandCatalog Commands { get; private set; } = CommandCatalog.Empty;
        public static string LastDebugLogExportPath { get; private set; } = string.Empty;

        private const uint CHANNEL_COUNT = 23;

        public static (string Path, int EntryCount) SaveDebugLogs()
        {
            var entries = DebugLogBuffer.Snapshot();
            if (entries.Length == 0)
                throw new InvalidOperationException("There are no captured log entries to save.");
            var configDirectory = PluginInterface.ConfigFile.DirectoryName ?? AppContext.BaseDirectory;
            LastDebugLogExportPath = DebugLogBuffer.SaveSnapshot(
                Path.Combine(configDirectory, "PuppetMasterLogs"),
                entries);
            return (LastDebugLogExportPath, entries.Length);
        }

        public static void InitializeCommands()
        {
            var gameCommands = new List<string[]>();
            var textCommands = DataManager.GetExcelSheet<TextCommand>();
            if (textCommands != null)
            {
                foreach (var row in textCommands)
                    gameCommands.Add(CommandForms(row));
            }

            var emoteCommands = new List<string[]>();
            var emotes = DataManager.GetExcelSheet<Emote>();
            if (emotes != null)
            {
                foreach (var emote in emotes)
                {
                    if (emote.TextCommand.ValueNullable is { } command)
                        emoteCommands.Add(CommandForms(command));
                }
            }

            Commands = new CommandCatalog(gameCommands, emoteCommands);
            if (Commands.EmoteCount == 0)
                PluginLog.Error("PuppetMaster could not read the emote list; emotes will be treated as unknown commands.");
        }

        private static string[] CommandForms(TextCommand row)
        {
            var forms = new List<string>(4);
            foreach (var text in new[] { row.Command, row.ShortCommand, row.Alias, row.ShortAlias })
            {
                var value = text.ExtractText();
                if (!string.IsNullOrWhiteSpace(value))
                    forms.Add(value);
            }
            return forms.ToArray();
        }

        public static bool IsPluginCommand(string command)
        {
            return CommandManager.Commands.ContainsKey(command);
        }

        public static void SetEnabledAll(bool enabled = true)
        {
            for (var i = 0; i < configuration?.Reactions.Count; i++)
            {
                configuration.Reactions[i].Enabled = enabled;
                if (!enabled)
                    ChatHandler.CancelReaction(configuration.Reactions[i]);
            }
            configuration?.Save();
#if DEBUG
            if (configuration != null && configuration.Reactions.Count > 0)
                ChatGui.Print("[PuppetMaster] "+(enabled ? "Enabled" : "Disabled") + $" {configuration.Reactions.Count} reaction" + (configuration.Reactions.Count > 1 ? "s" : ""));
#endif
        }

        public static void SetEnabled(string name, bool enabled = true, StringComparison sc = StringComparison.Ordinal)
        {
#if DEBUG
            var found = 0;
#endif
            for (var i = 0; i < configuration?.Reactions.Count; i++)
            {
                if (configuration.Reactions[i].Name.Equals(name, sc))
                {
                    configuration.Reactions[i].Enabled = enabled;
                    if (!enabled)
                        ChatHandler.CancelReaction(configuration.Reactions[i]);
#if DEBUG
                    found++;
#endif
                }
            }
#if DEBUG
            if (found > 0)
            {
                ChatGui.Print("[PuppetMaster] " + (enabled ? "Enabled" : "Disabled") + $" {found} reaction" + (found > 1 ? "s" : "") + $" with name={name}");
            }
#endif
            configuration?.Save();
        }

        public static bool IsValidReactionIndex(int index)
        {
            return (0 <= index && index < configuration?.Reactions.Count);
        }

        public static String GetDefaultRegex(int index)
        {
            return IsValidReactionIndex(index) && !configuration!.Reactions[index].TriggerPhrase.IsNullOrWhitespace() ?
                @"(?i)\b(?:" + ReactionCommandMatcher.EscapeTriggerPhrase(configuration.Reactions[index].TriggerPhrase) + @")\s+(?:\((.*?)\)|(\w+))" : @"";
        }
        public static String GetDefaultReplaceMatch()
        {
            return @"/$1$2";
        }

        private static void InitializeRegex()
        {
            for (var i = 0; i < configuration?.Reactions.Count; i++)
                InitializeRegex(i);
        }

        public static void InitializeRegex(int index, bool reload = false)
        {
            var reaction = configuration!.Reactions[index];
            if (!reload && (reaction.UseRegex ? reaction.CustomRx != null : reaction.Rx != null))
                return;

            reaction.Rx = null;
            reaction.CustomRx = null;
            try
            {
                if (reaction.UseRegex)
                {
                    if (!reaction.CustomPhrase.IsNullOrWhitespace())
                        reaction.CustomRx = new Regex(reaction.CustomPhrase, RegexOptions.None, TimeSpan.FromMilliseconds(250));
                }
                else
                {
                    var pattern = GetDefaultRegex(index);
                    if (!pattern.IsNullOrWhitespace())
                        reaction.Rx = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(250));
                }
            }
            catch (ArgumentException)
            {
                // Invalid patterns remain null so they cannot silently reuse stale compiled regexes.
            }
        }

        public struct ParsedTextCommand
        {
            public ParsedTextCommand() {}
            public string Main = string.Empty;
            public string Args = string.Empty;

            public override readonly string ToString()
            {
                return (Main + " " + Args).Trim();
            }
        }

        public static ParsedTextCommand FormatCommand(string command)
        {
            ParsedTextCommand textCommand = new();
            command = command.Trim();
            if (command.Length == 0)
                return textCommand;
            if (!command.StartsWith('/'))
            {
                textCommand.Main = command;
                return textCommand;
            }

            // The command name ends at the first whitespace of any kind (a full-width space must not hide the name).
            var end = 0;
            while (end < command.Length && !char.IsWhiteSpace(command[end]))
                end++;
            textCommand.Main = command[..end].ToLowerInvariant();
            textCommand.Args = CommandPolicy.ConvertPlaceholders(command[end..].Trim());
            return textCommand;
        }

        // UI-side check; must run on the framework thread (it reads the registered plugin commands).
        public static bool IsCommandAllowed(Reaction reaction, string command, out string reason)
        {
            var catalog = Commands;
            var canonical = catalog.Canonicalize(command);
            if (canonical == CommandPolicy.WaitCommand)
            {
                var blocked = catalog.CanonicalSet(reaction.CommandBlacklist).Contains(CommandPolicy.WaitCommand);
                reason = blocked ? "blocked by this reaction" : "pause";
                return !blocked;
            }
            return CommandPolicy.IsAllowed(
                canonical,
                catalog.Classify(command, IsPluginCommand),
                catalog.CanonicalSet(reaction.CommandWhitelist),
                catalog.CanonicalSet(reaction.CommandBlacklist),
                reaction.AllowAllCommands,
                out reason);
        }

        public static ParsedTextCommand GetTestInputCommand(int index)
        {
            ParsedTextCommand result = new();

            if (!IsValidReactionIndex(index) ||
                configuration!.Reactions[index].TestInput.IsNullOrWhitespace()) return result;

            var reaction = configuration.Reactions[index];
            var pattern = ReactionCommandMatcher.SelectPattern(reaction);
            if (pattern == null)
                return result;

            var status = ReactionCommandMatcher.TryGenerateCommand(
                pattern,
                reaction.TestInput,
                reaction.UseRegex ? reaction.ReplaceMatch : GetDefaultReplaceMatch(),
                out var command,
                out var matchedText,
                out _);
            if (status != ReactionMatchStatus.Success)
                return result;
            result.Args = matchedText;
            result.Main = FormatCommand(command).ToString();
            return result;
        }

        public static void InitializeConfig()
        {
            Exception? loadError = null;
            try
            {
                if (PluginInterface.GetPluginConfig() is Configuration loaded)
                {
                    configuration = loaded;
                    configuration.Initialize(PluginInterface);
                    var sourceVersion = configuration.Version;
                    ConfigurationUpgradeTransaction.Execute(
                        PluginInterface.ConfigFile.FullName,
                        sourceVersion,
                        ConfigVersion.CURRENT,
                        PrepareConfigurationForUse,
                        configuration.Save,
                        backupCreated: backupPath =>
                            PluginLog.Information(
                                "Backed up PuppetMaster configuration v{SourceVersion} to {BackupPath} before migrating to v{TargetVersion}.",
                                sourceVersion,
                                backupPath,
                                ConfigVersion.CURRENT));
                    return;
                }
            }
            catch (Exception ex)
            {
                loadError = ex;
            }

            // No config yet, or one we can't read (corrupt, truncated, from a newer version, failed migration).
            // Keep the unreadable file aside so nothing is lost, then start from defaults so the plugin still loads.
            string? preservedPath = null;
            if (loadError != null)
            {
                try
                {
                    if (PluginInterface.ConfigFile.Exists)
                        preservedPath = ConfigurationUpgradeTransaction.CopyAside(
                            PluginInterface.ConfigFile.FullName, "unreadable", "backup");
                }
                catch (Exception copyError)
                {
                    PluginLog.Error(copyError, "Failed to preserve the unreadable PuppetMaster configuration.");
                }
                PluginLog.Error(loadError, "PuppetMaster configuration could not be loaded; preserved it at {Path} and started from defaults.", preservedPath ?? "(not preserved)");
            }

            configuration = new Configuration();
            configuration.Initialize(PluginInterface);
            PrepareConfigurationForUse();
            if (loadError == null || preservedPath != null)
                configuration.Save();

            if (loadError != null)
            {
                NotificationManager.AddNotification(new Dalamud.Interface.ImGuiNotification.Notification
                {
                    Title = "Puppet Master",
                    Content = preservedPath != null
                        ? $"Your settings could not be read, so Puppet Master started with defaults.\nThe old file was kept at:\n{preservedPath}"
                        : "Your settings could not be read, so Puppet Master is using defaults for this session and will not overwrite the file.",
                    Type = Dalamud.Interface.ImGuiNotification.NotificationType.Error,
                    InitialDuration = TimeSpan.FromSeconds(20),
                });
            }
        }

        private static void PrepareConfigurationForUse()
        {
            var currentConfiguration = configuration!;
            ConfigurationMigrator.MigrateAndNormalize(currentConfiguration);

            if (currentConfiguration.EnabledChannels.Count != CHANNEL_COUNT)
            {
                currentConfiguration.EnabledChannels =
                [
                    new() {ChatType = (int)XivChatType.CrossLinkShell1, Name = "CWLS1"},
                    new() {ChatType = (int)XivChatType.CrossLinkShell2, Name = "CWLS2"},
                    new() {ChatType = (int)XivChatType.CrossLinkShell3, Name = "CWLS3"},
                    new() {ChatType = (int)XivChatType.CrossLinkShell4, Name = "CWLS4"},
                    new() {ChatType = (int)XivChatType.CrossLinkShell5, Name = "CWLS5"},
                    new() {ChatType = (int)XivChatType.CrossLinkShell6, Name = "CWLS6"},
                    new() {ChatType = (int)XivChatType.CrossLinkShell7, Name = "CWLS7"},
                    new() {ChatType = (int)XivChatType.CrossLinkShell8, Name = "CWLS8"},
                    new() {ChatType = (int)XivChatType.Ls1, Name = "LS1"},
                    new() {ChatType = (int)XivChatType.Ls2, Name = "LS2"},
                    new() {ChatType = (int)XivChatType.Ls3, Name = "LS3"},
                    new() {ChatType = (int)XivChatType.Ls4, Name = "LS4"},
                    new() {ChatType = (int)XivChatType.Ls5, Name = "LS5"},
                    new() {ChatType = (int)XivChatType.Ls6, Name = "LS6"},
                    new() {ChatType = (int)XivChatType.Ls7, Name = "LS7"},
                    new() {ChatType = (int)XivChatType.Ls8, Name = "LS8"},
                    new() {ChatType = (int)XivChatType.TellIncoming, Name = "Tell"},
                    new() {ChatType = (int)XivChatType.Say, Name = "Say"},
                    new() {ChatType = (int)XivChatType.Party, Name = "Party"},
                    new() {ChatType = (int)XivChatType.Yell, Name = "Yell"},
                    new() {ChatType = (int)XivChatType.Shout, Name = "Shout"},
                    new() {ChatType = (int)XivChatType.FreeCompany, Name = "Free Company"},
                    new() {ChatType = (int)XivChatType.Alliance, Name = "Alliance"}
                ];
            }

            if (currentConfiguration.Reactions.Count == 0)
            {
                currentConfiguration.Reactions.Add(Reaction.CreateDefault(
                    commandWhitelist: currentConfiguration.DefaultCommandWhitelist,
                    commandBlacklist: currentConfiguration.DefaultCommandBlacklist,
                    allowAllCommands: currentConfiguration.DefaultAllowAllCommands,
                    motionOnly: currentConfiguration.DefaultMotionOnly,
                    enabledChannels: currentConfiguration.DefaultEnabledChannels));
            }

            InitializeRegex();

            // Always set to false on load
            currentConfiguration.DebugLogTypes = false;

            if (!currentConfiguration.CopycatImportChecked)
            {
                currentConfiguration.CopycatImportChecked = true;
                CopycatImportedEnabled = TryImportCopycatSettings(currentConfiguration.EmoteReplies);
            }
        }

        // True when this load brought over enabled Right Back At You settings; the plugin tells the user once.
        public static bool CopycatImportedEnabled { get; private set; }

        // Right Back At You (Copycat) kept per-character settings in its own file. Emote replies are one global
        // setting now: take the first character that had it on (or the first one at all).
        private static bool TryImportCopycatSettings(EmoteReplySettings target)
        {
            try
            {
                var directory = PluginInterface.ConfigFile.DirectoryName;
                if (directory == null)
                    return false;
                var path = Path.Combine(directory, "Copycat.json");
                if (!File.Exists(path))
                    return false;

                var root = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
                if (root["PlayerConfigurations"] is not Newtonsoft.Json.Linq.JArray players || players.Count == 0)
                    return false;
                Newtonsoft.Json.Linq.JToken? chosen = null;
                foreach (var player in players)
                {
                    if (player.Value<bool?>("Enabled") == true)
                    {
                        chosen = player;
                        break;
                    }
                }
                chosen ??= players[0];

                target.Enabled = chosen.Value<bool?>("Enabled") ?? false;
                target.TargetBack = chosen.Value<bool?>("TargetBack") ?? true;
                target.MotionOnly = !string.IsNullOrEmpty(chosen.Value<string>("MotionOnly"));
                // Copycat's only working filter was "Friends Only" (Allowed == 1); anything else meant everyone.
                target.Senders = chosen.Value<int?>("Allowed") == 1
                    ? new SenderFilter { Anyone = false, Friends = true, FreeCompany = false, Party = false }
                    : SenderFilter.AnyoneFilter();
                PluginLog.Information("Imported Right Back At You settings from {Path}.", path);
                return target.Enabled;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Could not import Right Back At You settings.");
                return false;
            }
        }

        [PluginService]
        public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

        [PluginService]
        public static ICommandManager CommandManager { get; private set; } = null!;

        [PluginService]
        public static IChatGui ChatGui { get; private set; } = null!;

        [PluginService]
        public static ISigScanner SigScanner { get; private set; } = null!;

        [PluginService]
        public static IDataManager DataManager { get; private set; } = null!;

        [PluginService]
        public static IFramework Framework { get; private set; } = null!;

        [PluginService]
        public static INotificationManager NotificationManager { get; private set; } = null!;

        [PluginService]
        public static IPluginLog PluginLog { get; private set; } = null!;

        [PluginService]
        public static IPartyList PartyList { get; private set; } = null!;

        [PluginService]
        public static IObjectTable ObjectTable { get; private set; } = null!;

        [PluginService]
        public static IPlayerState PlayerState { get; private set; } = null!;

        [PluginService]
        public static ITargetManager TargetManager { get; private set; } = null!;

        [PluginService]
        public static IGameInteropProvider GameInterop { get; private set; } = null!;

        [PluginService]
        public static IClientState ClientState { get; private set; } = null!;

        [PluginService]
        public static ITextureProvider TextureProvider { get; private set; } = null!;
    }
}
