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
            response = "Usage: triangulate <model file (.stl/.obj)> <force color (true/false)>";
            return false;
        }

        string requestedFile = arguments.Array?[arguments.Offset] ?? string.Empty;
        var forceObjColor = false;

        if (arguments.Count == 2)
        {
            string rawFlag = arguments.Array?[arguments.Offset + 1] ?? string.Empty;

            if (!bool.TryParse(rawFlag, out forceObjColor))
            {
                response = "Invalid OBJ color flag. Use: true/false";
                return false;
            }
        }

        Vector3 spawnPosition = player.Position + player.GameObject.transform.forward * 2.5f + Vector3.up * 1.2f;

        if (!ModelFactoryV2.TryCreateModel(requestedFile, spawnPosition, _forceColor, forceObjColor, out ParallelogramSpace.ParallelogramSpace? createdModel, out string fileName, out string error, PrimitiveFlags.Visible))
        {
            response = error;
            return false;
        }

        _model = createdModel;

        response = $"Created model '{fileName}': triangles={createdModel!.Count}, quads={createdModel.QuadCount}, forceObjColor={forceObjColor}. Run command again to destroy.";
        return true;
    }
}