using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina.Excel.Sheets;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Loader;
using System.Text.RegularExpressions;

namespace PuppetMasterKK
{
    internal class Service
    {
        public static Plugin? plugin;
        public static Configuration? configuration;
        // Every game command and emote, with aliases resolved. Empty until InitializeCommands runs.
        public static CommandCatalog Commands { get; private set; } = CommandCatalog.Empty;
        public static string LastDebugLogExportPath { get; private set; } = string.Empty;

        public static (string Path, int EntryCount) SaveDebugLogs()
        {
            var entries = DebugLogBuffer.Snapshot();
            if (entries.Length == 0)
                throw new InvalidOperationException("There are no captured log entries to save.");
            var configDirectory = PluginInterface.ConfigFile.DirectoryName ?? AppContext.BaseDirectory;
            LastDebugLogExportPath = DebugLogBuffer.SaveSnapshot(
                Path.Combine(configDirectory, "PuppetMasterKKLogs"),
                entries);
            return (LastDebugLogExportPath, entries.Length);
        }

        public static void InitializeCommands()
        {
            // Each command's names on this client, plus its English names: block and allow entries written in
            // English (and the built-in blocks) then mean the same command on every client language.
            var english = new Dictionary<uint, string[]>();
            try
            {
                foreach (var row in DataManager.GetExcelSheet<TextCommand>(ClientLanguage.English))
                    english[row.RowId] = CommandForms(row);
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Could not read the English command names.");
            }

            string[] Forms(TextCommand row)
            {
                var forms = CommandForms(row);
                return english.TryGetValue(row.RowId, out var extra) ? [.. forms, .. extra] : forms;
            }

            var gameCommands = new List<string[]>();
            foreach (var row in DataManager.GetExcelSheet<TextCommand>())
                gameCommands.Add(Forms(row));

            var emoteCommands = new List<string[]>();
            foreach (var emote in DataManager.GetExcelSheet<Emote>())
            {
                if (emote.TextCommand.ValueNullable is { } command)
                    emoteCommands.Add(Forms(command));
            }

            Commands = new CommandCatalog(gameCommands, emoteCommands);
            if (Commands.EmoteCount == 0)
                PluginLog.Error("PuppetMasterKK could not read the emote list; emotes will be treated as unknown commands.");
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

        public static void SetEnabledAll(bool enabled)
        {
            for (var i = 0; i < configuration?.Reactions.Count; i++)
            {
                configuration.Reactions[i].Enabled = enabled;
                if (!enabled)
                    ChatHandler.CancelReaction(configuration.Reactions[i]);
            }
            SaveNow();
            if (configuration != null)
                ChatGui.Print($"[PuppetMasterKK] {(enabled ? "Turned on" : "Turned off")} {Plural(configuration.Reactions.Count, "trigger")}.");
        }

        public static void SetEnabled(string name, bool enabled)
        {
            var found = 0;
            for (var i = 0; i < configuration?.Reactions.Count; i++)
            {
                if (configuration.Reactions[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    configuration.Reactions[i].Enabled = enabled;
                    if (!enabled)
                        ChatHandler.CancelReaction(configuration.Reactions[i]);
                    found++;
                }
            }
            ChatGui.Print(found > 0
                ? $"[PuppetMasterKK] {(enabled ? "Turned on" : "Turned off")} {Plural(found, "trigger")} named \"{name}\"."
                : $"[PuppetMasterKK] No trigger is named \"{name}\".");
            SaveNow();
        }

        // Saves through ConfigSaver, so a locked file is retried later instead of throwing out of /pmkk.
        private static void SaveNow()
        {
            ConfigSaver.MarkDirty();
            ConfigSaver.Flush();
        }

        // A PuppetMasterKK toast. Framework thread.
        public static void Notify(string content, NotificationType type, int seconds = 20)
        {
            NotificationManager.AddNotification(new Notification
            {
                Title = "PuppetMasterKK",
                Content = content,
                Type = type,
                InitialDuration = TimeSpan.FromSeconds(seconds),
            });
        }

        private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

        public static bool IsValidReactionIndex(int index)
        {
            return (0 <= index && index < configuration?.Reactions.Count);
        }

        public static String GetDefaultRegex(int index)
        {
            return IsValidReactionIndex(index)
                ? ReactionCommandMatcher.BuildPhrasePattern(configuration!.Reactions[index].TriggerPhrase)
                : string.Empty;
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
                // Nothing but the text box enforces the length limit, and a hand-edited config can hold anything.
                var maxLength = configuration.MaxRegexLength;
                if (reaction.UseRegex)
                {
                    if (!reaction.CustomPhrase.IsNullOrWhitespace() && reaction.CustomPhrase.Length <= maxLength)
                        reaction.CustomRx = new Regex(reaction.CustomPhrase, RegexOptions.None, TimeSpan.FromMilliseconds(250));
                }
                else
                {
                    var pattern = GetDefaultRegex(index);
                    if (!pattern.IsNullOrWhitespace() && reaction.TriggerPhrase.Length <= maxLength)
                        reaction.Rx = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(250));
                }
            }
            catch (ArgumentException)
            {
                // Invalid patterns remain null so they cannot silently reuse stale compiled regexes.
            }
        }


