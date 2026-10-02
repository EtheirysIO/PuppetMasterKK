using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PuppetMasterKK;

/// <summary>
/// What a share code carries: a trigger's own rules, never who may trigger it (no player names), its notifications,
/// its Try it message (older codes that have one are read and it's ignored), whether it's on, custom channels or its turn group (a name for the sharer's own triggers: it means nothing to
/// whoever imports it, and joining one of theirs by chance would hold their triggers up). Its own type, so nothing
/// added to <see cref="Reaction"/> is ever shared by accident.
/// </summary>
public sealed class TriggerShare
{
    [JsonProperty("v")]
    public int V { get; set; }
    public string? Name { get; set; }
    public bool UseRegex { get; set; }
    public string? TriggerPhrase { get; set; }
    public string? CustomPhrase { get; set; }
    public string? ReplaceMatch { get; set; }
    public bool MotionOnly { get; set; } = true;
    public int CooldownSeconds { get; set; }
    public ReactionExecutionPolicy ExecutionPolicy { get; set; } = ReactionExecutionPolicy.IgnoreWhileRunning;
    public bool AllowAllCommands { get; set; }
    public List<string>? Allowed { get; set; }
    public List<string>? Blocked { get; set; }
    public ShareProtections? Protections { get; set; }
    // Never written. Read only so a hand-made code that asks for it can be refused.
    public bool NoProtections { get; set; }
    public bool ShouldSerializeNoProtections() => false;
    public int PerSenderCooldownSeconds { get; set; }
    public bool OneWaitingPerSender { get; set; }
    public ChoiceMode ChoiceMode { get; set; }
    public List<ShareChoice>? Choices { get; set; }
    public List<string>? FinalCommands { get; set; }
    public FinalActionWhen FinalWhen { get; set; }
    // Built-in channel numbers only.
    public List<int>? Channels { get; set; }
}

public sealed class ShareProtections
{
    public bool Chat { get; set; } = true;
    public List<string>? OpenChat { get; set; }
    public bool Risky { get; set; } = true;
    public List<string>? OpenRisky { get; set; }
    public bool Plugins { get; set; } = true;
    public List<string>? OpenPlugins { get; set; }
}

public sealed class ShareChoice
{
    public string? Word { get; set; }
    public string? Commands { get; set; }
}

/// <summary>
/// Share codes: "PMKK1." + base64url(Deflate(UTF-8 JSON of a <see cref="TriggerShare"/>)). Decoding treats the code as
/// hostile: size capped before and after decompressing, no type names, shallow JSON, every string and list capped.
/// </summary>
internal static class ShareCodeCodec
{
    public const string Prefix = "PMKK1.";
    public const int Version = 1;
    public const int MaxCodeLength = 16 * 1024;
    public const int MaxJsonBytes = 64 * 1024;
    public const int MaxDepth = 8;
    public const int MaxNameLength = 100;
    public const int MaxCommandsLength = 500;
    public const int MaxEntryLength = 100;
    public const int MaxListCount = 64;

