using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using System.Collections;
using TriangleScpSl.Core.ModelFactory;
using TriangleScpSl.Core.Models.ExactModel;
using TriangleScpSl.Core.NGons;
using TriangleScpSl.Core.Runtime;
using UnityEngine;

namespace TriangleScpSl.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public class TriangulateNgonCommand : ICommand
{
    Coroutine? _buildCoroutine;
    bool _isBuilding;
    ExactModel? _model;

    public string Command { get; } = "TriangulateNgon";
    public string[] Aliases { get; } = [];
    public string Description { get; } = "Displays an OBJ/FBX model using ExactModel. Usage: <filename(.obj|.fbx)>";

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

        if (arguments.Count != 1)
        {
            response = "Usage: TriangulateNgon <model file (.obj|.fbx)>";
            return false;
        }

        string requestedFile = arguments.Array?[arguments.Offset] ?? string.Empty;

        if (!NgonModelBuilder.TryLoad(requestedFile, Color.white, out List<ModelTriangle> triangles, out string fileName, out string error))
        {
            response = error;
            return false;
        }

        Vector3 spawnPosition = player.Position + player.GameObject.transform.forward * 2.5f + Vector3.up * 1.2f;
        var createdModel = ExactModel.CreateDeferred(triangles, spawnPosition, PrimitiveFlags.Visible, 1f, true);
        _model = createdModel;
        _isBuilding = true;

        int batchSize = Mathf.Max(1, Plugin.Instance?.Config.TriangulateNgonBuildBatchSize ?? 32);
        _buildCoroutine = CoroutineHost.Run(BuildRoutine(createdModel, fileName, batchSize));

        response = $"Started building OBJ/FBX model '{fileName}' asynchronously. Run command again to cancel.";
        return true;
    }

    IEnumerator BuildRoutine(ExactModel model, string fileName, int batchSize)
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
            Log.Warn($"[TriangulateNgon] Model '{fileName}' has no valid triangles after async build.");
            yield break;
        }

        Log.Info($"[TriangulateNgon] Created model '{fileName}': triangles={model.Count}, quads={model.QuadCount}.");
    }
}
