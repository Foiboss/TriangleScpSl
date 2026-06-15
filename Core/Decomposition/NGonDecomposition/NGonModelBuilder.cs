using Exiled.API.Features;
using MEC;
using TriangleScpSl.Core.Decomposition.NGonDecomposition.Geometry;
using TriangleScpSl.Core.Decomposition.NGonDecomposition.Merging;
using TriangleScpSl.Core.Decomposition.NGonDecomposition.Parallelogram;
using TriangleScpSl.Core.Decomposition.NGonDecomposition.Parsing;
using TriangleScpSl.Core.Paths;
using TriangleScpSl.Core.Primitives.Parallelogram;
using UnityEngine;

namespace TriangleScpSl.Core.Decomposition.NGonDecomposition;

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
                ngons, config, solid);
        }
        else
        {
            detectedPrimitives = [];
            remainingNgons = ngons;
        }

        List<NGonRaw> planarNgons = PlanarNGonSplitter.SplitAll(remainingNgons, config.PlanarThreshold);
        List<ConvexNGon> convexNgons = ConvexNGonDecomposer.Decompose(planarNgons);

        List<ModelParallelogram> parallelograms = HiddenTailParallelogramProcessor.Process(
            convexNgons, solid, config.UseEdgeWalkSampling, config.HiddenTailPullIn, config.AllowNonPlanarNGons);

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
    ///     Run it with <c>.Run()</c>. The result callback fires when done.
    /// </summary>
    public static IEnumerator<float> LoadCoroutine
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

        yield return Timing.WaitForOneFrame;

        ModelSolidVolume? solid = null;

        if (config.UseHiddenTailOptimization || config.DetectPrimitives)
        {
            solid = ModelSolidVolume.Build(ngons);
            yield return Timing.WaitForOneFrame;
        }

        List<ModelPrimitive> detectedPrimitives = [];
        List<NGonRaw> remainingNgons = ngons;

        if (config.DetectPrimitives)
        {
            var detectDone = false;

            yield return Timing.WaitUntilDone(Timing.RunCoroutine(PrimitiveShapeDetector.DetectCoroutine(
                ngons, config, solid, config.MaxMsPerFrame,
                (primitives, remaining) =>
                {
                    detectedPrimitives = primitives;
                    remainingNgons = remaining;
                    detectDone = true;
                })));

            if (!detectDone)
            {
                Log.Error("[NGonModelBuilder] Primitive detection coroutine did not complete.");
                onComplete(null);
                yield break;
            }
        }

        List<NGonRaw> planarNgons = PlanarNGonSplitter.SplitAll(remainingNgons, config.PlanarThreshold);
        yield return Timing.WaitForOneFrame;

        List<ConvexNGon> convexNgons = ConvexNGonDecomposer.Decompose(planarNgons);
        yield return Timing.WaitForOneFrame;

        List<ModelParallelogram> parallelograms = [];
        var paraDone = false;

        yield return Timing.WaitUntilDone(Timing.RunCoroutine(HiddenTailParallelogramProcessor.ProcessCoroutine(
            convexNgons, solid, config.UseEdgeWalkSampling, config.HiddenTailPullIn, config.AllowNonPlanarNGons, config.MaxMsPerFrame,
            result =>
            {
                parallelograms = result;
                paraDone = true;
            })));

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