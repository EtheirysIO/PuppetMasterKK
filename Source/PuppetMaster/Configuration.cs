using Dalamud.Configuration;
using Dalamud.Plugin;
using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PuppetMaster
{
    public enum ReactionExecutionPolicy
    {
        QueueEveryTrigger,
        IgnoreWhileRunning,
        QueueLatestTrigger,
        RestartImmediately,
    }

    public enum ReactionNotificationSetting
    {
        Inherit,
        Enabled,
        Disabled,
    }

    public class ConfigVersion
    {
        public const int CURRENT = 4;
    }

    public class ChannelSetting
    {
        public int ChatType { get; set; }
        public string Name { get; set; } = string.Empty;
        //---- Deprecated, setting will be managed per Reaction
        public bool Enabled { get; set; }
    }

    public class Reaction
    {
        public const string DefaultTriggerPhrase = "please do";

        public bool Enabled { get; set; } = false;
        public string Name { get; set; } = string.Empty;
        public string TriggerPhrase { get; set; } = DefaultTriggerPhrase;
        // Legacy field. False is normalized into default blacklist entries on load.
        public bool AllowSit { get; set; } = true;
        public bool MotionOnly { get; set; } = true;
        public int CooldownSeconds { get; set; } = 0;
        // QueueEveryTrigger preserves the behavior of configurations created before execution policies existed.
        public ReactionExecutionPolicy ExecutionPolicy { get; set; } = ReactionExecutionPolicy.QueueEveryTrigger;
        public ReactionNotificationSetting ProgressNotifications { get; set; } = ReactionNotificationSetting.Inherit;
        public ReactionNotificationSetting SuppressedNotifications { get; set; } = ReactionNotificationSetting.Inherit;
        // "Any command except those blocked" mode. Still live: command execution honors it.
        public bool AllowAllCommands { get; set; } = false;
        public bool UseRegex { get; set; } = false;
        public string CustomPhrase { get; set; } = string.Empty;
        public string ReplaceMatch { get; set; } = string.Empty;
        public string TestInput { get; set; } = string.Empty;
        public List<int> EnabledChannels { get; set; } = [];
        public List<string> CommandWhitelist { get; set; } = [];
        public List<string> CommandBlacklist { get; set; } = [];
        // Who may trigger this reaction. Reactions from before v4 are migrated to Anyone.
        public SenderFilter Senders { get; set; } = new();
        // Runtime only. Compiled regexes must never be persisted: Newtonsoft would rebuild them without the match
        // timeout, and a stale saved pattern would shadow later edits to TriggerPhrase/CustomPhrase.
        [JsonIgnore]
        public Regex? Rx;
        [JsonIgnore]
        public Regex? CustomRx;

        public static Reaction CreateDefault(
            string name = "Reaction",
            IEnumerable<string>? commandWhitelist = null,
            IEnumerable<string>? commandBlacklist = null,
            bool allowAllCommands = false,
            bool motionOnly = true,
            IEnumerable<int>? enabledChannels = null)
        {
            return new Reaction
            {
                Name = name,
                AllowAllCommands = allowAllCommands,
                MotionOnly = motionOnly,
                ExecutionPolicy = ReactionExecutionPolicy.IgnoreWhileRunning,
                EnabledChannels = enabledChannels != null ? new List<int>(enabledChannels) : [],
                CommandWhitelist = commandWhitelist != null ? new List<string>(commandWhitelist) : [],
                CommandBlacklist = commandBlacklist != null
                    ? new List<string>(commandBlacklist)
                    : ["/sit", "/groundsit", "/lounge"],
            };
        }
    }

    // Replying to emotes aimed at you (formerly the separate "Right Back At You" plugin).
    public class EmoteReplySettings
    {
        public bool Enabled { get; set; } = false;
        public bool TargetBack { get; set; } = true;
        public bool MotionOnly { get; set; } = true;
        // After replying to a player, their emotes are ignored for this long. This is what stops two players who
        // both reply to emotes from emoting at each other forever.
        public int PerPlayerCooldownSeconds { get; set; } = 10;
        public SenderFilter Senders { get; set; } = new();
    }

    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = ConfigVersion.CURRENT;

        //---- Version 0 Config [DEPRECATED, WILL NOT BE USED]
        public string TriggerPhrase { get; set; } = "please do";
        public bool AllowSit { get; set; } = false;
        public bool MotionOnly { get; set; } = true;
        public bool AllowAllCommands { get; set; } = false;
        public bool UseRegex { get; set; } = false;
        public string CustomPhrase { get; set; } = string.Empty;
        public string ReplaceMatch { get; set; } = string.Empty;
        public string TestInput { get; set; } = string.Empty;

        //---- Version 1 Config
        public List<ChannelSetting> EnabledChannels { get; set; } = [];
        public List<ChannelSetting> CustomChannels { get; set; } = [];
        public List<Reaction> Reactions { get; set; } = [];
        public int CurrentReactionEdit = -1;
        public bool DebugLogTypes { get; set; } = false;
        public bool ShowReactionNotifications { get; set; } = true;
        public bool ShowSuppressedReactionNotifications { get; set; } = false;
        public List<string> DefaultCommandWhitelist { get; set; } = [];
        // Replace, not merge: Newtonsoft otherwise appends the saved items to this non-empty default, so a user could
        // never remove a default entry or empty the list.
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> DefaultCommandBlacklist { get; set; } = ["/sit", "/groundsit", "/lounge"];
        public bool DefaultAllowAllCommands { get; set; } = false;
        public bool DefaultMotionOnly { get; set; } = true;
        public List<int> DefaultEnabledChannels { get; set; } = [];
        public int MaxRegexLength { get; set; } = 1000;

        //---- Version 4 Config
        // Your own chat lines never trigger reactions (stops a reaction from re-triggering itself).
        public bool IgnoreOwnMessages { get; set; } = true;
        public EmoteReplySettings EmoteReplies { get; set; } = new();
        // Set once the old Right Back At You settings have been looked for, so they're imported only once.
        public bool CopycatImportChecked { get; set; } = false;

        [NonSerialized]
        private IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.pluginInterface!.SavePluginConfig(this);
        }
    }
}
