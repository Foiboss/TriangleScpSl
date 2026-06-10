using System.Collections;
using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using TriangleScpSl.Core.Decomposition.NGonDecomposition;
using TriangleScpSl.Core.Models.ExactModel;
using TriangleScpSl.Core.Runtime;
using UnityEngine;

namespace TriangleScpSl.Commands.TriangulateCommands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public class TriangulateNGonCommand : ICommand
{
    Coroutine? _buildCoroutine;
    bool _isBuilding;
    ExactModel? _model;

    public string Command { get; } = "TriangulateNGon";
    public string[] Aliases { get; } = [];
    public string Description { get; } = "Displays an OBJ model using ExactModel. Usage: <filename(.obj)>";

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

        if (arguments.Count != 1)
        {
            response = "Usage: TriangulateNGon <model file (.obj)>";
            return false;
        }

        string requestedFile = arguments.Array?[arguments.Offset] ?? string.Empty;

        var config = NGonModelConfig.CreateFromSession();

        _isBuilding = true;

        int batchSize = Mathf.Max(1, Plugin.Instance?.Config.TriangulateNGonBuildBatchSize ?? 32);
        Vector3 spawnPosition = player.Position + player.GameObject.transform.forward * 2.5f + Vector3.up * 1.2f;

        _buildCoroutine = CoroutineHost.Run(
            BuildRoutine(requestedFile, config, batchSize, spawnPosition));

        response = $"Started building OBJ model '{requestedFile}' asynchronously. Run command again to cancel.";
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
            Log.Warn("[TriangulateNGon] Failed to load model.");
            yield break;
        }

        var createdModel = new ExactModel(loadResult.Parallelograms, loadResult.DetectedPrimitives, spawnPosition, PrimitiveFlags.Visible, 1f);
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
            Log.Warn($"[TriangulateNGon] Model '{loadResult.NormalizedFileName}' has no valid geometry after async build.");
            yield break;
        }

        Log.Info($"[TriangulateNGon] Created model '{loadResult.NormalizedFileName}': ParallelogramCount={createdModel.ParallelogramCount}, NativePrimitiveCount={createdModel.NativePrimitiveCount}, PrimitiveCount={createdModel.PrimitiveCount}.");
    }
}