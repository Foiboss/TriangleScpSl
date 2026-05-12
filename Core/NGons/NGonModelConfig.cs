using TriangleScpSl.Core.NGons.Detectors;

namespace TriangleScpSl.Core.NGons;

/// <summary>
///     Configuration for NGon model loading, primitive detection, and hidden-tail optimization.
/// </summary>
public sealed class NGonModelConfig
{
    /// <summary>Planar merging threshold (0 = exact only).</summary>
    public float PlanarThreshold { get; init; }

    /// <summary>Whether to use hidden-tail parallelogram optimization (requires solid volume).</summary>
    public bool UseHiddenTailOptimization { get; init; } = true;

    /// <summary>Whether to detect native primitives (spheres, cylinders, cubes).</summary>
    public bool DetectPrimitives { get; init; } = true;

    /// <summary>Vertex deduplication distance threshold.</summary>
    public float DeduplicateVertexThreshold { get; init; } = 1e-4f;

    /// <summary>Plane distance deduplication threshold.</summary>
    public float DeduplicatePlaneDistThreshold { get; init; } = 1e-4f;

    /// <summary>Max angle (radians) between adjacent face normals to consider them smooth.</summary>
    public float SmoothMaxAngle { get; init; } = SmoothnessCheck.DefaultMaxAngle;

    /// <summary>Minimum fraction of smooth edges required for a surface to be "smooth".</summary>
    public float SmoothMinFraction { get; init; } = SmoothnessCheck.DefaultMinFraction;

    /// <summary>Whether to use edge-walk sampling for hidden-tail verification (catches pits/holes between sample points).</summary>
    public bool UseEdgeWalkSampling { get; init; } = false;

    /// <summary>Pull-in distance along normal for hidden-tail solid checks.</summary>
    public float HiddenTailPullIn { get; init; } = 0.1f;

    /// <summary>Default configuration with all optimizations enabled.</summary>
    public static NGonModelConfig Default { get; } = new();
}