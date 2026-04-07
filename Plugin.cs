using Exiled.API.Enums;
using Exiled.API.Features;
using TriangleScpSl.Core.Paths;

namespace TriangleScpSl;

public class Plugin : Plugin<Config>
{
    public override string Author { get; } = "Foibos";
    public override string Name { get; } = "TriangleScpSl";
    public override Version Version { get; } = new(2, 2, 0);

    public override PluginPriority Priority { get; } = PluginPriority.Last;

    public override void OnEnabled()
    {
        TrianglePaths.EnsureModelsFolderExists();
        base.OnEnabled();
    }
}