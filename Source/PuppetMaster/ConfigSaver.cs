using System;

namespace PuppetMaster;

// The UI marks the config dirty instead of writing it on every keystroke; it is saved once edits pause, when the
// window closes, and on unload. Framework thread only.
internal static class ConfigSaver
{
    private const long QuietMs = 600;
    private static bool dirty;
    private static long lastChange;

    public static void MarkDirty()
    {
        dirty = true;
        lastChange = Environment.TickCount64;
    }

    // Called every frame.
    public static void Tick()
    {
        if (dirty && Environment.TickCount64 - lastChange >= QuietMs)
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
            Service.PluginLog.Error(ex, "Failed to save the PuppetMaster configuration.");
        }
    }
}
