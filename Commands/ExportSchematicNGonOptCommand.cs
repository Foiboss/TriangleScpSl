using System.Collections;
using System.Globalization;
using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using TriangleScpSl.Core.Models.ApproximateModel;
using TriangleScpSl.Core.NGons;
using TriangleScpSl.Core.NGons.Detectors;
using TriangleScpSl.Core.Paths;
using TriangleScpSl.Core.ProjectMerExport;
using TriangleScpSl.Core.Runtime;
using UnityEngine;

namespace TriangleScpSl.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public sealed class ExportSchematicNGonOptCommand : ICommand
{
    Coroutine? _exportCoroutine;
    bool _isExporting;
    ApproximateModel? _activeModel;

    public string Command { get; } = "ExportSchematicNGonOpt";
    public string[] Aliases { get; } = [];

    public string Description { get; } =
        "Exports an OBJ as ProjectMER schematic JSON with all optimizations. " +
        "Usage: <model file (.obj)> <output JSON file> [accuracy(0.001)] [smoothness(0.32)]";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (_isExporting)
        {
            CancelCurrentExport();
            response = "Export cancelled.";
            return true;
        }

        if (arguments.Count is < 2 or > 4)
        {
            response = "Usage: ExportSchematicNGonOpt <model file (.obj)> <output JSON file> [accuracy(0.001)] [smoothness(0.32)]";
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

        if (arguments.Count >= 3)
        {
            string rawAccuracy = arguments.Array?[arguments.Offset + 2] ?? string.Empty;

            if (!float.TryParse(rawAccuracy, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out accuracy) || accuracy <= 0f)
            {
                response = "Invalid accuracy. Use a positive number (example: 0.001).";
                return false;
            }
        }

        float smoothness = SmoothnessCheck.DefaultMaxAngle;

        if (arguments.Count >= 4)
        {
            string rawSmoothness = arguments.Array?[arguments.Offset + 3] ?? string.Empty;

            if (!float.TryParse(rawSmoothness, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out smoothness) || smoothness <= 0f)
            {
                response = "Invalid smoothness value. Use a positive number in radians (default: 0.32 ~ 18 deg).";
                return false;
            }
        }

        int buildBatch = Mathf.Max(1, Plugin.Instance?.Config.ExportBuildBatchSize ?? 64);
        int writeBatch = Mathf.Max(1, Plugin.Instance?.Config.ExportWriteBatchSize ?? 256);

        var config = new NGonModelConfig
        {
            UseHiddenTailOptimization = true,
            DetectPrimitives = true,
            SmoothMaxAngle = smoothness,
        };

        _isExporting = true;

        _exportCoroutine = CoroutineHost.Run(
            ExportRoutine(requestedFile, outputFileName, config, accuracy, buildBatch, writeBatch));

        response = "Export started asynchronously. Run command again to cancel.";
        return true;
    }

    IEnumerator ExportRoutine
    (
        string requestedFile,
        string outputFileName,
        NGonModelConfig config,
        float accuracy,
        int buildBatch,
        int writeBatch)
    {
        try
        {
            NGonModelResult? loadResult = null;

            yield return NGonModelBuilder.LoadCoroutine(requestedFile, Color.white, result => { loadResult = result; }, config);

            if (loadResult == null)
            {
                Log.Warn("[ExportSchematicNGonOpt] Failed to load model.");
                yield break;
            }

            _activeModel = ApproximateModel.CreateDeferred(
                loadResult.Parallelograms,
                loadResult.DetectedPrimitives,
                Vector3.zero,
                PrimitiveFlags.Visible,
                accuracy);

            yield return _activeModel.BuildTrianglesCoroutine(PrimitiveFlags.Visible, buildBatch);

            if (_activeModel is { ParallelogramCount: 0, NativePrimitiveCount: 0 })
            {
                Log.Warn("[ExportSchematicNGonOpt] Model has no valid geometry.");
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
                Log.Warn($"[ExportSchematicNGonOpt] Failed to export schematic: {exportError}");
                yield break;
            }

            Log.Info(
                $"[ExportSchematicNGonOpt] Exported: {outputPath} " +
                $"(ParallelogramCount={_activeModel.ParallelogramCount}, " +
                $"NativePrimitiveCount={_activeModel.NativePrimitiveCount}, " +
                $"PrimitiveCount={_activeModel.PrimitiveCount}, " +
                $"accuracy={accuracy.ToString(CultureInfo.InvariantCulture)}, " +
                $"smoothness={config.SmoothMaxAngle.ToString(CultureInfo.InvariantCulture)}).");
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