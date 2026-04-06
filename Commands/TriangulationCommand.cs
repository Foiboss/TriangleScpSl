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
    public string Description { get; } = "Triangulates a .stl file into triangles and displays them (for example: \"triangulate blender_monkey.stl\")";

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
        string fileName = Path.GetFileName(requestedFile);

        if (!string.Equals(requestedFile, fileName))
        {
            response = "Only a file name is allowed (without directories).";
            return false;
        }

        if (!fileName.EndsWith(".stl", StringComparison.OrdinalIgnoreCase))
            fileName += ".stl";

        string stlFilePath = Path.Combine(Paths.Plugins, "StlModels", fileName);

        if (!File.Exists(stlFilePath))
        {
            response = $"STL file not found: {stlFilePath}";
            return false;
        }

        if (!StlParser.TryParseFile(stlFilePath, out List<StlTriangle> parsedTriangles, out string parseError))
        {
            response = $"Failed to parse STL: {parseError}";
            return false;
        }

        Vector3 spawnPosition = player.Position + player.GameObject.transform.forward * 2.5f + Vector3.up * 1.2f;
        Color color = Color.white;

        _model = TriangulatedModel.Create(parsedTriangles, spawnPosition, color, PrimitiveFlags.Visible);

        if (_model.Count == 0)
        {
            Clear();
            response = "No valid non-degenerate triangles found in STL.";
            return false;
        }

        response = $"Created model '{fileName}': triangles={_model.Count}, quads={_model.QuadCount}. Run command again to destroy.";
        return true;
    }
}