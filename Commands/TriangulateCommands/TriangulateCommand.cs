using System.Collections;
using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using TriangleScpSl.Core.Decomposition.TriangleDecomposition.ModelFactory;
using TriangleScpSl.Core.Models.ExactModel;
using TriangleScpSl.Core.Primitives.Triangle;
using TriangleScpSl.Core.Runtime;
using UnityEngine;

namespace TriangleScpSl.Commands.TriangulateCommands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public class TriangulateCommand : ICommand
{
    readonly Color _fallbackColor = Color.white;
    Coroutine? _buildCoroutine;
    bool _isBuilding;
    ExactModel? _model;

    public string Command { get; } = "Triangulate";
    public string[] Aliases { get; } = [];
    public string Description { get; } = "Displays a model Usage: <filename(.obj)>";

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

        if (arguments.Count < 1)
        {
            response = "Usage: triangulate <model file (.obj)>";
            return false;
        }

        string requestedFile = arguments.Array?[arguments.Offset] ?? string.Empty;

        Vector3 spawnPosition = player.Position + player.GameObject.transform.forward * 2.5f + Vector3.up * 1.2f;

        if (!ModelFactory.TryLoadTrianglesRaw(requestedFile, _fallbackColor, false, out List<ModelTriangle> triangles, out string fileName, out string error))
        {
            response = error;
            return false;
        }

        var createdModel = new ExactModel(triangles, spawnPosition, PrimitiveFlags.Visible);
        _model = createdModel;
        _isBuilding = true;

        int batchSize = Mathf.Max(1, Plugin.Instance?.Config.TriangulateBuildBatchSize ?? 32);
        _buildCoroutine = CoroutineHost.Run(BuildRoutine(createdModel, fileName, batchSize));

        response = $"Started building model '{fileName}' asynchronously. Run command again to cancel while building.";
        return true;
    }

    IEnumerator BuildRoutine(ExactModel model, string fileName, int batchSize)
    {
        yield return model.BuildTrianglesCoroutine(PrimitiveFlags.Visible, batchSize);

        _buildCoroutine = null;
        _isBuilding = false;

        if (!ReferenceEquals(_model, model))
            yield break;

        if (model.ParallelogramCount == 0)
        {
            model.Destroy();
            _model = null;
            Log.Warn($"[Triangulate] Model '{fileName}' has no valid triangles after async build.");
            yield break;
        }

        Log.Info($"[Triangulate] Created model '{fileName}': ParallelogramCount={model.ParallelogramCount}, PrimitiveCount={model.PrimitiveCount}.");
    }
}