    private static readonly JsonSerializerSettings Settings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        // "$type", "$id", "$ref" are plain unknown properties: skipped, never acted on.
        MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        DateParseHandling = DateParseHandling.None,
        MaxDepth = MaxDepth,
        NullValueHandling = NullValueHandling.Ignore,
    };

    /// <summary>
    /// A trigger as a share code. A trigger with no protections is shared with them on (<paramref name="protectionsTurnedOn"/>).
    /// False (with <paramref name="error"/>) when it's too big to share.
    /// </summary>
    public static bool TryEncode(Reaction reaction, Func<int, bool> isBuiltInChannel, int maxPatternLength, out string code,
        out bool protectionsTurnedOn, out string error)
    {
        code = string.Empty;
        protectionsTurnedOn = reaction.NoProtections;
        var share = ToShare(reaction, isBuiltInChannel);
        if (Validate(share, maxPatternLength) is { } invalid)
        {
            error = invalid;
            return false;
        }
        code = Pack(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(share, Formatting.None, Settings)));
        if (code.Length > MaxCodeLength)
        {
            error = "This trigger is too big to share.";
            code = string.Empty;
            return false;
        }
        error = string.Empty;
        return true;
    }

    internal static TriggerShare ToShare(Reaction reaction, Func<int, bool> isBuiltInChannel)
    {
        var protections = reaction.Protections ?? new ProtectionSettings();
        var channels = new List<int>();
        foreach (var channel in reaction.EnabledChannels ?? [])
        {
            if (isBuiltInChannel(channel) && !channels.Contains(channel))
                channels.Add(channel);
        }
        var choices = new List<ShareChoice>();
        foreach (var choice in reaction.Choices ?? [])
        {
            if (choice != null)
                choices.Add(new ShareChoice { Word = choice.Word ?? string.Empty, Commands = choice.Commands ?? string.Empty });
        }
        return new TriggerShare
        {
            V = Version,
            Name = reaction.Name ?? string.Empty,
            UseRegex = reaction.UseRegex,
            TriggerPhrase = reaction.TriggerPhrase ?? string.Empty,
            CustomPhrase = reaction.CustomPhrase ?? string.Empty,
            ReplaceMatch = reaction.ReplaceMatch ?? string.Empty,
            MotionOnly = reaction.MotionOnly,
            CooldownSeconds = reaction.CooldownSeconds,
            ExecutionPolicy = reaction.ExecutionPolicy,
            AllowAllCommands = reaction.AllowAllCommands,
            Allowed = [.. reaction.CommandWhitelist ?? []],
            Blocked = [.. reaction.CommandBlacklist ?? []],
            Protections = new ShareProtections
            {
                Chat = protections.Chat,
                OpenChat = [.. protections.OpenChat ?? []],
                Risky = protections.Risky,
                OpenRisky = [.. protections.OpenRisky ?? []],
                Plugins = protections.Plugins,
                OpenPlugins = [.. protections.OpenPlugins ?? []],
            },
            NoProtections = false,
            PerSenderCooldownSeconds = reaction.PerSenderCooldownSeconds,
            OneWaitingPerSender = reaction.OneWaitingPerSender,
            ChoiceMode = reaction.ChoiceMode,
            Choices = choices,
            FinalCommands = [.. reaction.FinalCommands ?? []],
            FinalWhen = reaction.FinalWhen,
            Channels = channels,
        };
    }

    /// <summary>Reads a pasted code. False (with a short reason) for anything that isn't a valid code we can use.</summary>
    public static bool TryDecode(string? input, int maxPatternLength, out TriggerShare share, out string error)
    {
        share = new TriggerShare();
        var text = input?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            error = "The clipboard is empty. Copy a share code first.";
            return false;
        }
        if (text.Length > MaxCodeLength)
        {
            error = "That's too long to be a share code.";
            return false;
        }
        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            error = text.StartsWith("PMKK", StringComparison.Ordinal) && !text.StartsWith("PMKK1", StringComparison.Ordinal)
                ? "This code is from a newer PuppetMasterKK. Update the plugin first."
                : "The clipboard doesn't hold a PuppetMasterKK share code.";
            return false;
        }

        TriggerShare? parsed;
        try
        {
            var json = Unpack(text[Prefix.Length..]);
            if (json == null)
            {
                error = "This share code is too large.";
                return false;
            }
            parsed = JsonConvert.DeserializeObject<TriggerShare>(json, Settings);
        }
        catch (Exception)
        {
            error = "This share code is damaged or incomplete. Copy it again.";
            return false;
        }
        if (parsed == null)
        {
            error = "This share code is damaged or incomplete. Copy it again.";
            return false;
        }
        if (parsed.V > Version)
        {
            error = "This code is from a newer PuppetMasterKK. Update the plugin first.";
            return false;
        }
        if (parsed.V < 1)
        {
            error = "This share code is damaged or incomplete. Copy it again.";
            return false;
        }
        if (parsed.NoProtections)
        {
            error = "This code asks for a trigger with no protections. That can't be imported.";
            return false;
        }
        if (Validate(parsed, maxPatternLength) is { } invalid)
        {
            error = invalid;
            return false;
        }
        share = parsed;
        error = string.Empty;
        return true;
    }

    // Null when every string and list is within its limit.
    private static string? Validate(TriggerShare share, int maxPatternLength)
    {
        if (Over(share.Name, MaxNameLength)) return "Its name is too long.";
        if (Over(share.TriggerPhrase, maxPatternLength) || Over(share.CustomPhrase, maxPatternLength))
            return "Its phrase or pattern is longer than your limit (Settings).";
        if (Over(share.ReplaceMatch, MaxCommandsLength)) return "Its commands are too long.";
        if (TooMany(share.Allowed) || TooMany(share.Blocked)) return "Its Allowed or Blocked list is too long.";
        if (share.Protections is { } protections &&
            (TooMany(protections.OpenChat) || TooMany(protections.OpenRisky) || TooMany(protections.OpenPlugins)))
            return "Its protections list is too long.";
        if (share.Channels is { Count: > MaxListCount }) return "It has too many channels.";
        if (share.Choices is { Count: > Reaction.MaxChoices }) return $"It has more than {Reaction.MaxChoices} choices.";
        foreach (var choice in share.Choices ?? [])
        {
            if (choice != null && (Over(choice.Word, MaxNameLength) || Over(choice.Commands, MaxCommandsLength)))
                return "One of its choices is too long.";
        }
        if (share.FinalCommands is { Count: > Reaction.MaxFinalCommands })
            return $"Its final action has more than {Reaction.MaxFinalCommands} commands.";
        foreach (var line in share.FinalCommands ?? [])
        {
            if (Over(line, MaxCommandsLength))
                return "Its final action is too long.";
        }
        return null;
    }

    private static bool Over(string? text, int max) => text != null && text.Length > max;

    private static bool TooMany(List<string>? items)
    {
        if (items == null)
            return false;
        if (items.Count > MaxListCount)
            return true;
        foreach (var item in items)
        {
            if (Over(item, MaxEntryLength))
                return true;
        }
        return false;
    }

    // UTF-8 JSON -> the code.
    internal static string Pack(byte[] json)
    {
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(json, 0, json.Length);
        return Prefix + Convert.ToBase64String(buffer.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // The code's body -> JSON text, or null when it inflates past MaxJsonBytes. Throws on anything malformed.
    private static string? Unpack(string body)
    {
        var builder = new StringBuilder(body.Length + 3);
        foreach (var c in body)
        {
            if (char.IsWhiteSpace(c))
                continue; // a code that was wrapped across lines
            builder.Append(c switch { '-' => '+', '_' => '/', _ => c });
        }
        while (builder.Length % 4 != 0)
            builder.Append('=');
        var compressed = Convert.FromBase64String(builder.ToString());

        using var input = new MemoryStream(compressed, writable: false);
        using var inflate = new DeflateStream(input, CompressionMode.Decompress);
        // Never more than the cap (plus one byte to notice going over) is inflated.
        var output = new byte[MaxJsonBytes + 1];
        var total = 0;
        while (total < output.Length)
        {
            var read = inflate.Read(output, total, output.Length - total);
            if (read == 0)
                break;
            total += read;
        }
        if (total > MaxJsonBytes)
            return null;
        return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(output, 0, total);
    }
}
