using System;

namespace PuppetMasterKK;

// The UI marks the config dirty instead of writing it on every keystroke; it is saved once edits pause, when the
// window closes, and on unload. Framework thread only.
internal static class ConfigSaver
{
    private const long QuietMs = 600;
    // After a failed save (a locked file), wait this long before trying again instead of retrying every frame.
    private const long RetryMs = 10_000;
    private static bool dirty;
    private static long saveAt;

    public static void MarkDirty()
    {
        dirty = true;
        saveAt = Environment.TickCount64 + QuietMs;
    }

    // Called every frame.
    public static void Tick()
    {
        if (dirty && Environment.TickCount64 >= saveAt)
            Flush();
    }

    public static void Flush()
    {
        if (!dirty)
            return;
        dirty = false;
        try
        {
            Service.configuration?.Save();
        }
        catch (Exception ex)
        {
            Service.PluginLog.Error(ex, "Failed to save the PuppetMasterKK configuration; will retry.");
            dirty = true;
            saveAt = Environment.TickCount64 + RetryMs;
        }
    }
}
