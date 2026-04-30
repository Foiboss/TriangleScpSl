using System.Collections;
using System.Globalization;
using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using TriangleScpSl.Core.ModelFactory;
using TriangleScpSl.Core.Models.ExactModel;
using TriangleScpSl.Core.NGons;
using TriangleScpSl.Core.Paths;
using TriangleScpSl.Core.ProjectMerExport;
using TriangleScpSl.Core.Runtime;
using UnityEngine;

namespace TriangleScpSl.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public sealed class ExportSchematicNGonCommand : ICommand
{
    Coroutine? _exportCoroutine;
    bool _isExporting;
    ExactModel? _activeModel;

    public string Command { get; } = "ExportSchematicNGon";
    public string[] Aliases { get; } = [];
    public string Description { get; } = "Exports an OBJ as ProjectMER schematic JSON using ExactModel. Usage: <model file (.obj)> <output JSON file> [previewScale]";

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
            response = "Usage: ExportSchematicNGon <model file (.obj)> <output JSON file> [previewScale]";
            return false;
        }

        string requestedFile = arguments.Array?[arguments.Offset] ?? string.Empty;
        string outputFileArg = arguments.Array?[arguments.Offset + 1] ?? string.Empty;

        if (!TryNormalizeOutputName(outputFileArg, out string outputFileName))
        {
            response = "Output must be file name only (without directories).";
            return false;
        }

        var previewScale = 1f;

        if (arguments.Count >= 3)
        {
            string rawScale = arguments.Array?[arguments.Offset + 2] ?? string.Empty;

            if (!float.TryParse(rawScale, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out previewScale) || previewScale <= 0f)
            {
                response = "Invalid previewScale. Use a positive number (example: 1 or 0.5).";
                return false;
            }
        }

        int buildBatch = Mathf.Max(1, Plugin.Instance?.Config.ExportBuildBatchSize ?? 64);
        int writeBatch = Mathf.Max(1, Plugin.Instance?.Config.ExportWriteBatchSize ?? 256);

        _isExporting = true;
        _exportCoroutine = CoroutineHost.Run(ExportRoutine(requestedFile, outputFileName, previewScale, buildBatch, writeBatch));

        response = "Export started asynchronously. Run command again to cancel.";
        return true;
    }

    IEnumerator ExportRoutine
    (
        string requestedFile,
        string outputFileName,
        float previewScale,
        int buildBatch,
        int writeBatch)
    {
        try
        {
            if (!NGonModelBuilder.TryLoad(requestedFile, Color.white, out List<ModelParallelogram> parallelograms, out _, out string modelError))
            {
                Log.Warn($"[ExportSchematicNGon] {modelError}");
                yield break;
            }

            _activeModel = ExactModel.CreateDeferred(parallelograms, Vector3.zero, PrimitiveFlags.Visible, 1f, true);
            yield return _activeModel.BuildTrianglesCoroutine(PrimitiveFlags.Visible, buildBatch);

            if (_activeModel.Count == 0)
            {
                Log.Warn("[ExportSchematicNGon] Model has no valid non-degenerate triangles.");
                yield break;
            }

            _activeModel.Scale = Vector3.one * previewScale;

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
                (success, err) =>
                {
                    exportSucceeded = success;
                    exportError = err;
                    completed = true;
                });

            if (!completed || !exportSucceeded)
            {
                Log.Warn($"[ExportSchematicNGon] Failed to export schematic: {exportError}");
                yield break;
            }

            Log.Info($"[ExportSchematicNGon] Exported: {outputPath} (triangles={_activeModel.Count}, quads={_activeModel.QuadCount}, previewScale={previewScale.ToString(CultureInfo.InvariantCulture)}).");
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