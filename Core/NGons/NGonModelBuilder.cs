using TriangleScpSl.Core.Paths;
using TriangleScpSl.Core.Triangulation.Parallelogram;
using UnityEngine;

namespace TriangleScpSl.Core.NGons;

// Load OBJ and decompose N-gons into parallelograms via pipeline:
// ObjNGonParser → PlanarNGonSplitter → ConvexNGonDecomposer → ParallelogramProcessor
//
// planarThreshold: max vertex displacement when snapping to plane (0 = disabled).
public static class NGonModelBuilder
{
    // Load OBJ file, decompose N-gons, return parallelograms.
    // requestedFile: file name only (no path), with or without .obj extension.
    // defaultColor: fallback color if OBJ has no color data.
    // planarThreshold: max vertex displacement during plane snapping (0 = disabled).
    public static bool TryLoad
    (
        string requestedFile,
        Color defaultColor,
        out List<ModelParallelogram> parallelograms,
        out string normalizedFileName,
        out string error,
        float planarThreshold = 0f)
    {
        parallelograms = [];
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
            error = "Only .obj files are supported for NGon models.";
            return false;
        }

        string modelPath = TrianglePaths.GetModelPath(fileName);

        if (!File.Exists(modelPath))
        {
            error = $"Model file not found: {modelPath}";
            return false;
        }

        normalizedFileName = fileName;

        try
        {
            List<NGonRaw> ngons = [];

            if (modelPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
            {
                if (!ObjNGonParser.TryParseFile(modelPath, defaultColor, out ngons, out string objError))
                {
                    error = objError;
                    return false;
                }
            }

            if (ngons.Count == 0)
            {
                error = "No valid polygons found in model file.";
                return false;
            }

            // Split non-planar faces (when planarThreshold > 0)
            List<NGonRaw> planarNgons = PlanarNGonSplitter.SplitAll(ngons, planarThreshold);

            List<ConvexNGon> convexNgons = ConvexNGonDecomposer.Decompose(planarNgons);
            parallelograms = ParallelogramProcessor.Process(convexNgons);

            if (parallelograms.Count == 0)
            {
                error = "No valid triangles produced from model polygons.";
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
            error = $"Failed to parse model: {ex.Message}";
            return false;
        }
    }
}