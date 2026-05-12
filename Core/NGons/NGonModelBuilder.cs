using Exiled.API.Features;
using System.Collections;
using TriangleScpSl.Core.Paths;
using TriangleScpSl.Core.Triangulation.Parallelogram;
using UnityEngine;

namespace TriangleScpSl.Core.NGons;

public static class NGonModelBuilder
{
    /// <summary>
    ///     Loads a model synchronously. Throws on failure.
    /// </summary>
    public static NGonModelResult Load(string requestedFile, Color defaultColor, NGonModelConfig? config = null)
    {
        config ??= NGonModelConfig.CreateFromSession();

        (string fileName, string modelPath) = ResolveModelPath(requestedFile);

        List<NGonRaw> ngons = ParseModel(modelPath, defaultColor);

        ngons = NGonDeduplicator.Deduplicate(ngons,
            config.DeduplicateVertexThreshold, config.DeduplicatePlaneDistThreshold);

        ModelSolidVolume? solid = config.UseHiddenTailOptimization || config.DetectPrimitives
            ? ModelSolidVolume.Build(ngons)
            : null;

        List<ModelPrimitive> detectedPrimitives;
        List<NGonRaw> remainingNgons;

        if (config.DetectPrimitives)
        {
            (detectedPrimitives, remainingNgons) = PrimitiveShapeDetector.Detect(
                ngons, solid, config.SmoothMaxAngle, config.SmoothMinFraction);
        }
        else
        {
            detectedPrimitives = [];
            remainingNgons = ngons;
        }

        List<NGonRaw> planarNgons = PlanarNGonSplitter.SplitAll(remainingNgons, config.PlanarThreshold);
        List<ConvexNGon> convexNgons = ConvexNGonDecomposer.Decompose(planarNgons);

        List<ModelParallelogram> parallelograms = HiddenTailParallelogramProcessor.Process(
            convexNgons, solid, config.UseEdgeWalkSampling, config.HiddenTailPullIn);

        if (parallelograms.Count == 0 && detectedPrimitives.Count == 0)
            throw new InvalidOperationException("No valid geometry produced from model polygons.");

        return new NGonModelResult
        {
            Parallelograms = parallelograms,
            DetectedPrimitives = detectedPrimitives,
            NormalizedFileName = fileName,
        };
    }

    /// <summary>
    ///     Loads a model as a coroutine, yielding periodically to avoid freezing.
    ///     Call with CoroutineHost.Run. The result callback fires when done.
    /// </summary>
    public static IEnumerator LoadCoroutine
    (
        string requestedFile,
        Color defaultColor,
        Action<NGonModelResult?> onComplete,
        NGonModelConfig? config = null)
    {
        config ??= NGonModelConfig.CreateFromSession();

        string fileName;
        List<NGonRaw> ngons;

        try
        {
            (fileName, string modelPath) = ResolveModelPath(requestedFile);
            ngons = ParseModel(modelPath, defaultColor);

            ngons = NGonDeduplicator.Deduplicate(ngons,
                config.DeduplicateVertexThreshold, config.DeduplicatePlaneDistThreshold);
        }
        catch (Exception ex)
        {
            Log.Error($"[NGonModelBuilder] {ex.Message}");
            onComplete(null);
            yield break;
        }

        yield return null;

        ModelSolidVolume? solid = null;

        if (config.UseHiddenTailOptimization || config.DetectPrimitives)
        {
            solid = ModelSolidVolume.Build(ngons);
            yield return null;
        }

        List<ModelPrimitive> detectedPrimitives = [];
        List<NGonRaw> remainingNgons = ngons;

        if (config.DetectPrimitives)
        {
            var detectDone = false;

            yield return PrimitiveShapeDetector.DetectCoroutine(
                ngons, solid, config.SmoothMaxAngle, config.SmoothMinFraction, config.MaxMsPerFrame,
                (primitives, remaining) =>
                {
                    detectedPrimitives = primitives;
                    remainingNgons = remaining;
                    detectDone = true;
                });

            if (!detectDone)
            {
                Log.Error("[NGonModelBuilder] Primitive detection coroutine did not complete.");
                onComplete(null);
                yield break;
            }
        }

        List<NGonRaw> planarNgons = PlanarNGonSplitter.SplitAll(remainingNgons, config.PlanarThreshold);
        yield return null;

        List<ConvexNGon> convexNgons = ConvexNGonDecomposer.Decompose(planarNgons);
        yield return null;

        List<ModelParallelogram> parallelograms = [];
        var paraDone = false;

        yield return HiddenTailParallelogramProcessor.ProcessCoroutine(
            convexNgons, solid, config.UseEdgeWalkSampling, config.HiddenTailPullIn, config.MaxMsPerFrame,
            result =>
            {
                parallelograms = result;
                paraDone = true;
            });

        if (!paraDone)
        {
            Log.Error("[NGonModelBuilder] Parallelogram processing coroutine did not complete.");
            onComplete(null);
            yield break;
        }

        if (parallelograms.Count == 0 && detectedPrimitives.Count == 0)
        {
            Log.Warn("[NGonModelBuilder] No valid geometry produced from model polygons.");
            onComplete(null);
            yield break;
        }

        onComplete(new NGonModelResult
        {
            Parallelograms = parallelograms,
            DetectedPrimitives = detectedPrimitives,
            NormalizedFileName = fileName,
        });
    }

    static (string fileName, string modelPath) ResolveModelPath(string requestedFile)
    {
        if (string.IsNullOrWhiteSpace(requestedFile))
            throw new ArgumentException("Model file name cannot be empty.");

        string fileName = Path.GetFileName(requestedFile);

        if (!string.Equals(requestedFile, fileName, StringComparison.Ordinal))
            throw new ArgumentException("Only a file name is allowed (without directories).");

        string extension = Path.GetExtension(fileName);

        if (string.IsNullOrEmpty(extension))
        {
            string objName = fileName + ".obj";
            string objPath = TrianglePaths.GetModelPath(objName);

            if (File.Exists(objPath))
                fileName = objName;
            else
                throw new FileNotFoundException($"Model file not found: {objPath}");
        }
        else if (!fileName.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Only .obj files are supported for NGon models.");
        }

        string modelPath = TrianglePaths.GetModelPath(fileName);

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Model file not found: {modelPath}");

        return (fileName, modelPath);
    }

    static List<NGonRaw> ParseModel(string modelPath, Color defaultColor)
    {
        if (modelPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
        {
            if (!ObjNGonParser.TryParseFile(modelPath, defaultColor, out List<NGonRaw> ngons, out string objError))
                throw new InvalidOperationException(objError);

            if (ngons.Count == 0)
                throw new InvalidOperationException("No valid polygons found in model file.");

            return ngons;
        }

        throw new NotSupportedException($"Unsupported model format: {Path.GetExtension(modelPath)}");
    }
}