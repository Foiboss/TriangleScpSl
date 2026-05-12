using TriangleScpSl.Core.NGons.Detectors;
using TriangleScpSl.Core.Paths;
using TriangleScpSl.Core.Triangulation.Parallelogram;
using UnityEngine;

namespace TriangleScpSl.Core.NGons;

public static class NGonModelBuilder
{
    public static bool TryLoad
    (
        string requestedFile,
        Color defaultColor,
        out List<ModelParallelogram> parallelograms,
        out List<ModelPrimitive> detectedPrimitives,
        out string normalizedFileName,
        out string error,
        float planarThreshold = 0f,
        bool useHiddenTailOptimization = true,
        bool detectPrimitives = true,
        float deduplicateVertexThreshold = 1e-4f,
        float deduplicatePlaneDistThreshold = 1e-4f,
        float smoothMaxAngle = SmoothnessCheck.DefaultMaxAngle,
        float smoothMinFraction = SmoothnessCheck.DefaultMinFraction,
        bool useEdgeWalkSampling = true,
        float hiddenTailPullIn = 0.1f)
    {
        parallelograms = [];
        detectedPrimitives = [];
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
                if (!ObjNGonParser.TryParseFile(modelPath, defaultColor, out ngons,
                    out string objError))
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

            ngons = NGonDeduplicator.Deduplicate(ngons,
                deduplicateVertexThreshold, deduplicatePlaneDistThreshold);

            // Build solid volume for winding number calculations (used by primitive
            // detection and hidden-tail optimization)
            ModelSolidVolume? solid = useHiddenTailOptimization
                ? ModelSolidVolume.Build(ngons)
                : null;

            // Detect primitives before planar merging (to preserve topology)
            List<NGonRaw> remainingNgons;

            if (detectPrimitives)
            {
                (detectedPrimitives, remainingNgons) = PrimitiveShapeDetector.Detect(ngons, solid, smoothMaxAngle, smoothMinFraction);
            }
            else
            {
                remainingNgons = ngons;
            }

            List<NGonRaw> planarNgons = PlanarNGonSplitter.SplitAll(remainingNgons, planarThreshold);

            List<ConvexNGon> convexNgons = ConvexNGonDecomposer.Decompose(planarNgons);

            parallelograms = HiddenTailParallelogramProcessor.Process(convexNgons, solid, useEdgeWalkSampling, hiddenTailPullIn);

            if (parallelograms.Count == 0 && detectedPrimitives.Count == 0)
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