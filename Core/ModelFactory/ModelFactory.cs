using AdminToys;
using TriangleScpSl.Core.FileToTriangles;
using TriangleScpSl.Core.Paths;
using UnityEngine;

namespace TriangleScpSl.Core.ModelFactory;

public static class ModelFactory
{
    static bool TryResolveModelPath(string? requestedFile, out string modelPath, out string normalizedFileName, out string error)
    {
        modelPath = string.Empty;
        normalizedFileName = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(requestedFile))
        {
            error = "Model file name cannot be empty.";
            return false;
        }

        string fileName = Path.GetFileName(requestedFile);

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

    public static TriangulatedModel.TriangulatedModel CreateModel
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible)
        => TriangulatedModel.TriangulatedModel.Create(triangles, worldPosition, flags);

    public static ParallelogramSpace.ParallelogramSpace CreateModel
    (
        IReadOnlyList<ModelTriangle> triangles,
        Vector3 worldPosition,
        PrimitiveFlags flags = PrimitiveFlags.Visible,
        float absoluteToleranceUnits = 0.001f)
        => ParallelogramSpace.ParallelogramSpace.Create(triangles, worldPosition, flags, absoluteToleranceUnits);

    public static bool TryLoadTriangles
    (
        string requestedFile,
        Color color,
        bool forceObjColor,
        out List<ModelTriangle> triangles,
        out string normalizedFileName,
        out string error)
    {
        if (!TryLoadTrianglesRaw(requestedFile, color, forceObjColor, out triangles, out normalizedFileName, out error))
            return false;

        FixWinding(triangles);
        return true;
    }

    internal static bool TryLoadTrianglesRaw
    (
        string requestedFile,
        Color color,
        bool forceObjColor,
        out List<ModelTriangle> triangles,
        out string normalizedFileName,
        out string error)
    {
        triangles = [];

        if (!TryResolveModelPath(requestedFile, out string modelPath, out normalizedFileName, out error))
            return false;

        if (modelPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
        {
            if (!ObjParser.TryParseFile(modelPath, out List<ModelTriangle> parsedObjTriangles, out string parseError))
            {
                error = $"Failed to parse OBJ: {parseError}";
                return false;
            }

            triangles = parsedObjTriangles;

            if (forceObjColor)
            {
                for (var i = 0; i < triangles.Count; i++)
                {
                    ModelTriangle tri = triangles[i];
                    triangles[i] = new ModelTriangle(tri.P1, tri.P2, tri.P3, color);
                }
            }
        }
        else
        {
            if (!StlParser.TryParseFile(modelPath, color, out List<ModelTriangle> parsedStlTriangles, out string parseError))
            {
                error = $"Failed to parse STL: {parseError}";
                return false;
            }

            triangles = parsedStlTriangles;
        }

        if (triangles.Count == 0)
        {
            error = "No valid non-degenerate triangles found in model file.";
            return false;
        }

        return true;
    }

    static void FixWinding(List<ModelTriangle> triangles)
    {
        for (var i = 0; i < triangles.Count; i++)
        {
            ModelTriangle t = triangles[i];
            triangles[i] = new ModelTriangle(t.P1, t.P3, t.P2, t.Color);
        }
    }
}