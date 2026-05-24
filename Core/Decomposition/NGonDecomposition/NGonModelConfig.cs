using System.Globalization;
using System.Reflection;
using TriangleScpSl.Core.Decomposition.NGonDecomposition.Detectors;

namespace TriangleScpSl.Core.Decomposition.NGonDecomposition;

/// <summary>
///     Configuration for NGon model loading, primitive detection, and hidden-tail optimization.
///     Use <see cref="Session" /> for the current session defaults (modifiable at runtime).
///     Use <see cref="CreateFromSession" /> to snapshot current defaults for a single operation.
///     All public instance properties with a getter and setter are automatically exposed
///     for runtime get/set by name (case-insensitive).
/// </summary>
public sealed class NGonModelConfig
{
    static readonly Dictionary<string, PropertyInfo> Properties;

    static NGonModelConfig()
    {
        Properties = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (PropertyInfo prop in typeof(NGonModelConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop is { CanRead: true, CanWrite: true })
                Properties[prop.Name] = prop;
        }
    }

    // ── General ──

    /// <summary>Planar merging threshold (0 = exact only).</summary>
    public float PlanarThreshold { get; set; }

    /// <summary>Whether to use hidden-tail parallelogram optimization (requires solid volume).</summary>
    public bool UseHiddenTailOptimization { get; set; } = true;

    /// <summary>Whether to detect native primitives (spheres, cylinders, cubes).</summary>
    public bool DetectPrimitives { get; set; } = true;

    /// <summary>Vertex deduplication distance threshold.</summary>
    public float DeduplicateVertexThreshold { get; set; } = 1e-4f;

    /// <summary>Plane distance deduplication threshold.</summary>
    public float DeduplicatePlaneDistThreshold { get; set; } = 1e-4f;

    /// <summary>Max angle (radians) between adjacent face normals to consider them smooth.</summary>
    public float SmoothMaxAngle { get; set; } = SmoothnessCheck.DefaultMaxAngle;

    /// <summary>Minimum fraction of smooth edges required for a surface to be "smooth".</summary>
    public float SmoothMinFraction { get; set; } = SmoothnessCheck.DefaultMinFraction;

    /// <summary>Whether to use edge-walk sampling for hidden-tail verification (catches pits/holes between sample points).</summary>
    public bool UseEdgeWalkSampling { get; set; } = false;

    /// <summary>Pull-in distance along normal for hidden-tail solid checks.</summary>
    public float HiddenTailPullIn { get; set; } = 0.03f;

    /// <summary>
    ///     When true, non-planar n-gons are decomposed as-is instead of being fan-triangulated first.
    ///     Reduces primitive count on models with many non-planar faces at the cost of geometric inaccuracy.
    /// </summary>
    public bool AllowNonPlanarNGons { get; set; } = false;

    /// <summary>Accuracy for approximate model rendering (lower = more precise, more primitives).</summary>
    public float Accuracy { get; set; } = 0.001f;

    /// <summary>How many optimization passes to reparent stretch-children under visible quads.</summary>
    public int HierarchicalOptimizationPasses { get; set; } = 3;

    /// <summary>Maximum milliseconds per frame before yielding in coroutine mode.</summary>
    public float MaxMsPerFrame { get; set; } = 100f;

    // ── Sphere detection ──

    /// <summary>Max relative vertex deviation from mean radius for exact sphere fit.</summary>
    public float SphereTolerance { get; set; } = 0.05f;

    /// <summary>Max relative vertex deviation for approximate sphere fit.</summary>
    public float SphereApproxTolerance { get; set; } = 0.12f;

    /// <summary>Minimum face count to attempt sphere detection.</summary>
    public int SphereMinFaces { get; set; } = 6;

    /// <summary>Minimum solid angle coverage (radians) for partial sphere detection.</summary>
    public float SphereMinCoverage { get; set; } = 1.0f * 3.14159265f;

    /// <summary>Minimum solid angle coverage for approximate sphere detection.</summary>
    public float SphereMinApproxCoverage { get; set; } = 0.8f * 3.14159265f;

    /// <summary>Solid angle above which a sphere is considered fully covered (no solid check needed).</summary>
    public float SphereFullCoverage { get; set; } = 3.8f * 3.14159265f;

