namespace PuppetMasterKK;

/// <summary>
/// A trigger's per-person cooldown: <see cref="Cooldowns"/> behind a lock (the dispatcher and the UI both touch it).
/// Bounded like Cooldowns, so a crowd of senders can't grow it without end.
/// </summary>
internal sealed class SenderCooldowns
{
    private readonly Cooldowns cooldowns = new();

    /// <summary>"Name@World", or just the name when the world isn't known. Everyone with no name shares "".</summary>
    public static string KeyFor(in SenderInfo sender)
        => sender.Name.Length == 0 ? string.Empty : sender.World.Length == 0 ? sender.Name : sender.Key;

    public bool IsWaiting(string key, long now)
    {
        lock (cooldowns)
            return cooldowns.IsWaiting(key, now);
    }

    public void Start(string key, long now, long duration)
    {
        if (duration <= 0)
            return;
        lock (cooldowns)
            cooldowns.Start(key, now, duration);
    }

    public void Clear()
    {
        lock (cooldowns)
            cooldowns.Clear();
    }

    public int Count
    {
        get
        {
            lock (cooldowns)
                return cooldowns.Count;
        }
    }
}
