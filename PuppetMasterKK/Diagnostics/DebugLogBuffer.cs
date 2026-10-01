using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PuppetMasterKK;

// Sequence identifies an entry for its whole life (row ids stay put while older entries scroll out).
internal readonly record struct DebugLogEntry(long Sequence, int ChatTypeId, string Text, string TriggerText);

internal static class DebugLogBuffer
{
    private const int MaximumEntries = 500;
    // Longer text (other players' messages) is cut: the buffer stays small and the log never draws huge strings.
    private const int MaximumTextLength = 2000;
    private static readonly object Sync = new();
    private static readonly Queue<DebugLogEntry> Entries = new();
    private static long revision;
    private static long nextSequence;

    public static long Revision
    {
        get
        {
            lock (Sync)
                return revision;
        }
    }

    public static void Add(int chatTypeId, string text, string triggerText)
    {
        lock (Sync)
        {
            Entries.Enqueue(new DebugLogEntry(++nextSequence, chatTypeId, Cap(text), Cap(triggerText)));
            revision++;
            while (Entries.Count > MaximumEntries)
                Entries.Dequeue();
        }
    }

    private static string Cap(string? text)
    {
        text ??= string.Empty;
        return text.Length <= MaximumTextLength ? text : text[..MaximumTextLength];
    }

    public static DebugLogEntry[] Snapshot()
    {
        lock (Sync)
            return Entries.ToArray();
    }

    // The entries and the revision they belong to, read together (a cache keyed on the revision never misses one).
    public static (DebugLogEntry[] Entries, long Revision) SnapshotWithRevision()
    {
        lock (Sync)
            return (Entries.ToArray(), revision);
    }

    public static void Clear()
    {
        lock (Sync)
        {
            Entries.Clear();
            revision++;
        }
    }

    public static string SaveSnapshot(string directory, DebugLogEntry[] entries)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(
            directory,
            $"PuppetMasterKK-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log.txt");
        var lines = new List<string>(entries.Length + 3)
        {
            "# PuppetMasterKK message log",
            $"# Exported: {DateTimeOffset.Now:O}",
            $"# Entries: {entries.Length}",
        };
        // One line per entry: a line break inside a message can't pass for another entry.
        lines.AddRange(entries.Select(static entry => entry.Text.ReplaceLineEndings(" ")));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
        return path;
    }
}
