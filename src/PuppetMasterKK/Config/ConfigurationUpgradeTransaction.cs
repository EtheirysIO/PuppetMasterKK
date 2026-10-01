using System;
using System.IO;

namespace PuppetMasterKK;

internal static class ConfigurationUpgradeTransaction
{
    public static string? Execute(
        string configPath,
        int sourceVersion,
        int targetVersion,
        Action prepareAndValidate,
        Action save,
        DateTime? utcNow = null,
        Action<string>? backupCreated = null)
    {
        string? backupPath = null;
        if (sourceVersion < targetVersion && File.Exists(configPath))
        {
            backupPath = CopyAside(configPath, $"v{sourceVersion}", "backup", utcNow);
            backupCreated?.Invoke(backupPath);
        }

        prepareAndValidate();
        save();
        return backupPath;
    }

    // Copies the config next to itself as "<name>.<tag>.<timestamp>.<kind>.json", never overwriting an earlier copy.
    public static string CopyAside(string configPath, string tag, string kind, DateTime? utcNow = null)
    {
        var timestamp = (utcNow ?? DateTime.UtcNow).ToString("yyyyMMddHHmmssfff");
        var directory = Path.GetDirectoryName(configPath)!;
        var stem = $"{Path.GetFileNameWithoutExtension(configPath)}.{tag}.{timestamp}";
        var path = Path.Combine(directory, $"{stem}.{kind}.json");
        for (var attempt = 1; File.Exists(path); attempt++)
            path = Path.Combine(directory, $"{stem}-{attempt}.{kind}.json");
        File.Copy(configPath, path, overwrite: false);
        return path;
    }
}
