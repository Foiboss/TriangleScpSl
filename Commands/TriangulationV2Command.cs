using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using TriangleScpSl.ParallelogramSpace;
using UnityEngine;

namespace TriangleScpSl.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public class TriangulationV2Command : ICommand
{
    readonly Color _forceColor = Color.cyan;
    ParallelogramSpace.ParallelogramSpace? _model;

    public string Command { get; } = "TriangulateV2";
    public string[] Aliases { get; } = [];
    public string Description { get; } = "Displays a model Usage: <filename(.obj/.stl)> <true/false(force color)>";

    void Clear()
    {
        _model?.Destroy();
        _model = null;
    }

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (_model is not null)
        {
            Clear();
            response = "Destroyed";
            return true;
        }

        Player? player = Player.Get(sender);

        if (player is null)
        {
            response = "This command can only be used by a player.";
            return false;
        }

        if (arguments.Count is < 1 or > 2)
        {
            response = "Usage: triangulate <model file (.stl/.obj)> <clusterization accuracy>";
            return false;
        }

        string requestedFile = arguments.Array?[arguments.Offset] ?? string.Empty;
        const bool forceObjColor = false;
        float accuracy = 0.001f;

        if (arguments.Count == 2)
        {
            string rawAccuracy = arguments.Array?[arguments.Offset + 1] ?? string.Empty;

            if (!float.TryParse(rawAccuracy, out accuracy))
            {
                response = "Invalid accuracy value";
                return false;
            }
        }

        Vector3 spawnPosition = player.Position + player.GameObject.transform.forward * 2.5f + Vector3.up * 1.2f;

        if (!ModelFactoryV2.TryCreateModel
            (
                requestedFile,
                spawnPosition,
                _forceColor,
                forceObjColor,
                out ParallelogramSpace.ParallelogramSpace? createdModel,
                out string fileName,
                out string error,
                PrimitiveFlags.Visible,
                accuracy
            )
        )
        {
            response = error;
            return false;
        }

        _model = createdModel;

        response = $"Created model '{fileName}': triangles={createdModel!.Count}, quads={createdModel.QuadCount}, forceObjColor={forceObjColor}. Run command again to destroy.";
        return true;
    }
}