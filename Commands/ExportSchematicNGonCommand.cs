using System.Collections;
using System.Globalization;
using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using TriangleScpSl.Core.Models.ExactModel;
using TriangleScpSl.Core.NGons;
using TriangleScpSl.Core.Paths;
using TriangleScpSl.Core.ProjectMerExport;
using TriangleScpSl.Core.Runtime;
using TriangleScpSl.Core.Triangulation.Parallelogram;
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
    public string Description { get; } = "Exports an OBJ as ProjectMER schematic JSON using ExactModel. Usage: <model file (.obj)> <output JSON file> [planar threshold(0)]";

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
            response = "Usage: ExportSchematicNGon <model file (.obj)> <output JSON file> [planar threshold(0)]";
            return false;
        }

        string requestedFile = arguments.Array?[arguments.Offset] ?? string.Empty;
        string outputFileArg = arguments.Array?[arguments.Offset + 1] ?? string.Empty;

        if (!TryNormalizeOutputName(outputFileArg, out string outputFileName))
        {
            response = "Output must be file name only (without directories).";
            return false;
        }

        var planarThreshold = 0f;

        if (arguments.Count >= 3)
        {
            string rawPlanarThreshold = arguments.Array?[arguments.Offset + 2] ?? string.Empty;

            if (!float.TryParse(rawPlanarThreshold, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out planarThreshold) || planarThreshold < 0f)
            {
                response = "Invalid planarThreshold. Use a nonnegative number (example: 0.001 or 0).";
                return false;
            }
        }

        int buildBatch = Mathf.Max(1, Plugin.Instance?.Config.ExportBuildBatchSize ?? 64);
        int writeBatch = Mathf.Max(1, Plugin.Instance?.Config.ExportWriteBatchSize ?? 256);

        _isExporting = true;
        _exportCoroutine = CoroutineHost.Run(ExportRoutine(requestedFile, outputFileName, planarThreshold, buildBatch, writeBatch));

        response = "Export started asynchronously. Run command again to cancel.";
        return true;
    }

    IEnumerator ExportRoutine
    (
        string requestedFile,
        string outputFileName,
        float planarThreshold,
        int buildBatch,
        int writeBatch)
    {
        try
        {
            if (!NGonModelBuilder.TryLoad(requestedFile, Color.white, out List<ModelParallelogram> parallelograms, out _, out string modelError, planarThreshold))
            {
                Log.Warn($"[ExportSchematicNGon] {modelError}");
                yield break;
            }

            _activeModel = ExactModel.CreateDeferred(parallelograms, Vector3.zero, PrimitiveFlags.Visible, 1f);
            yield return _activeModel.BuildTrianglesCoroutine(PrimitiveFlags.Visible, buildBatch);

            if (_activeModel.ParallelogramCount == 0)
            {
                Log.Warn("[ExportSchematicNGon] Model has no valid non-degenerate triangles.");
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

            Log.Info($"[ExportSchematicNGon] Exported: {outputPath} (parallelograms={_activeModel.ParallelogramCount}, quads={_activeModel.QuadCount}, planarThreshold={planarThreshold.ToString(CultureInfo.InvariantCulture)}).");
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