using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PuppetMasterKK;

internal enum ImportRiskKind
{
    AnyGameCommand,
    AllowedCommand,
    SenderWait,
    ChatUnprotected,
    ChatOpen,
    RiskyUnprotected,
    RiskyOpen,
    PluginsUnprotected,
    PluginOpen,
    Unblocked,
    EmoteText,
    QueueEvery,
}

/// <summary>One thing a shared trigger would let through that the user's new triggers don't. Left out unless ticked.</summary>
internal sealed class ImportRisk(ImportRiskKind kind, string key, string label, string detail, string? canonical = null)
{
    // Allowed commands: the command as the catalog knows it, so another spelling of it can't slip past.
    internal string Canonical { get; } = canonical ?? key;
    public ImportRiskKind Kind { get; } = kind;
    // The command, protection group key or plugin name.
    public string Key { get; } = key;
    public string Label { get; } = ShareCodeReview.Clean(label);
    public string Detail { get; } = detail;
    public bool Accepted { get; set; }
}

/// <summary>A channel the shared trigger listened to that the user's new triggers don't. Not added unless ticked.</summary>
internal sealed class ImportChannel(int id, bool isPublic)
{
    public int Id { get; } = id;
    public bool IsPublic { get; } = isPublic;
    public bool Accepted { get; set; }
}

/// <summary>
/// The import review for a decoded share code, against the user's "New triggers" defaults. Everything less safe than
/// those defaults is a <see cref="ImportRisk"/>, unticked; <see cref="Build"/> leaves out whatever is still unticked.
/// The trigger always comes in turned off, for the default senders (never Anyone), on the user's default channels.
/// </summary>
internal sealed class ShareCodeReview
{
    private readonly TriggerShare share;
    private readonly Configuration configuration;
    private readonly CommandCatalog catalog;

    public List<ImportRisk> Risks { get; } = [];
    public List<ImportChannel> SuggestedChannels { get; } = [];
    // The code's Allowed entries as shown in the summary (look-alikes left out).
    public List<string> Allowed { get; } = [];
    // Allowed entries written with look-alike characters ("/ｔell" looks like /tell): never offered, always left out.
    public List<string> LookAlikes { get; } = [];
    public string Name { get; }

