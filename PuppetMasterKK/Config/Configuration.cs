using Dalamud.Configuration;
using Dalamud.Plugin;
using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PuppetMasterKK
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
        // Chat channels, risky game commands and plugins this trigger protects.
        public ProtectionSettings Protections { get; set; } = new();
        // Every protection off: any command runs except /follow (the Allowed and Blocked lists are off too).
        public bool NoProtections { get; set; } = false;
        // Runtime only. Compiled regexes must never be persisted: Newtonsoft would rebuild them without the match
        // timeout, and a stale saved pattern would shadow later edits to TriggerPhrase/CustomPhrase.
        [JsonIgnore]
        public Regex? Rx;
        [JsonIgnore]
        public Regex? CustomRx;

        public static Reaction CreateDefault(
            string name = "Trigger",
            IEnumerable<string>? commandWhitelist = null,
            IEnumerable<string>? commandBlacklist = null,
            bool allowAllCommands = false,
            bool motionOnly = true,
            IEnumerable<int>? enabledChannels = null,
            ProtectionSettings? protections = null)
        {
            return new Reaction
            {
                Protections = protections?.Clone() ?? new ProtectionSettings(),
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
        // Emotes never answered (postures that stick: a stranger shouldn't be able to make you lie down).
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> BlockedEmotes { get; set; } = ["/sit", "/groundsit", "/lounge", "/doze"];
        // "When they use this emote on me, reply with that one instead". An empty reply means don't reply.
        public List<EmoteOverride> Overrides { get; set; } = [];

        // The emote to answer `command` with: its override (empty: don't answer), or the same emote.
        public static string ReplyFor(IReadOnlyList<EmoteOverride> overrides, string command, Func<string, string> canonicalize)
        {
            var canonical = canonicalize(command);
            foreach (var entry in overrides)
            {
                if (entry != null && !string.IsNullOrWhiteSpace(entry.When) && canonicalize(entry.When.Trim()) == canonical)
                    return (entry.Reply ?? string.Empty).Trim();
            }
            return command;
        }

        // Below this, two players who both answer emotes would keep answering each other.
        public const int MinimumCooldownSeconds = 3;
    }

    public class EmoteOverride
    {
        public string When { get; set; } = string.Empty;
        public string Reply { get; set; } = string.Empty;
    }

    // Follow mode: "<call name> <follow word> [player]" makes you target that player (or the sender) and /follow them;
    // "<call name> <stop word>" stops everything.
    public class FollowSettings
    {
        public const string TargetPlaceholder = "<target>";

        public bool Enabled { get; set; } = false;
        // "|" separates alternatives, like a trigger phrase.
        public string CallNames { get; set; } = string.Empty;
        public string FollowWords { get; set; } = "follow";
        public string StopWords { get; set; } = "stop";
        // "Ami come": come to the sender and follow them.
        public string ComeWords { get; set; } = "come";
        // When the player is in the zone but too far to follow, walk to them with vnavmesh first (when it's loaded).
        public bool WalkWithVnavmesh { get; set; } = true;
        // "Ami mimic me": copy that player's emotes, aimed at whoever they aim them at, until the stop word.
        public string MimicWords { get; set; } = "mimic";
        public bool MimicMotionOnly { get; set; } = true;
        // Tell, Party, Free Company.
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<int> Channels { get; set; } = [13, 14, 24];
        public SenderFilter Senders { get; set; } = new();
        // Never take a request from these ("Name@World" or "Name"), whatever Senders allows.
        public List<string> NeverFrom { get; set; } = [];
        // When anyone is listed, only they are followed.
        public List<string> OnlyFollow { get; set; } = [];
        public List<string> NeverFollow { get; set; } = [];
        public bool ReplyWhenNotNearby { get; set; } = true;
        public string NotNearbyMessage { get; set; } = "Sorry, I don't see <target> near me.";
        // Stop takes one tiny automove step: moving is what ends following, emote loops, sitting and lying down.
        public bool StopMoves { get; set; } = true;
        // Extra commands run on stop (in order, after the step).
        public List<string> StopCommands { get; set; } = [];
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
        public ProtectionSettings DefaultProtections { get; set; } = new();
        public List<int> DefaultEnabledChannels { get; set; } = [];
        public int MaxRegexLength { get; set; } = 1000;

        //---- Version 4 Config
        // Your own chat lines never trigger reactions (stops a reaction from re-triggering itself).
        public bool IgnoreOwnMessages { get; set; } = true;
        public EmoteReplySettings EmoteReplies { get; set; } = new();
        public FollowSettings Follow { get; set; } = new();
        // Set once the old Right Back At You settings have been looked for, so they're imported only once.
        public bool CopycatImportChecked { get; set; } = false;
        // Set once the old Puppet Master's settings have been looked for, so they're imported at most once.
        public bool LegacyImportChecked { get; set; } = false;

        // Migrated reactions that let anyone run any game command: shown to the user once, then cleared. Not saved.
        [JsonIgnore]
        public List<string> ReviewAfterMigration { get; } = [];

        // Appearance (phys1ksUI).
        public phys1ksUI.AccentColor Accent { get; set; } = phys1ksUI.AccentColor.Orange;
        public float TextScale { get; set; } = 1f;
        public bool Colorblind { get; set; } = false;

        [NonSerialized]
        private IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        // Set when the file on disk couldn't be read or copied aside: this session runs on defaults and must never
        // write over the user's only copy.
        [JsonIgnore]
        public bool ReadOnlySession { get; set; }

        public void Save()
        {
            if (ReadOnlySession)
                return;
            this.pluginInterface!.SavePluginConfig(this);
        }
    }
}
