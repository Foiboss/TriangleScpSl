using System.Reflection;
using AdminToys;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Toys;
using Mirror;
using TriangleScpSl.Core.Paths;
using UnityEngine;

namespace TriangleScpSl;

public class Plugin : Plugin<Config>
{
    public static Plugin? Instance { get; private set; }

    public override string Author { get; } = "Foibos";
    public override string Name { get; } = "TriangleScpSl";
    public override Version Version { get; } = new(6, 0, 0);

    public override PluginPriority Priority { get; } = PluginPriority.Last;

    public override void OnEnabled()
    {
        Instance = this;
        TrianglePaths.EnsureModelsFolderExists();
        Exiled.Events.Handlers.Server.WaitingForPlayers += OnWaitingForPlayers;
        base.OnEnabled();
    }

    public override void OnDisabled()
    {
        Exiled.Events.Handlers.Server.WaitingForPlayers -= OnWaitingForPlayers;
        Instance = null;
        base.OnDisabled();
    }

    static void OnWaitingForPlayers()
    {
        if (Primitive.Prefab is not null)
            return;

        foreach (GameObject go in NetworkClient.prefabs.Values)
        {
            if (!go.TryGetComponent(out PrimitiveObjectToy toy))
                continue;

            typeof(Primitive)
                .GetProperty("Prefab", BindingFlags.Public | BindingFlags.Static)
                ?.SetValue(null, toy);

            Log.Info("[TriangleScpSl] Manually initialized Primitive.Prefab");
            return;
        }

        Log.Warn("[TriangleScpSl] Could not find PrimitiveObjectToy in NetworkClient.prefabs - Primitive.Create will fail!");
    }
}