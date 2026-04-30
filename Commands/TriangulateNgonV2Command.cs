using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using System.Collections;
using TriangleScpSl.Core.ModelFactory;
using TriangleScpSl.Core.Models.ApproximateModel;
using TriangleScpSl.Core.NGons;
using TriangleScpSl.Core.Runtime;
using UnityEngine;

namespace TriangleScpSl.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public class TriangulateNgonV2Command : ICommand
{
    Coroutine? _buildCoroutine;
    bool _isBuilding;
    ApproximateModel? _model;

    public string Command { get; } = "TriangulateNgonV2";
    public string[] Aliases { get; } = [];
    public string Description { get; } = "Displays an OBJ/FBX model using ApproximateModel. Usage: <filename(.obj|.fbx)> [accuracy(0.001)]";

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
            response = "Usage: TriangulateNgonV2 <model file (.obj|.fbx)> [accuracy(0.001)]";
            return false;
        }

        string requestedFile = arguments.Array?[arguments.Offset] ?? string.Empty;
        var accuracy = 0.001f;

        if (arguments.Count == 2)
        {
            string rawAccuracy = arguments.Array?[arguments.Offset + 1] ?? string.Empty;

            if (!float.TryParse(rawAccuracy, out accuracy))
            {
                response = "Invalid accuracy value.";
                return false;
            }
        }

        if (!NgonModelBuilder.TryLoad(requestedFile, Color.white, out List<ModelTriangle> triangles, out string fileName, out string error))
        {
            response = error;
            return false;
        }

        Vector3 spawnPosition = player.Position + player.GameObject.transform.forward * 2.5f + Vector3.up * 1.2f;

        var createdModel = ApproximateModel.CreateDeferred(
            triangles,
            spawnPosition,
            PrimitiveFlags.Visible,
            accuracy);

        _model = createdModel;
        _isBuilding = true;

        int batchSize = Mathf.Max(1, Plugin.Instance?.Config.TriangulateNgonV2BuildBatchSize ?? 16);
        _buildCoroutine = CoroutineHost.Run(BuildRoutine(createdModel, fileName, batchSize));

        response = $"Started building OBJ/FBX model '{fileName}' asynchronously. Run command again to cancel.";
        return true;
    }

    IEnumerator BuildRoutine(ApproximateModel model, string fileName, int batchSize)
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
            Log.Warn($"[TriangulateNgonV2] Model '{fileName}' has no valid triangles after async build.");
            yield break;
        }

        Log.Info($"[TriangulateNgonV2] Created model '{fileName}': triangles={model.Count}, quads={model.QuadCount}.");
    }
}
