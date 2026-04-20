using System.Globalization;
using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using TriangleScpSl.Core.Paths;
using TriangleScpSl.Core.TriangulatedModel;
using TriangleScpSl.ParallelogramSpace;
using UnityEngine;

namespace TriangleScpSl.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public sealed class ExportSchematicV2Command : ICommand
{
    readonly Color _fallbackColor = Color.white;

    public string Command { get; } = "ExportSchematicV2";
    public string[] Aliases { get; } = [];
    public string Description { get; } = "Exports .obj/.stl as ProjectMER schematic JSON. Usage: <model file> <output file> [accuracy(0.001)] [previewScale]";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (arguments.Count is < 2 or > 4)
        {
            response = "Usage: ExportSchematicV2 <model file (.obj/.stl)> <output json file> [accuracy(0.001)] [previewScale]";
            return false;
        }

        string requestedFile = arguments.Array?[arguments.Offset] ?? string.Empty;
        string outputFileArg = arguments.Array?[arguments.Offset + 1] ?? string.Empty;

        if (!TryNormalizeOutputName(outputFileArg, out string outputFileName))
        {
            response = "Output must be file name only (without directories).";
            return false;
        }

        const bool forceObjColor = false;
        float accuracy = 0.001f;

        if (arguments.Count >= 3)
        {
            string rawAccuracy = arguments.Array?[arguments.Offset + 2] ?? string.Empty;

            if (!float.TryParse(rawAccuracy, out accuracy))
            {
                response = "Invalid accuracy value";
                return false;
            }
        }

        var previewScale = 1f;

        if (arguments.Count >= 4)
        {
            string rawScale = arguments.Array?[arguments.Offset + 3] ?? string.Empty;

            if (!float.TryParse(rawScale, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out previewScale) || previewScale <= 0f)
            {
                response = "Invalid previewScale. Use a positive number (example: 1 or 0.5).";
                return false;
            }
        }

        Player? player = Player.Get(sender);
        Vector3 spawnPosition = Vector3.zero;

        if (player is not null)
            spawnPosition = player.Position + player.GameObject.transform.forward * 2.5f + Vector3.up * 1.2f;

        if (!ModelFactoryV2.TryCreateModel(
            requestedFile,
            spawnPosition,
            _fallbackColor,
            forceObjColor,
            out ParallelogramSpace.ParallelogramSpace? parallelogramSpace,
            out _,
            out string modelError,
            PrimitiveFlags.Visible,
            accuracy))
        {
            response = modelError;
            return false;
        }

        try
        {
            parallelogramSpace!.Scale = Vector3.one * previewScale;

            TrianglePaths.EnsureSchematicDirectoryExists(outputFileName);
            string outputPath = TrianglePaths.GetSchematicOutputPath(outputFileName);
            string schematicName = TrianglePaths.GetSchematicFolderName(outputFileName);

            if (!ProjectMerSchematicExporter.TryExport(parallelogramSpace, outputPath, schematicName, out string exportError))
            {
                response = $"Failed to export schematic: {exportError}";
                return false;
            }

            response = $"Schematic exported to LabAPI MER folder: {outputPath} (triangles={parallelogramSpace.Count}, quads={parallelogramSpace.QuadCount}, previewScale={previewScale.ToString(CultureInfo.InvariantCulture)}).";
            return true;
        }
        catch (Exception ex)
        {
            response = $"Export error: {ex.Message}";
            return false;
        }
        finally
        {
            parallelogramSpace?.Destroy();
        }
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