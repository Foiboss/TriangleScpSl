using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using Triangle.Core.TriangulatedModel;
using UnityEngine;

namespace Triangle.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public class TriangulationCommand : ICommand
{
    TriangulatedModel? _model;
    readonly Color _forceColor = Color.cyan;
    
    public string Command { get; } = "Triangulate";
    public string[] Aliases { get; } = [];
    public string Description { get; } = "Displays a model Usage: <filename(.obj/.stl)> <true/false(force color)>";

    static bool ParseObjColorFlag(string raw, out bool forceObjColor)
    {
        if (bool.TryParse(raw, out forceObjColor))
            return true;

        forceObjColor = false;
        return false;
    }

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
        bool forceObjColor = false;

        if (arguments.Count == 2)
        {
            string rawFlag = arguments.Array?[arguments.Offset + 1] ?? string.Empty;

            if (!ParseObjColorFlag(rawFlag, out forceObjColor))
            {
                response = "Invalid OBJ color flag. Use: true/false";
                return false;
            }
        }

        Vector3 spawnPosition = player.Position + player.GameObject.transform.forward * 2.5f + Vector3.up * 1.2f;

        if (!ModelFactory.TryCreateModel(requestedFile, spawnPosition, _forceColor, forceObjColor, out TriangulatedModel? createdModel, out string fileName, out string error, PrimitiveFlags.Visible))
        {
            response = error;
            return false;
        }

        _model = createdModel;

        response = $"Created model '{fileName}': triangles={createdModel!.Count}, quads={createdModel.QuadCount}, forceObjColor={forceObjColor}. Run command again to destroy.";
        return true;
    }
}