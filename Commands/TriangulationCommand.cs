using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using Triangle.Core.Triangulation.Stl;
using Triangle.Core.Triangulation.Triangle;
using UnityEngine;

namespace Triangle.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public class TriangulationCommand : ICommand
{
    TriangulatedModel? _model;

    public string Command { get; } = "Triangulate";
    public string[] Aliases { get; } = [];
    public string Description { get; } = "Triangulates .stl file and displays them (\"triangulate blender_monkey.stl\")";

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

        if (arguments.Count != 1)
        {
            response = "Usage: triangulate <stl file>";
            return false;
        }

        string requestedFile = arguments.Array?[arguments.Offset] ?? string.Empty;

        Vector3 spawnPosition = player.Position + player.GameObject.transform.forward * 2.5f + Vector3.up * 1.2f;
        Color color = Color.white;

        if (!StlModelFactory.TryCreateModel(requestedFile, spawnPosition, color, out TriangulatedModel? createdModel, out string fileName, out string error, PrimitiveFlags.Visible))
        {
            response = error;
            return false;
        }

        _model = createdModel;

        response = $"Created model '{fileName}': triangles={createdModel!.Count}, quads={createdModel.QuadCount}. Run command again to destroy.";
        return true;
    }
}