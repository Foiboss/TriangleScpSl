using TriangleScpSl.Core.FileToTriangles;
using TriangleScpSl.Core.Paths;
using TriangleScpSl.Core.Triangulation.Triangle;
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

        string extension = Path.GetExtension(fileName);

        // Only accept .obj files (no .stl support for triangle commands)
        if (string.IsNullOrEmpty(extension))
        {
            string objName = fileName + ".obj";
            string objPath = TrianglePaths.GetModelPath(objName);

            if (File.Exists(objPath))
                fileName = objName;
            else
            {
                error = $"Model file not found: {objPath}";
                return false;
            }
        }
        else if (!fileName.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
        {
            error = "Only .obj files are supported.";
            return false;
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

    internal static bool TryLoadTrianglesRaw
    (
        string requestedFile,
        Color fallbackColor,
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
            if (!ObjParser.TryParseFile(modelPath, fallbackColor, out List<ModelTriangle> parsedObjTriangles, out string parseError))
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
                    triangles[i] = new ModelTriangle(tri.P1, tri.P2, tri.P3, fallbackColor);
                }
            }
        }

        if (triangles.Count == 0)
        {
            error = "No valid non-degenerate triangles found in model file.";
            return false;
        }

        return true;
    }
}