using Exiled.API.Enums;
using Exiled.API.Features;

namespace Triangle;

public class Plugin : Plugin<Config>
{
    public override string Author { get; } = "Foibos";
    public override string Name { get; } = "Triangle";
    public override Version Version { get; } = new(2, 0, 0);

    public override PluginPriority Priority { get; } = PluginPriority.Last;
}