    /// <param name="isPluginCommand">Whether a command belongs to a loaded plugin (Service.IsPluginCommand).</param>
    /// <param name="isBuiltInChannel">Built-in chat channels; anything else in the code is dropped.</param>
    public ShareCodeReview(TriggerShare share, Configuration configuration, CommandCatalog catalog, Func<string, bool>? isPluginCommand,
        Func<int, bool> isBuiltInChannel)
    {
        this.share = share;
        this.configuration = configuration;
        this.catalog = catalog;
        // Line breaks, invisible and direction-changing characters don't belong in a name.
        var name = Clean(share.Name).Trim();
        Name = name.Length == 0 ? "Imported trigger" : name;

        if (share.AllowAllCommands && !configuration.DefaultAllowAllCommands)
            Risks.Add(new(ImportRiskKind.AnyGameCommand, string.Empty, "Any game command",
                          "Runs any game command that isn't blocked. Chat, risky and plugin commands stay protected."));

        var defaultAllowed = catalog.CanonicalSet(configuration.DefaultCommandWhitelist ?? []);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wait = catalog.Canonicalize(CommandPolicy.WaitCommand);
        foreach (var entry in share.Allowed ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;
            var command = entry.Trim();
            if (catalog.LookAlikeOf(command) is { } real)
            {
                var shown = $"{Visible(command)} looks like {Visible(real)}";
                if (!LookAlikes.Contains(shown))
                    LookAlikes.Add(shown);
                continue;
            }
            var canonical = catalog.Canonicalize(command);
            if (!seen.Add(canonical))
                continue;
            Allowed.Add(command);
            if (defaultAllowed.Contains(canonical))
                continue;
            if (canonical == wait || CommandCatalog.Normalize(command) == CommandPolicy.WaitCommand)
            {
                Risks.Add(new(ImportRiskKind.SenderWait, command, $"Allow {command} in the sender's message",
                              "Senders could pause it for up to a minute each time, keeping it busy.", canonical));
                continue;
            }
            var detail = catalog.Classify(command, isPluginCommand) switch
            {
                CommandKind.Chat => "Posts text other players can read.",
                CommandKind.Sensitive => "A risky game command (teleporting, party, gear, trading...).",
                CommandKind.Plugin => "Another plugin's command.",
                CommandKind.Unknown => "Not a command this game knows, or a plugin that isn't loaded.",
                CommandKind.Game when !configuration.DefaultAllowAllCommands => "A game command your new triggers don't allow.",
                // Emotes run anyway; blocked commands and /follow never run.
                _ => null,
            };
            if (detail != null)
                Risks.Add(new(ImportRiskKind.AllowedCommand, command, $"Allow {command}", detail, canonical));
        }

        if (!share.MotionOnly && configuration.DefaultMotionOnly)
            Risks.Add(new(ImportRiskKind.EmoteText, string.Empty, "Emotes show the sender's text",
                          "Emotes post their chat message, not just the motion."));
        if (share.ExecutionPolicy == ReactionExecutionPolicy.QueueEveryTrigger &&
            DefaultPolicy != ReactionExecutionPolicy.QueueEveryTrigger)
            Risks.Add(new(ImportRiskKind.QueueEvery, string.Empty, "Queue every trigger",
                          "Each message waits its turn, so a spammer can keep it busy. Otherwise it uses your default."));

        var defaults = configuration.DefaultProtections ?? new ProtectionSettings();
        var shared = share.Protections ?? new ShareProtections();
        AddProtectionRisks(shared.Chat, shared.OpenChat, defaults.Chat, defaults.OpenChat, ProtectionGroups.Chat,
                           ImportRiskKind.ChatUnprotected, ImportRiskKind.ChatOpen, "Chat commands unprotected",
                           "Any chat command could post for you.");
        AddProtectionRisks(shared.Risky, shared.OpenRisky, defaults.Risky, defaults.OpenRisky, ProtectionGroups.Risky,
                           ImportRiskKind.RiskyUnprotected, ImportRiskKind.RiskyOpen, "Risky game commands unprotected",
                           "Teleporting, leaving the party, changing gear, trading and more could run.");
        if (!shared.Plugins && defaults.Plugins)
            Risks.Add(new(ImportRiskKind.PluginsUnprotected, string.Empty, "Plugin commands unprotected",
                          "Any plugin's commands could run."));
        foreach (var plugin in Distinct(shared.OpenPlugins))
        {
            if (Opens(defaults.Plugins, defaults.OpenPlugins, plugin))
                continue;
            Risks.Add(new(ImportRiskKind.PluginOpen, plugin, $"Unprotect {plugin}",
                          ProtectionGroups.RiskyPlugins.TryGetValue(plugin, out var reason)
                              ? reason + "."
                              : "Its commands would run without being allowed."));
        }

        var sharedBlocked = catalog.CanonicalSet(share.Blocked ?? []);
        var unblocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in configuration.DefaultCommandBlacklist ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;
            var canonical = catalog.Canonicalize(entry);
            if (sharedBlocked.Contains(canonical) || !unblocked.Add(canonical))
                continue;
            Risks.Add(new(ImportRiskKind.Unblocked, entry.Trim(), $"Unblock {entry.Trim()}", "Your new triggers block it."));
        }

