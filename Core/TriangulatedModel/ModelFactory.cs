using AdminToys;
using TriangleScpSl.Core.FileToTriangles;
using TriangleScpSl.Core.Paths;
using UnityEngine;

namespace TriangleScpSl.Core.TriangulatedModel;

public static class ModelFactory
{
    static bool TryResolveModelPath(string? requestedFile, out string modelPath, out string normalizedFileName, out string error)
    {
        modelPath = string.Empty;
        normalizedFileName = string.Empty;
        error = string.Empty;

        string fileName = Path.GetFileName(requestedFile ?? string.Empty);

        if (!string.Equals(requestedFile, fileName, StringComparison.Ordinal))
        {
            error = "Only a file name is allowed (without directories).";
            return false;
        }

        if (!fileName.EndsWith(".stl", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".stl";
        }

        modelPath = TrianglePaths.GetModelPath(fileName);

        if (!File.Exists(modelPath))
        {
            error = $"Model file not found: {modelPath}";
            return false;
        }

        normalizedFileName = fileName;
        return true;
    }

    public static TriangulatedModel CreateModel
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        bool swapSecondAndThirdVertices,
        PrimitiveFlags flags = PrimitiveFlags.Visible)
        => TriangulatedModel.Create(triangles, worldPosition, flags, swapSecondAndThirdVertices: swapSecondAndThirdVertices);

    public static bool TryCreateModel
    (
        string requestedFile,
        Vector3 worldPosition,
        Color color,
        bool forceObjColor,
        out TriangulatedModel? model,
        out string normalizedFileName,
        out string error,
        PrimitiveFlags flags = PrimitiveFlags.Visible)
    {
        model = null;

        if (!TryResolveModelPath(requestedFile, out string modelPath, out normalizedFileName, out error))
            return false;

        if (modelPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
        {
            if (!ObjParser.TryParseFile(modelPath, out List<ModelTriangle> parsedObjTriangles, out string parseError))
            {
                error = $"Failed to parse OBJ: {parseError}";
                return false;
            }

            // OBJ files in this pipeline need winding conversion as well.
            model = CreateModel(parsedObjTriangles, worldPosition, true, flags);

            if (forceObjColor)
                model.Color = color;
        }
        else
        {
            if (!StlParser.TryParseFile(modelPath, color, out List<ModelTriangle> parsedStlTriangles, out string parseError))
            {
                error = $"Failed to parse STL: {parseError}";
                return false;
            }

            model = CreateModel(parsedStlTriangles, worldPosition, true, flags);
        }

        if (model.Count == 0)
        {
            model.Destroy();
            model = null;
            error = "No valid non-degenerate triangles found in model file.";
            return false;
        }

        return true;
    }
}