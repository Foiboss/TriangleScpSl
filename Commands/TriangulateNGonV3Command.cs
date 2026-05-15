using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using System.Collections;
using TriangleScpSl.Core.Models.HierarchicalModel;
using TriangleScpSl.Core.NGons;
using TriangleScpSl.Core.Runtime;
using UnityEngine;

namespace TriangleScpSl.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public class TriangulateNGonV3Command : ICommand
{
    Coroutine? _buildCoroutine;
    bool _isBuilding;
    HierarchicalModel? _model;

    public string Command { get; } = "TriangulateNGonV3";
    public string[] Aliases { get; } = [];
    public string Description { get; } = "Displays an OBJ model using HierarchicalModel (V3). Usage: <filename(.obj)> [planar threshold] [accuracy]";

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
            response = "Model build canceled.";
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

        if (arguments.Count is < 1 or > 3)
        {
            response = "Usage: TriangulateNGonV3 <model file (.obj)> [planar threshold] [accuracy]";
            return false;
        }

        string requestedFile = arguments.Array?[arguments.Offset] ?? string.Empty;

        var config = NGonModelConfig.CreateFromSession();

        if (arguments.Count >= 2)
        {
            string rawPlanar = arguments.Array?[arguments.Offset + 1] ?? string.Empty;

            if (!float.TryParse(rawPlanar, out float planarThreshold))
            {
                response = "Invalid planar threshold value.";
                return false;
            }

            config.PlanarThreshold = planarThreshold;
        }

        if (arguments.Count == 3)
        {
            string rawAccuracy = arguments.Array?[arguments.Offset + 2] ?? string.Empty;

            if (!float.TryParse(rawAccuracy, out float accuracy))
            {
                response = "Invalid accuracy value.";
                return false;
            }

            config.Accuracy = accuracy;
        }

        _isBuilding = true;

        int batchSize = Mathf.Max(1, Plugin.Instance?.Config.TriangulateNGonV3BuildBatchSize ?? 64);
        Vector3 spawnPosition = player.Position + player.GameObject.transform.forward * 2.5f + Vector3.up * 1.2f;

        _buildCoroutine = CoroutineHost.Run(
            BuildRoutine(requestedFile, config, batchSize, spawnPosition));

        response = $"Started building V3 hierarchical model '{requestedFile}' asynchronously. Run command again to cancel.";
        return true;
    }

    IEnumerator BuildRoutine(string requestedFile, NGonModelConfig config, int batchSize, Vector3 spawnPosition)
    {
        NGonModelResult? loadResult = null;

        yield return NGonModelBuilder.LoadCoroutine(requestedFile, Color.white, result => { loadResult = result; }, config);

        if (loadResult == null)
        {
            _buildCoroutine = null;
            _isBuilding = false;
            Log.Warn("[TriangulateNGonV3] Failed to load model.");
            yield break;
        }

        var createdModel = HierarchicalModel.CreateDeferred(
            loadResult.Parallelograms,
            loadResult.DetectedPrimitives,
            spawnPosition,
            PrimitiveFlags.Visible,
            config.Accuracy);

        _model = createdModel;

        yield return createdModel.BuildTrianglesCoroutine(PrimitiveFlags.Visible, batchSize);

        _buildCoroutine = null;
        _isBuilding = false;

        if (!ReferenceEquals(_model, createdModel))
            yield break;

        if (createdModel is { ParallelogramCount: 0, NativePrimitiveCount: 0 })
        {
            createdModel.Destroy();
            _model = null;
            Log.Warn($"[TriangulateNGonV3] Model '{loadResult.NormalizedFileName}' has no valid triangles after async build.");
            yield break;
        }

        Log.Info($"[TriangulateNGonV3] Created model '{loadResult.NormalizedFileName}': ParallelogramCount={createdModel.ParallelogramCount}, NativePrimitiveCount={createdModel.NativePrimitiveCount}, PrimitiveCount={createdModel.PrimitiveCount}, Reparented={createdModel.ReparentedCount}, StretchesSaved={createdModel.StretchesSaved}.");
    }
}