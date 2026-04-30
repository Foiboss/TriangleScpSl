using TriangleScpSl.Core.ModelFactory;
using TriangleScpSl.Core.Paths;
using UnityEngine;

namespace TriangleScpSl.Core.NGons;

// Loads an OBJ file and converts the NGon pipeline output into ModelTriangles
// ready for ExactModel or ApproximateModel.
//
// Each NGon with n vertices produces (n-3) parallelograms and 1 triangle.
// Each parallelogram is split along its vUp diagonal into 2 ModelTriangles.
// All triangles are CCW relative to their face normal.
public static class NGonModelBuilder
{
    // Resolves the model file path, runs the full NGon pipeline, and returns ModelTriangles.
    // requestedFile: file name only (no directory path), with or without .obj extension.
    // defaultColor: fallback face color when the model has no color data.
    public static bool TryLoad
    (
        string requestedFile,
        Color defaultColor,
        out List<ModelParallelogram> parallelograms,
        out string normalizedFileName,
        out string error)
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

            List<ConvexNGon> convexNgons = ConvexNGonDecomposer.Decompose(ngons);
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