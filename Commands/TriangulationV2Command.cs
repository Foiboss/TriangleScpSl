using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using System.Collections;
using TriangleScpSl.Core.ModelFactory;
using TriangleScpSl.Core.ParallelogramSpace;
using TriangleScpSl.Core.Runtime;
using TriangleScpSl.Core.TriangulatedModel;
using UnityEngine;

namespace TriangleScpSl.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public class TriangulationV2Command : ICommand
{
    Coroutine? _buildCoroutine;
    bool _isBuilding;
    ParallelogramSpace? _model;

    public string Command { get; } = "TriangulateV2";
    public string[] Aliases { get; } = [];
    public string Description { get; } = "Displays a model Usage: <filename(.obj/.stl)> <clusterization accuracy(0.001)>";

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
            response = "Usage: triangulate <model file (.stl/.obj)> <clusterization accuracy (0.001)>";
            return false;
        }

        string requestedFile = arguments.Array?[arguments.Offset] ?? string.Empty;
        var accuracy = 0.001f;

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

        if (!ModelFactory.TryLoadTrianglesRaw(requestedFile, Color.white, false, out List<ModelTriangle> triangles, out string fileName, out string error))
        {
            response = error;
            return false;
        }

        var createdModel = ParallelogramSpace.CreateDeferred(
            triangles,
            spawnPosition,
            PrimitiveFlags.Visible,
            accuracy);

        _model = createdModel;
        _isBuilding = true;

        int batchSize = Mathf.Max(1, Plugin.Instance?.Config.TriangulateV2BuildBatchSize ?? 16);
        _buildCoroutine = CoroutineHost.Run(BuildRoutine(createdModel, fileName, batchSize));

        response = $"Started building model '{fileName}' asynchronously. Run command again to cancel while building.";
        return true;
    }

    IEnumerator BuildRoutine(ParallelogramSpace model, string fileName, int batchSize)
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
            Log.Warn($"[TriangulateV2] Model '{fileName}' has no valid triangles after async build.");
            yield break;
        }

        Log.Info($"[TriangulateV2] Created model '{fileName}': triangles={model.Count}, quads={model.QuadCount}.");
    }
}