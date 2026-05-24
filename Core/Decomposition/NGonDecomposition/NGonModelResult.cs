using TriangleScpSl.Core.Primitives.Parallelogram;

namespace TriangleScpSl.Core.Decomposition.NGonDecomposition;

/// <summary>
///     Result of NGon model loading: parallelograms, detected primitives, and the normalized file name.
/// </summary>
public sealed class NGonModelResult
{
    public required List<ModelParallelogram> Parallelograms { get; init; }
    public required List<ModelPrimitive> DetectedPrimitives { get; init; }
    public required string NormalizedFileName { get; init; }
}