        // The plugin that registered a command (its internal name), or null. Framework thread: it reads the registered
        // commands.
        public static string? PluginOwner(string command)
        {
            return CommandManager.Commands.TryGetValue(CommandCatalog.Normalize(command), out var info) ? HandlerOwner(info) : null;
        }

        // Dalamud loads each plugin into a load context named after its main assembly, which is the plugin's internal
        // name. That also covers handlers that live in a library the plugin loaded (ECommons' command attributes);
        // otherwise the handler's own assembly name is used. Null when there's no handler: then nothing is opened.
        private static string? HandlerOwner(IReadOnlyCommandInfo info)
        {
            var assembly = info.Handler?.Method.DeclaringType?.Assembly;
            if (assembly == null)
                return null;
            var context = AssemblyLoadContext.GetLoadContext(assembly);
            return context != null && context != AssemblyLoadContext.Default && !string.IsNullOrEmpty(context.Name)
                ? context.Name
                : assembly.GetName().Name;
        }

        // Whether a command runs without being listed, because this trigger switched its protection off.
        public static bool IsOpen(ProtectionSettings protections, CommandKind kind, string canonical, string command)
        {
            var group = kind == CommandKind.Plugin ? PluginOwner(command) : Commands.GroupOf(canonical);
            return protections.IsOpen(kind, group);
        }

        // Loaded plugins that registered commands, risky ones first (framework thread). Cached for a second: the
        // Protections card asks every frame.
        private static List<(string InternalName, string Name, string? Risk)> commandPlugins = [];
        private static long commandPluginsAt;

        public static IReadOnlyList<(string InternalName, string Name, string? Risk)> CommandPlugins()
        {
            var now = Environment.TickCount64;
            if (now - commandPluginsAt < 1000 && commandPluginsAt != 0)
                return commandPlugins;
            commandPluginsAt = now;
            var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var info in CommandManager.Commands.Values)
                {
                    if (HandlerOwner(info) is { } owner)
                        owners.Add(owner);
                }
                var list = new List<(string InternalName, string Name, string? Risk)>();
                foreach (var plugin in PluginInterface.InstalledPlugins)
                {
                    if (!plugin.IsLoaded || !owners.Contains(plugin.InternalName) ||
                        plugin.InternalName.Equals(PluginInterface.InternalName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    ProtectionGroups.RiskyPlugins.TryGetValue(plugin.InternalName, out var risk);
                    list.Add((plugin.InternalName, plugin.Name, risk));
                }
                list.Sort((a, b) => a.Risk == null != (b.Risk == null)
                    ? (a.Risk == null ? 1 : -1)
                    : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                commandPlugins = list;
            }
            catch (Exception ex)
            {
                PluginLog.Debug(ex, "Couldn't read the plugin list.");
            }
            return commandPlugins;
        }

        // What the editor shows for a line of a trigger's commands, including /wait.
        public static bool IsCommandAllowed(Reaction reaction, string command, bool templateWait, out string reason)
        {
            var catalog = Commands;
            var whitelist = catalog.CanonicalSet(reaction.CommandWhitelist);
            var blacklist = catalog.CanonicalSet(reaction.CommandBlacklist);
            if (CommandCatalog.Normalize(command) == CommandPolicy.WaitCommand)
                return CommandPolicy.IsWaitAllowed(catalog, templateWait, whitelist, blacklist, out reason, reaction.NoProtections);
            return IsCommandAllowed(command, whitelist, blacklist, reaction.AllowAllCommands, reaction.Protections,
                                    reaction.NoProtections, out reason);
        }

        // The one permission check for a command (not /wait). The lists must come from Commands.CanonicalSet.
        // Framework thread: it reads the registered plugin commands.
        public static bool IsCommandAllowed(
            string command,
            IReadOnlySet<string> whitelist,
            IReadOnlySet<string> blacklist,
            bool allowAllGameCommands,
            ProtectionSettings protections,
            bool noProtections,
            out string reason)
        {
            var catalog = Commands;
            var canonical = catalog.Canonicalize(command);
            var kind = catalog.Classify(command, IsPluginCommand);
            return CommandPolicy.IsAllowed(canonical, kind, whitelist, blacklist, allowAllGameCommands, out reason, noProtections,
                                           IsOpen(protections, kind, canonical, command));
        }

        // True when this load brought over the old Puppet Master's settings.
        public static bool LegacyConfigImported { get; private set; }
        // Old Puppet Master files that exist but couldn't be read (shown to the user).
        public static List<string> LegacyUnreadable { get; } = [];

        // PuppetMasterKK keeps its own settings file. The old Puppet Master's (left untouched) is read once: its main
        // file first, then its backups newest first, taking the first that reads and migrates. Forks wrote other
        // formats, so a file that doesn't read is skipped, not fatal.
        private static Configuration? TryLoadLegacyConfig()
        {
            var directory = PluginInterface.ConfigFile.DirectoryName;
            if (directory == null || !Directory.Exists(directory))
                return null;
            var candidates = new List<string>();
            var main = Path.Combine(directory, "PuppetMaster.json");
            if (File.Exists(main))
                candidates.Add(main);
            var backups = Directory.GetFiles(directory, "PuppetMaster.*.backup.json");
            Array.Sort(backups, StringComparer.OrdinalIgnoreCase);
            Array.Reverse(backups); // names carry a sortable timestamp: newest first
            candidates.AddRange(backups);

            foreach (var path in candidates)
            {
                try
                {
                    var legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(path));
                    if (legacy == null)
                        continue;
                    ConfigurationMigrator.MigrateAndNormalize(legacy);
                    if (legacy.Reactions.Count == 0)
                        continue;
                    LegacyConfigImported = true;
                    PluginLog.Information("Imported Puppet Master settings from {Path}.", path);
                    return legacy;
                }
                catch (Exception ex)
                {
                    LegacyUnreadable.Add(Path.GetFileName(path));
                    PluginLog.Warning(ex, "Old Puppet Master settings in {Path} couldn't be read; trying the next file.", path);
                }
            }
            return null;
        }

