using Newtonsoft.Json;
using PuppetMaster;

// Mirrors how Dalamud persists plugin configs, so the tests see what a real user's file goes through:
// saving is SerializeObject with type names (Simple assembly format, TypeNameHandling.Objects), and
// GetPluginConfig loads with a plain DeserializeObject<T> (default settings, so collections are merged by default).
internal static class DalamudJson
{
    private static readonly JsonSerializerSettings SaveSettings = new()
    {
        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
        TypeNameHandling = TypeNameHandling.Objects,
    };

    public static string Save(Configuration configuration)
    {
        return JsonConvert.SerializeObject(configuration, Formatting.Indented, SaveSettings);
    }

    public static Configuration Load(string json)
    {
        return JsonConvert.DeserializeObject<Configuration>(json)
            ?? throw new InvalidOperationException("Could not deserialize configuration.");
    }
}
