using Exiled.API.Enums;
using Exiled.API.Features;
using TriangleScpSl.Core.Paths;
using TriangleScpSl.Core.Runtime;

namespace TriangleScpSl;

public class Plugin : Plugin<Config>
{
    public static Plugin? Instance { get; private set; }

    public override string Author { get; } = "Foibos";
    public override string Name { get; } = "TriangleScpSl";
    public override Version Version { get; } = new(3, 0, 0);

    public override PluginPriority Priority { get; } = PluginPriority.Last;

    public override void OnEnabled()
    {
        Instance = this;
        TrianglePaths.EnsureModelsFolderExists();
        base.OnEnabled();
    }

    public override void OnDisabled()
    {
        CoroutineHost.Shutdown();
        Instance = null;
        base.OnDisabled();
    }
}