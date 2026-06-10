using System.Collections;
using System.Globalization;
using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using TriangleScpSl.Core.Decomposition.TriangleDecomposition.ModelFactory;
using TriangleScpSl.Core.Models.HierarchicalModel;
using TriangleScpSl.Core.Paths;
using TriangleScpSl.Core.Primitives.Triangle;
using TriangleScpSl.Core.ProjectMerExport;
using TriangleScpSl.Core.Runtime;
using UnityEngine;

namespace TriangleScpSl.Commands.ExportCommands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public sealed class ExportSchematicV3Command : ICommand
{
    readonly Color _fallbackColor = Color.white;
    Coroutine? _exportCoroutine;
    bool _isExporting;
    HierarchicalModel? _activeModel;

    public string Command { get; } = "ExportSchematicV3";
    public string[] Aliases { get; } = [];
    public string Description { get; } = "Exports .obj as ProjectMER schematic JSON using V3 hierarchical model. Usage: <model file> <output file> [accuracy(0.001)]";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (_isExporting)
        {
            CancelCurrentExport();
            response = "Export cancelled.";
            return true;
        }

        if (arguments.Count is < 2 or > 3)
        {
            response = "Usage: ExportSchematicV3 <model file (.obj)> <output JSON file> [accuracy(0.001)]";
            return false;
        }

        string requestedFile = arguments.Array?[arguments.Offset] ?? string.Empty;
        string outputFileArg = arguments.Array?[arguments.Offset + 1] ?? string.Empty;

        if (!TryNormalizeOutputName(outputFileArg, out string outputFileName))
        {
            response = "Output must be file name only (without directories).";
            return false;
        }

        var accuracy = 0.001f;

        if (arguments.Count == 3)
        {
            string rawAccuracy = arguments.Array?[arguments.Offset + 2] ?? string.Empty;

            if (!float.TryParse(rawAccuracy, out accuracy))
            {
                response = "Invalid accuracy value";
                return false;
            }
        }

        int buildBatch = Mathf.Max(1, Plugin.Instance?.Config.ExportBuildBatchSize ?? 64);
        int writeBatch = Mathf.Max(1, Plugin.Instance?.Config.ExportWriteBatchSize ?? 256);

        _isExporting = true;
        _exportCoroutine = CoroutineHost.Run(ExportRoutine(requestedFile, outputFileName, accuracy, buildBatch, writeBatch));

        response = "Export started asynchronously. Run command again to cancel current export.";
        return true;
    }

    IEnumerator ExportRoutine
    (
        string requestedFile,
        string outputFileName,
        float accuracy,
        int buildBatch,
        int writeBatch)
    {
        try
        {
            if (!ModelFactory.TryLoadTrianglesRaw(requestedFile, _fallbackColor, false, out List<ModelTriangle> triangles, out _, out string modelError))
            {
                Log.Warn($"[ExportSchematicV3] {modelError}");
                yield break;
            }

            _activeModel = new HierarchicalModel(
                triangles,
                Vector3.zero,
                PrimitiveFlags.Visible,
                accuracy);

            yield return _activeModel.BuildTrianglesCoroutine(PrimitiveFlags.Visible, buildBatch);

            if (_activeModel.ParallelogramCount == 0)
            {
                Log.Warn("[ExportSchematicV3] Model has no valid non-degenerate triangles.");
                yield break;
            }

            _activeModel.Scale = Vector3.one;

            TrianglePaths.EnsureSchematicDirectoryExists(outputFileName);
            string outputPath = TrianglePaths.GetSchematicOutputPath(outputFileName);
            string schematicName = TrianglePaths.GetSchematicFolderName(outputFileName);

            var completed = false;
            var exportSucceeded = false;
            var exportError = string.Empty;

            yield return ProjectMerSchematicExporter.ExportCoroutine(
                _activeModel,
                outputPath,
                schematicName,
                writeBatch,
                (success, error) =>
                {
                    exportSucceeded = success;
                    exportError = error;
                    completed = true;
                });

            if (!completed || !exportSucceeded)
            {
                Log.Warn($"[ExportSchematicV3] Failed to export schematic: {exportError}");
                yield break;
            }

            Log.Info($"[ExportSchematicV3] Exported: {outputPath} (ParallelogramCount={_activeModel.ParallelogramCount}, PrimitiveCount={_activeModel.PrimitiveCount}, Reparented={_activeModel.ReparentedCount}, StretchesSaved={_activeModel.StretchesSaved}, accuracy={accuracy.ToString(CultureInfo.InvariantCulture)}).");
        }
        finally
        {
            _activeModel?.Destroy();
            _activeModel = null;
            _exportCoroutine = null;
            _isExporting = false;
        }
    }

    void CancelCurrentExport()
    {
        if (_exportCoroutine is not null)
            CoroutineHost.Stop(_exportCoroutine);

        _exportCoroutine = null;
        _isExporting = false;

        _activeModel?.Destroy();
        _activeModel = null;
    }

    static bool TryNormalizeOutputName(string raw, out string fileName)
    {
        fileName = Path.GetFileName(raw);

        if (!string.Equals(fileName, raw, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(fileName))
            return false;

        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            fileName += ".json";

        return true;
    }
}