        var defaultChannels = configuration.DefaultEnabledChannels ?? [];
        foreach (var channel in share.Channels ?? [])
        {
            if (channel is < 0 or > ushort.MaxValue || !isBuiltInChannel(channel) || defaultChannels.Contains(channel) ||
                SuggestedChannels.Exists(existing => existing.Id == channel))
                continue;
            SuggestedChannels.Add(new ImportChannel(channel, PluginUiLogic.IsPublicChannel(channel)));
        }
    }

    // What the user's new triggers do while one is already running.
    private ReactionExecutionPolicy DefaultPolicy => Reaction.FromDefaults(configuration).ExecutionPolicy;

    /// <summary>Text from a code without control, invisible or direction-changing characters.</summary>
    internal static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!IsHidden(c))
                builder.Append(c);
        }
        return builder.ToString();
    }

    // Hidden characters shown as "?", so a look-alike can be told apart from the real command.
    private static string Visible(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
            builder.Append(IsHidden(c) ? '?' : c);
        return builder.ToString();
    }

    private static bool IsHidden(char c)
        => char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format;

    private void AddProtectionRisks(bool sharedMaster, List<string>? sharedOpen, bool defaultMaster, List<string>? defaultOpen,
        ProtectionGroup[] groups, ImportRiskKind masterKind, ImportRiskKind openKind, string masterLabel, string masterDetail)
    {
        if (!sharedMaster && defaultMaster)
            Risks.Add(new(masterKind, string.Empty, masterLabel, masterDetail));
        foreach (var key in Distinct(sharedOpen))
        {
            var group = Array.Find(groups, candidate => candidate.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (group == null || Opens(defaultMaster, defaultOpen, group.Key))
                continue; // unknown keys open nothing
            Risks.Add(new(openKind, group.Key, $"Unprotect {group.Label}", group.Description));
        }
    }

    // True when these settings let a group or plugin's commands run unlisted.
    private static bool Opens(bool master, List<string>? open, string key)
        => !master || (open?.Exists(entry => entry.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? false);

    private static List<string> Distinct(List<string>? items)
    {
        var result = new List<string>();
        foreach (var item in items ?? [])
        {
            if (!string.IsNullOrWhiteSpace(item) && !result.Exists(existing => existing.Equals(item.Trim(), StringComparison.OrdinalIgnoreCase)))
                result.Add(item.Trim());
        }
        return result;
    }

    private bool Accepted(ImportRiskKind kind, string key = "")
        => Risks.Exists(risk => risk.Kind == kind && risk.Accepted && risk.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The trigger to add: off, default senders and channels (plus ticked suggestions), unticked risks left out.</summary>
    public Reaction Build()
    {
        var defaults = configuration.DefaultProtections ?? new ProtectionSettings();
        var shared = share.Protections ?? new ShareProtections();

        var allowed = new List<string>();
        foreach (var entry in share.Allowed ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;
            var command = entry.Trim();
            if (catalog.LookAlikeOf(command) != null)
                continue;
            var canonical = catalog.Canonicalize(command);
            var risky = Risks.Find(risk => risk.Kind is ImportRiskKind.AllowedCommand or ImportRiskKind.SenderWait &&
                                           risk.Canonical.Equals(canonical, StringComparison.OrdinalIgnoreCase));
            if (risky == null || risky.Accepted)
                allowed.Add(command);
        }

        var blocked = new List<string>();
        foreach (var entry in share.Blocked ?? [])
        {
            if (!string.IsNullOrWhiteSpace(entry))
                blocked.Add(entry.Trim());
        }
        foreach (var risk in Risks)
        {
            if (risk.Kind == ImportRiskKind.Unblocked && !risk.Accepted)
                blocked.Add(risk.Key);
        }

        var choices = new List<ReactionChoice>();
        foreach (var choice in share.Choices ?? [])
        {
            if (choice != null)
                choices.Add(new ReactionChoice { Word = Clean(choice.Word), Commands = choice.Commands ?? string.Empty });
        }

        var channels = new List<int>(configuration.DefaultEnabledChannels ?? []);
        foreach (var channel in SuggestedChannels)
        {
            if (channel.Accepted)
                channels.Add(channel.Id);
        }

        var reaction = new Reaction
        {
            Enabled = false,
            Name = Name,
            UseRegex = share.UseRegex,
            TriggerPhrase = string.IsNullOrWhiteSpace(share.TriggerPhrase) ? Reaction.DefaultTriggerPhrase : share.TriggerPhrase,
            CustomPhrase = share.CustomPhrase ?? string.Empty,
            ReplaceMatch = share.ReplaceMatch ?? string.Empty,
            TestInput = string.Empty,
            MotionOnly = share.MotionOnly || (configuration.DefaultMotionOnly && !Accepted(ImportRiskKind.EmoteText)),
            CooldownSeconds = share.CooldownSeconds,
            ExecutionPolicy = share.ExecutionPolicy == ReactionExecutionPolicy.QueueEveryTrigger &&
                              DefaultPolicy != ReactionExecutionPolicy.QueueEveryTrigger && !Accepted(ImportRiskKind.QueueEvery)
                ? DefaultPolicy
                : share.ExecutionPolicy,
            AllowAllCommands = share.AllowAllCommands && (configuration.DefaultAllowAllCommands || Accepted(ImportRiskKind.AnyGameCommand)),
            CommandWhitelist = allowed,
            CommandBlacklist = blocked,
            Senders = new SenderFilter(),
            NoProtections = false,
            Protections = new ProtectionSettings
            {
                Chat = Master(shared.Chat, defaults.Chat, ImportRiskKind.ChatUnprotected),
                OpenChat = OpenKeys(shared.OpenChat, ImportRiskKind.ChatOpen),
                Risky = Master(shared.Risky, defaults.Risky, ImportRiskKind.RiskyUnprotected),
                OpenRisky = OpenKeys(shared.OpenRisky, ImportRiskKind.RiskyOpen),
                Plugins = Master(shared.Plugins, defaults.Plugins, ImportRiskKind.PluginsUnprotected),
                OpenPlugins = OpenKeys(shared.OpenPlugins, ImportRiskKind.PluginOpen),
            },
            EnabledChannels = channels,
            PerSenderCooldownSeconds = share.PerSenderCooldownSeconds,
            OneWaitingPerSender = share.OneWaitingPerSender,
            ChoiceMode = share.ChoiceMode,
            Choices = choices,
            FinalCommands = [.. share.FinalCommands ?? []],
            FinalWhen = share.FinalWhen,
        };
        ConfigurationMigrator.NormalizeReaction(reaction);
        return reaction;
    }

    // Protected when the code protects it, or the user's defaults do and turning it off wasn't ticked.
    private bool Master(bool shared, bool byDefault, ImportRiskKind kind)
        => shared || (byDefault && !Accepted(kind));

    // The code's opened groups or plugins that the defaults also open (no risk) or that were ticked. Unknown keys are kept
    // out: they open nothing.
    private List<string> OpenKeys(List<string>? sharedOpen, ImportRiskKind kind)
    {
        var defaults = configuration.DefaultProtections ?? new ProtectionSettings();
        var result = new List<string>();
        foreach (var key in Distinct(sharedOpen))
        {
            var known = kind switch
            {
                ImportRiskKind.ChatOpen => Array.Find(ProtectionGroups.Chat, group => group.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Key,
                ImportRiskKind.RiskyOpen => Array.Find(ProtectionGroups.Risky, group => group.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Key,
                _ => key,
            };
            if (known == null)
                continue;
            var openByDefault = kind switch
            {
                ImportRiskKind.ChatOpen => Opens(defaults.Chat, defaults.OpenChat, known),
                ImportRiskKind.RiskyOpen => Opens(defaults.Risky, defaults.OpenRisky, known),
                _ => Opens(defaults.Plugins, defaults.OpenPlugins, known),
            };
            if (openByDefault || Accepted(kind, known))
                result.Add(known);
        }
        return result;
    }
}