    /// <summary>Number of sample directions for hidden-surface solid checks on spheres.</summary>
    public int SphereHiddenSurfaceSamples { get; set; } = 64;

    // ── Cylinder detection ──

    /// <summary>Max relative vertex deviation from mean radius for exact cylinder fit.</summary>
    public float CylinderTolerance { get; set; } = 0.05f;

    /// <summary>Max relative vertex deviation for approximate cylinder fit.</summary>
    public float CylinderApproxTolerance { get; set; } = 0.12f;

    /// <summary>Minimum face count to attempt cylinder detection.</summary>
    public int CylinderMinFaces { get; set; } = 6;

    /// <summary>Minimum eigenvalue ratio (smallest/largest) for cylinder axis detection.</summary>
    public float CylinderMinEigenRatio { get; set; } = 0.6f;

    /// <summary>Minimum fraction of face normals pointing outward from cylinder axis.</summary>
    public float CylinderMinNormalsOutwardFraction { get; set; } = 0.5f;

    // ── Cube detection ──

    /// <summary>Normal direction clustering tolerance for cube face grouping.</summary>
    public float CubeNormalTolerance { get; set; } = 0.12f;

    /// <summary>Maximum dot product between cube axes (orthogonality check).</summary>
    public float CubeOrthogonalityTolerance { get; set; } = 0.07f;

    /// <summary>Relative vertex deviation from box surface (exact detection).</summary>
    public float CubeVertexTolerance { get; set; } = 0.02f;

    /// <summary>Relative vertex deviation from box surface (partial/relaxed detection).</summary>
    public float CubeRelaxedVertexTolerance { get; set; } = 0.05f;

    /// <summary>Minimum face count for cube detection.</summary>
    public int CubeMinFaces { get; set; } = 4;

    // ── Foreign vertex rejection ──

    /// <summary>
    ///     Normalized depth threshold (0 = surface, 1 = center) for rejecting primitives
    ///     that would cover foreign vertices near their surface. Higher values reject more
    ///     aggressively. Set to 0 to disable foreign-vertex rejection.
    /// </summary>
    public float SurfaceDepthThreshold { get; set; } = 0.15f;

    /// <summary>
    ///     Session-scoped defaults. Modify at runtime to change defaults for all subsequent operations.
    ///     Reset on plugin reload / server restart.
    /// </summary>
    public static NGonModelConfig Session { get; } = new();

    /// <summary>
    ///     All settable property names, for listing.
    /// </summary>
    public static IEnumerable<string> PropertyNames => Properties.Keys;

    /// <summary>Creates a snapshot copy of the current session defaults.</summary>
    public static NGonModelConfig CreateFromSession() => Session.Clone();

    /// <summary>Creates a deep copy of this config.</summary>
    public NGonModelConfig Clone()
    {
        var copy = new NGonModelConfig();

        foreach (PropertyInfo prop in Properties.Values)
            prop.SetValue(copy, prop.GetValue(this));

        return copy;
    }

    /// <summary>
    ///     Try to get a property value by name (case-insensitive). Returns null if name not found.
    /// </summary>
    public string? TryGetValue(string name)
    {
        if (!Properties.TryGetValue(name, out PropertyInfo? prop))
            return null;

        object? value = prop.GetValue(this);

        return value switch
        {
            float f => f.ToString("G", CultureInfo.InvariantCulture),
            bool b => b.ToString(),
            _ => value?.ToString(),
        };
    }

    /// <summary>
    ///     Try to set a property value by name (case-insensitive). Returns false if name not found or value invalid.
    /// </summary>
    public bool TrySetValue(string name, string value)
    {
        if (!Properties.TryGetValue(name, out PropertyInfo? prop))
            return false;

        Type type = prop.PropertyType;

        if (type == typeof(float))
        {
            if (!float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float f))
                return false;

            prop.SetValue(this, f);
            return true;
        }

        if (type == typeof(bool))
        {
            if (!bool.TryParse(value, out bool b))
                return false;

            prop.SetValue(this, b);
            return true;
        }

        if (type == typeof(int))
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                return false;

            prop.SetValue(this, i);
            return true;
        }

        return false;
    }
}