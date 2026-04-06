using AdminToys;
using Exiled.API.Features;
using Triangle.Core.Triangulation.Triangle;
using UnityEngine;

namespace Triangle.Core.Triangulation.Stl;

public static class StlModelFactory
{
    static readonly string StlFolderPath = Path.Combine(Paths.Plugins, "StlModels");

    public static bool TryParseTriangles(
        string requestedFile,
        out List<StlTriangle> parsedTriangles,
        out string normalizedFileName,
        out string error)
    {
        parsedTriangles = [];
        normalizedFileName = string.Empty;
        error = string.Empty;

        string fileName = Path.GetFileName(requestedFile ?? string.Empty);

        if (!string.Equals(requestedFile, fileName, StringComparison.Ordinal))
        {
            error = "Only a file name is allowed (without directories).";
            return false;
        }

        if (!fileName.EndsWith(".stl", StringComparison.OrdinalIgnoreCase))
            fileName += ".stl";

        string stlFilePath = Path.Combine(StlFolderPath, fileName);

        if (!File.Exists(stlFilePath))
        {
            error = $"STL file not found: {stlFilePath}";
            return false;
        }

        if (!StlParser.TryParseFile(stlFilePath, out parsedTriangles, out string parseError))
        {
            error = $"Failed to parse STL: {parseError}";
            return false;
        }

        normalizedFileName = fileName;
        return true;
    }

    public static TriangulatedModel CreateModel(
        IReadOnlyList<StlTriangle> triangles,
        Vector3 worldPosition,
        Color color,
        PrimitiveFlags flags = PrimitiveFlags.Visible)
    {
        return TriangulatedModel.Create(triangles, worldPosition, color, flags);
    }

    public static bool TryCreateModel(
        string requestedFile,
        Vector3 worldPosition,
        Color color,
        out TriangulatedModel? model,
        out string normalizedFileName,
        out string error,
        PrimitiveFlags flags = PrimitiveFlags.Visible)
    {
        model = null;

        if (!TryParseTriangles(requestedFile, out List<StlTriangle> parsedTriangles, out normalizedFileName, out error))
            return false;

        model = CreateModel(parsedTriangles, worldPosition, color, flags);

        if (model.Count == 0)
        {
            model.Destroy();
            model = null;
            error = "No valid non-degenerate triangles found in STL.";
            return false;
        }

        return true;
    }
}

