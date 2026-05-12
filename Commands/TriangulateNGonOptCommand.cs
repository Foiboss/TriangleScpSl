using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using System.Collections;
using TriangleScpSl.Core.Models.ApproximateModel;
using TriangleScpSl.Core.NGons;
using TriangleScpSl.Core.Runtime;
using TriangleScpSl.Core.Triangulation.Parallelogram;
using UnityEngine;

namespace TriangleScpSl.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public class TriangulateNGonOptCommand : ICommand
{
    Coroutine? _buildCoroutine;
    bool _isBuilding;
    ApproximateModel? _model;

    public string Command { get; } = "TriangulateNGonOpt";
    public string[] Aliases { get; } = [];
    public string Description { get; } = "Displays an OBJ model with all optimizations. Usage: <filename(.obj)> [accuracy] [smoothness]";

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

        if (arguments.Count < 1 || arguments.Count > 3)
        {
            response = "Usage: TriangulateNGonOpt <model file (.obj)> [accuracy] [smoothness]";
            return false;
        }

        var config = NGonModelConfig.CreateFromSession();
        config.UseHiddenTailOptimization = true;
        config.DetectPrimitives = true;

        if (arguments.Count >= 2)
        {
            string rawAccuracy = arguments.Array?[arguments.Offset + 1] ?? string.Empty;

            if (!float.TryParse(rawAccuracy, out float accuracy))
            {
                response = "Invalid accuracy value.";
                return false;
            }

            config.Accuracy = accuracy;
        }

        if (arguments.Count >= 3)
        {
            string rawSmoothness = arguments.Array?[arguments.Offset + 2] ?? string.Empty;

            if (!float.TryParse(rawSmoothness, out float smoothness) || smoothness <= 0f)
            {
                response = "Invalid smoothness value. Use a positive number in radians.";
                return false;
            }

            config.SmoothMaxAngle = smoothness;
        }

        string requestedFile = arguments.Array?[arguments.Offset] ?? string.Empty;

        _isBuilding = true;

        int batchSize = Mathf.Max(1, Plugin.Instance?.Config.TriangulateNGonOptBuildBatchSize ?? 128);
        Vector3 spawnPosition = player.Position + player.GameObject.transform.forward * 2.5f + Vector3.up * 1.2f;

        _buildCoroutine = CoroutineHost.Run(
            BuildRoutine(requestedFile, config, batchSize, spawnPosition));

        response = $"Building '{requestedFile}' asynchronously (accuracy={config.Accuracy}, smoothness={config.SmoothMaxAngle:F2}rad). Run command again to cancel.";
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
            Log.Warn("[TriangulateNGonOpt] Failed to load model.");
            yield break;
        }

        var rectCount = 0;
        var nonRectCount = 0;

        foreach (ModelParallelogram p in loadResult.Parallelograms)
        {
            if (p.IsRectangle) rectCount++;
            else nonRectCount++;
        }

        var createdModel = ApproximateModel.CreateDeferred(
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
            Log.Warn($"[TriangulateNGonOpt] Model '{loadResult.NormalizedFileName}' has no valid geometry after async build.");
            yield break;
        }

        Log.Info($"[TriangulateNGonOpt] Created model '{loadResult.NormalizedFileName}': " +
            $"ParallelogramCount={loadResult.Parallelograms.Count} ({rectCount} rect, {nonRectCount} normal), " +
            $"NativePrimitiveCount={loadResult.DetectedPrimitives.Count}, PrimitiveCount={createdModel.PrimitiveCount}.");
    }
}