        // A config that was only ever created with defaults (nothing worth keeping over an import).
        private static bool LooksUntouched(Configuration config)
        {
            if (config.Reactions.Count == 0)
                return true;
            if (config.Reactions.Count > 1)
                return false;
            var only = config.Reactions[0];
            return !only.Enabled && !only.UseRegex && (only.Name == "Reaction" || only.Name == "Trigger") &&
                   only.TriggerPhrase == Reaction.DefaultTriggerPhrase && only.EnabledChannels.Count == 0;
        }

        // A save that fails at startup (a locked file) must not be mistaken for an unreadable config: keep what's in
        // memory and let ConfigSaver try again.
        private static void SaveOrRetryLater()
        {
            try
            {
                configuration?.Save();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Couldn't save the PuppetMasterKK configuration; will retry.");
                ConfigSaver.MarkDirty();
            }
        }

        public static void InitializeConfig()
        {
            Exception? loadError = null;
            try
            {
                var loaded = PluginInterface.GetPluginConfig() as Configuration;
                if (loaded == null || (!loaded.LegacyImportChecked && LooksUntouched(loaded)))
                    loaded = TryLoadLegacyConfig() ?? loaded;
                if (loaded != null)
                {
                    loaded.LegacyImportChecked = true;
                    configuration = loaded;
                    configuration.Initialize(PluginInterface);
                    var sourceVersion = configuration.Version;
                    ConfigurationUpgradeTransaction.Execute(
                        PluginInterface.ConfigFile.FullName,
                        sourceVersion,
                        ConfigVersion.CURRENT,
                        PrepareConfigurationForUse,
                        SaveOrRetryLater,
                        backupCreated: backupPath =>
                            PluginLog.Information(
                                "Backed up PuppetMasterKK configuration v{SourceVersion} to {BackupPath} before migrating to v{TargetVersion}.",
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
                    PluginLog.Error(copyError, "Failed to preserve the unreadable PuppetMasterKK configuration.");
                }
                PluginLog.Error(loadError, "PuppetMasterKK configuration could not be loaded; preserved it at {Path} and started from defaults.", preservedPath ?? "(not preserved)");
            }

            configuration = new Configuration { LegacyImportChecked = true };
            configuration.Initialize(PluginInterface);
            PrepareConfigurationForUse();
            // Read-only only when our own file exists and couldn't be kept aside: then saving would destroy it.
            configuration.ReadOnlySession = loadError != null && preservedPath == null && PluginInterface.ConfigFile.Exists;
            SaveOrRetryLater();

            if (loadError != null)
            {
                Notify(preservedPath != null
                           ? $"Your settings could not be read, so PuppetMasterKK started with defaults.\nThe old file was kept at:\n{preservedPath}"
                           : "Your settings could not be read, so PuppetMasterKK is using defaults for this session. Changes won't be saved, so the file isn't overwritten.",
                       NotificationType.Error);
            }
        }

        private static void PrepareConfigurationForUse()
        {
            var currentConfiguration = configuration!;
            ConfigurationMigrator.MigrateAndNormalize(currentConfiguration);

            if (currentConfiguration.Reactions.Count == 0)
            {
                currentConfiguration.Reactions.Add(Reaction.FromDefaults(currentConfiguration));
            }

            InitializeRegex();

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

        [PluginService]
        public static ICondition Condition { get; private set; } = null!;
    }
}
