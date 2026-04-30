using TriangleScpSl.Core.ModelFactory;
using TriangleScpSl.Core.Paths;
using UnityEngine;

namespace TriangleScpSl.Core.NGons;

// Loads an FBX file and converts the NGon pipeline output into ModelTriangles
// ready for ExactModel or ApproximateModel.
//
// Each NGon with n vertices produces (n-3) parallelograms and 1 triangle.
// Each parallelogram is split along its vUp diagonal into 2 ModelTriangles.
// All triangles are CCW relative to their face normal.
public static class NgonModelBuilder
{
    // Resolves the FBX file path, runs the full NGon pipeline, and returns ModelTriangles.
    // requestedFile: file name only (no directory path), with or without .fbx extension.
    // defaultColor: fallback face color when the FBX has no vertex color layer.
    public static bool TryLoad(
        string requestedFile,
        Color defaultColor,
        out List<ModelTriangle> triangles,
        out string normalizedFileName,
        out string error)
    {
        triangles = [];
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

        if (!fileName.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            fileName += ".fbx";

        string modelPath = TrianglePaths.GetModelPath(fileName);

        if (!File.Exists(modelPath))
        {
            error = $"FBX file not found: {modelPath}";
            return false;
        }

        normalizedFileName = fileName;

        try
        {
            List<NgonRaw> ngons = FbxNgonParser.Parse(modelPath, defaultColor);

            if (ngons.Count == 0)
            {
                error = "No valid polygons found in FBX file.";
                return false;
            }

            List<ConvexNgon> convexNgons = ConvexNgonDecomposer.Decompose(ngons);
            var (parallelograms, triangleInfos) = ParallelogramProcessor.Process(convexNgons);

            triangles = ToModelTriangles(parallelograms, triangleInfos);

            if (triangles.Count == 0)
            {
                error = "No valid triangles produced from FBX polygons.";
                return false;
            }

            return true;
        }
        catch (NotSupportedException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse FBX: {ex.Message}";
            return false;
        }
    }

    // Converts the NGon pipeline output to a flat ModelTriangle list.
    // Each ParallelogramInfo becomes 2 triangles (split along the vUp diagonal).
    // Each TriangleInfo becomes 1 triangle.
    // All resulting triangles are CCW relative to their face normal.
    static List<ModelTriangle> ToModelTriangles(
        List<ParallelogramInfo> parallelograms,
        List<TriangleInfo> triangleInfos)
    {
        var result = new List<ModelTriangle>(triangleInfos.Count + parallelograms.Count * 2);

        foreach (TriangleInfo tri in triangleInfos)
            result.Add(new ModelTriangle(tri.V0, tri.V1, tri.V2, tri.Color));

        foreach (ParallelogramInfo para in parallelograms)
        {
            // Four vertices of the rhombus (split along vUp diagonal):
            //   v1 = center + vUp  (top)
            //   v2 = center + vLeft
            //   v3 = center - vUp  (bottom)
            //   v4 = center - vLeft
            // CCW winding is guaranteed because cross(vUp, vLeft) · normal > 0.
            Vector3 v1 = para.Center + para.VUp;
            Vector3 v2 = para.Center + para.VLeft;
            Vector3 v3 = para.Center - para.VUp;
            Vector3 v4 = para.Center - para.VLeft;

            result.Add(new ModelTriangle(v1, v2, v3, para.Color)); // top-left triangle
            result.Add(new ModelTriangle(v1, v3, v4, para.Color)); // bottom-right triangle
        }

        return result;
    }
}
