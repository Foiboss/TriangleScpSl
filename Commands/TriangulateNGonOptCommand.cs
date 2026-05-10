using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using System.Collections;
using TriangleScpSl.Core.Models.ApproximateModel;
using TriangleScpSl.Core.NGons;
using TriangleScpSl.Core.NGons.Detectors;
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
    public string Description { get; } = "Displays an OBJ model with all optimizations for minimal primitive count. Usage: <filename(.obj)> [accuracy(0.001)] [smoothness(0.32)]";

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
            response = "Usage: TriangulateNGonOpt <model file (.obj)> [accuracy(0.001)] [smoothness(0.32)]";
            return false;
        }

        var accuracy = 0.001f;
        float smoothness = SmoothnessCheck.DefaultMaxAngle;

        if (arguments.Count >= 2)
        {
            string rawAccuracy = arguments.Array?[arguments.Offset + 1] ?? string.Empty;

            if (!float.TryParse(rawAccuracy, out accuracy))
            {
                response = "Invalid accuracy value.";
                return false;
            }
        }

        if (arguments.Count >= 3)
        {
            string rawSmoothness = arguments.Array?[arguments.Offset + 2] ?? string.Empty;

            if (!float.TryParse(rawSmoothness, out smoothness) || smoothness <= 0f)
            {
                response = "Invalid smoothness value. Use a positive number in radians (default: 0.32 ≈ 18°).";
                return false;
            }
        }

        string requestedFile = arguments.Array?[arguments.Offset] ?? string.Empty;

        if (!NGonModelBuilder.TryLoad(
            requestedFile,
            Color.white,
            out List<ModelParallelogram> parallelograms,
            out List<ModelPrimitive> detectedPrimitives,
            out string fileName,
            out string error,
            0f,
            true,
            true,
            1e-4f,
            1e-4f,
            smoothness))
        {
            response = error;
            return false;
        }

        var rectCount = 0;
        var nonRectCount = 0;

        foreach (ModelParallelogram p in parallelograms)
        {
            if (p.IsRectangle) rectCount++;
            else nonRectCount++;
        }

        Vector3 spawnPosition = player.Position + player.GameObject.transform.forward * 2.5f + Vector3.up * 1.2f;

        var createdModel = ApproximateModel.CreateDeferred(
            parallelograms,
            detectedPrimitives,
            spawnPosition,
            PrimitiveFlags.Visible,
            accuracy);

        _model = createdModel;
        _isBuilding = true;

        int batchSize = Mathf.Max(1, Plugin.Instance?.Config.TriangulateNGonOptBuildBatchSize ?? 128);

        _buildCoroutine = CoroutineHost.Run(BuildRoutine(createdModel, fileName, batchSize,
            parallelograms.Count, detectedPrimitives.Count, rectCount, nonRectCount));

        response = $"Building '{fileName}': {parallelograms.Count} parallelograms ({rectCount} rectangles, {nonRectCount} normal parallelograms), " +
            $"{detectedPrimitives.Count} native primitives, accuracy={accuracy}, smoothness={smoothness:F2}rad.";
        return true;
    }

    IEnumerator BuildRoutine
    (ApproximateModel model, string fileName, int batchSize,
        int paraCount, int nativeCount, int rectCount, int nonRectCount)
    {
        yield return model.BuildTrianglesCoroutine(PrimitiveFlags.Visible, batchSize);

        _buildCoroutine = null;
        _isBuilding = false;

        if (!ReferenceEquals(_model, model))
            yield break;

        if (model is { ParallelogramCount: 0, NativePrimitiveCount: 0 })
        {
            model.Destroy();
            _model = null;
            Log.Warn($"[TriangulateNGonOpt] Model '{fileName}' has no valid geometry after async build.");
            yield break;
        }

        Log.Info($"[TriangulateNGonOpt] Created model '{fileName}': ParallelogramCount={paraCount} ({rectCount} rect, {nonRectCount} normal parallelograms), NativePrimitiveCount={nativeCount}, PrimitiveCount={model.PrimitiveCount}.");
    }
}