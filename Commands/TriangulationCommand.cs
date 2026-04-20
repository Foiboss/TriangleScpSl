using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using System.Collections;
using TriangleScpSl.Core.ModelFactory;
using TriangleScpSl.Core.Runtime;
using TriangleScpSl.Core.TriangulatedModel;
using UnityEngine;

namespace TriangleScpSl.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public class TriangulationCommand : ICommand
{
    readonly Color _forceColor = Color.white;
    Coroutine? _buildCoroutine;
    bool _isBuilding;
    TriangulatedModel? _model;

    public string Command { get; } = "Triangulate";
    public string[] Aliases { get; } = [];
    public string Description { get; } = "Displays a model Usage: <filename(.obj/.stl)> <true/false(force color)>";

    void Clear()
    {
        if (_buildCoroutine is not null)
            CoroutineHost.Stop(_buildCoroutine);

        _buildCoroutine = null;
        _isBuilding = false;
        _model?.Destroy();
        _model = null;
    }

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (_isBuilding)
        {
            Clear();
            response = "Model build cancelled.";
            return true;
        }

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

        if (!ModelFactory.TryLoadTriangles(requestedFile, _forceColor, forceObjColor, out List<ModelTriangle> triangles, out string fileName, out string error))
        {
            response = error;
            return false;
        }

        var createdModel = TriangulatedModel.CreateDeferred(triangles, spawnPosition, PrimitiveFlags.Visible);
        _model = createdModel;
        _isBuilding = true;

        int batchSize = Mathf.Max(1, Plugin.Instance?.Config.TriangulateBuildBatchSize ?? 32);
        _buildCoroutine = CoroutineHost.Run(BuildRoutine(createdModel, fileName, forceObjColor, batchSize));

        response = $"Started building model '{fileName}' asynchronously. Run command again to cancel while building.";
        return true;
    }

    IEnumerator BuildRoutine(TriangulatedModel model, string fileName, bool forceObjColor, int batchSize)
    {
        yield return model.BuildTrianglesCoroutine(PrimitiveFlags.Visible, batchSize);

        _buildCoroutine = null;
        _isBuilding = false;

        if (!ReferenceEquals(_model, model))
            yield break;

        if (model.Count == 0)
        {
            model.Destroy();
            _model = null;
            Log.Warn($"[Triangulate] Model '{fileName}' has no valid triangles after async build.");
            yield break;
        }

        Log.Info($"[Triangulate] Created model '{fileName}': triangles={model.Count}, quads={model.QuadCount}, forceObjColor={forceObjColor}.");
    }
}