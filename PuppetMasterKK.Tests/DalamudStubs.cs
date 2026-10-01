using Dalamud.Configuration;

namespace Dalamud.Configuration
{
    public interface IPluginConfiguration
    {
        int Version { get; set; }
    }
}

namespace Dalamud.Plugin
{
    public interface IDalamudPluginInterface
    {
        void SavePluginConfig(IPluginConfiguration configuration);
    }
}

namespace phys1ksUI
{
    // The plugin compiles phys1ksUI's AccentColor in from the kit; the tests only need the type (saved as a number).
    public enum AccentColor
    {
        Orange,
        Purple,
        Indigo,
        Blue,
        Teal,
        Green,
        Pink,
        Graphite,
        Gray,
        Silver,
        White,
    }
}
