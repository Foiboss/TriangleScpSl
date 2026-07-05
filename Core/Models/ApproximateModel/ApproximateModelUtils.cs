using AdminToys;
using Exiled.API.Features.Toys;
using UnityEngine;

namespace TriangleScpSl.Core.Models.ApproximateModel;

public static class ApproximateModelUtils
{
    public static Primitive CreateStretch(float theta, float phi)
    {
        var stretch = Primitive.Create(
            PrimitiveType.Quad,
            PrimitiveFlags.None,
            Vector3.zero,
            null,
            new Vector3(Mathf.Cos(phi) * VectorPhiSolver.F, Mathf.Sin(phi) * VectorPhiSolver.F, 1f),
            true,
            null
        );
        stretch.Rotation = Quaternion.Euler(0f, 0f, theta * Mathf.Rad2Deg);
        return stretch;
    }

    /// <summary>
    ///     Applies the full forward phi-transform (rotate -theta, then scale 1/(cos(phi)*F), 1/(sin(phi)*F)
    ///     World vector → local vector in the stretch space
    /// </summary>
    public static Vector3 ForwardTransform(Vector3 v, float theta, float phi)
    {
        // Validate phi to avoid division by zero
        double cp = Math.Cos(phi);
        double sp = Math.Sin(phi);

        if (Math.Abs(cp) < 1e-10 || Math.Abs(sp) < 1e-10)
            return v; // Degenerate case, return unchanged

        double c = Math.Cos(theta), s = Math.Sin(theta);
        double rx = v.x * c + v.y * s;
        double ry = -v.x * s + v.y * c;
        const double f = VectorPhiSolver.F;

        return new Vector3(
            (float)(rx / (cp * f)),
            (float)(ry / (sp * f)),
            v.z
        );
    }

    /// <summary>
    ///     Applies the inverse stretch (as Unity would apply to a child with scale (cos(phi)*F, sin(phi)*F, 1) and rotZ=θ)
    ///     Local vector → world
    /// </summary>
    public static Vector3 InverseTransform(Vector3 vLocal, float theta, float phi)
    {
        double cp = Math.Cos(phi), sp = Math.Sin(phi);
        const double f = VectorPhiSolver.F;
        double sx = vLocal.x * (cp * f);
        double sy = vLocal.y * (sp * f);
        double c = Math.Cos(theta), s = Math.Sin(theta);

        if (double.IsNaN(sx) || double.IsNaN(sy))
            throw new InvalidOperationException($"InverseTransform produced NaN: sx={sx}, sy={sy}, vLocal={vLocal}, theta={theta}, phi={phi}");

        // Inverse of R(-theta) is R(theta)
        return new Vector3(
            (float)(sx * c - sy * s),
            (float)(sx * s + sy * c),
            vLocal.z
        );
    }

    /// <summary>
    ///    Measures the world-space vertex displacement of the rendered parallelogram
    ///    when CreateParallelogram is run with this candidate stretch instead of one
    ///    that exactly fits (vLeft, vUp). Returns absolute world units, scales linearly
    ///    with parallelogram size, and equals 0 whenever the candidate would render
    ///    (vLeft, vUp) exactly - including non-trivial cases where (candidateTheta, candidatePhi)
    ///    differs from the "true" solver output but still satisfies |v1C| = |v2C|.
    /// </summary>
    public static float MaxVertexError
    (
        Vector3 vLeft, Vector3 vUp,
        float candidateTheta, float candidatePhi)
    {
        // Project the half-diagonals into the candidate stretch's local space -
        // these are exactly the v1ForStretch / v2ForStretch CreateParallelogram uses.
        Vector3 v1C = ForwardTransform(vLeft, candidateTheta, candidatePhi);
        Vector3 v2C = ForwardTransform(vUp, candidateTheta, candidatePhi);

        Vector3 sumLocal = v1C + v2C;
        Vector3 diffLocal = v1C - v2C;
        float a = sumLocal.magnitude;
        float b = diffLocal.magnitude;

        if (a < 1e-12f || b < 1e-12f) return float.MaxValue;

        // Same orientation CreateParallelogram applies via LookRotation:
        // local Y along (v1-v2), local Z along normal, local X = Y × Z.
        // Normal from the unit diagonals, mirroring CreateParallelogram.
        Vector3 yAxis = diffLocal / b;
        Vector3 normalLocal = Vector3.Cross(yAxis, sumLocal / a);
        if (normalLocal.sqrMagnitude < 1e-24f) return float.MaxValue;
        normalLocal = normalLocal.normalized;

        Vector3 xAxis = Vector3.Cross(yAxis, normalLocal);

        // The unit quad after scale (a, b, 1) and that rotation has 4 corners at
        // (±a/2)X + (±b/2)Y in candidate-local. Two of them; their negatives
        // give the other two and produce symmetric errors.
        Vector3 cornerA = a * 0.5f * xAxis + b * 0.5f * yAxis;
        Vector3 cornerB = a * 0.5f * xAxis - b * 0.5f * yAxis;

        // Apply the candidate stretch's parent transform: candidate-local -> world.
        Vector3 worldA = InverseTransform(cornerA, candidateTheta, candidatePhi);
        Vector3 worldB = InverseTransform(cornerB, candidateTheta, candidatePhi);

        // Geometric matching is fixed: cornerA always falls on the ±vUp corner pair,
        // cornerB on the ±vLeft pair (this drops out of LookRotation's basis choice).
        // Min(±) just picks which side of the pair.
        float dA = Mathf.Min((worldA - vUp).magnitude, (worldA + vUp).magnitude);
        float dB = Mathf.Min((worldB - vLeft).magnitude, (worldB + vLeft).magnitude);
        return Mathf.Max(dA, dB);
    }

    public static Primitive CreateParallelogram
    (
        Vector3 position, Vector3 v1, Vector3 v2,
        Primitive stretch, PrimitiveFlags flags, Color color)
    {
        float a = (v1 + v2).magnitude;
        float b = (v1 - v2).magnitude;

        // Basis from the unit diagonals: same rotation as Quaternion.LookRotation(normal, (v1 - v2).normalized);
        // (cross(v1 - v2, v1 + v2) = 2 * cross(v1, v2)) but never degenerate. cross(v1, v2)
        // underflows Unity's 1e-5 normalize threshold for thin quads in stretch space and
        // LookRotation then silently returns identity.
        Vector3 up = (v1 - v2) / b;
        Vector3 normal = Vector3.Cross(up, (v1 + v2) / a);

        var prim = Primitive.Create(
            PrimitiveType.Quad, flags, position, null, Vector3.one, true, color);

        prim.Transform.SetParent(stretch.Transform, true);
        prim.Transform.localRotation = Quaternion.LookRotation(normal, up);
        prim.Transform.localScale = new Vector3(a, b, 1f);
        return prim;
    }

    /// <summary>
    ///     Finds the best existing stretch that can render the parallelogram within
    ///     tolerance. Stretches near the parallelogram's own (theta, phi) solution are
    ///     checked first; on a miss, ALL stretches are scanned - a parallelogram has a
    ///     whole curve of valid (theta, phi) decompositions, so a stretch far from this
    ///     particular solution point can still render it within tolerance.
    /// </summary>
    public static StretchSpatialIndex.Entry? FindBestStretch
    (
        StretchSpatialIndex stretches,
        Vector3 vLeft, Vector3 vUp,
        float theta, float phi,
        float toleranceUnits)
    {
        StretchSpatialIndex.Entry? best = null;
        var bestErr = float.MaxValue;

        foreach (StretchSpatialIndex.Entry entry in stretches.QueryNearby(theta, phi))
        {
            float err = MaxVertexError(vLeft, vUp, entry.Theta, entry.Phi);

            if (err <= toleranceUnits && err < bestErr)
            {
                bestErr = err;
                best = entry;
            }
        }

        if (best != null)
            return best;

        foreach (StretchSpatialIndex.Entry entry in stretches.All())
        {
            float err = MaxVertexError(vLeft, vUp, entry.Theta, entry.Phi);

            if (err <= toleranceUnits && err < bestErr)
            {
                bestErr = err;
                best = entry;
            }
        }

        return best;
    }

    /// <summary>
    ///     Re-parents an existing quad onto another stretch, rebuilding the local
    ///     rotation and scale for the new stretch space. The quad's pivot is the
    ///     parallelogram center, so its world position is preserved.
    /// </summary>
    public static void ReparentToStretch(Primitive quad, Primitive stretch, Vector3 v1, Vector3 v2)
    {
        float a = (v1 + v2).magnitude;
        float b = (v1 - v2).magnitude;
        Vector3 up = (v1 - v2) / b;
        Vector3 normal = Vector3.Cross(up, (v1 + v2) / a);

        quad.Transform.SetParent(stretch.Transform, true);
        quad.Transform.localRotation = Quaternion.LookRotation(normal, up);
        quad.Transform.localScale = new Vector3(a, b, 1f);
    }

    /// <summary>
    ///     Tries to empty sparsely-used stretches by rehoming all of their child quads
    ///     onto other stretches within tolerance, smallest stretch first. Build order
    ///     creates such stretches: an early parallelogram spawns its own stretch before
    ///     a later, more popular one exists that would have fit it too. Each fully
    ///     drained stretch is one primitive saved. Returns the emptied stretches.
    /// </summary>
    /// <param name="stretches">The stretch index.</param>
    /// <param name="quadCount">Total quad count (indices 0..quadCount-1).</param>
    /// <param name="stretchOf">The stretch a quad is parented to, or null (rectangles, reparented quads).</param>
    /// <param name="diagonalsOf">The quad's original world-space half-diagonals.</param>
    /// <param name="rehome">Moves a quad onto the target stretch and updates bookkeeping.</param>
    /// <param name="toleranceUnits">Max world-space vertex error.</param>
    public static HashSet<Primitive> ConsolidateStretches
    (
        StretchSpatialIndex stretches,
        int quadCount,
        Func<int, Primitive?> stretchOf,
        Func<int, (Vector3 vLeft, Vector3 vUp)> diagonalsOf,
        Action<int, StretchSpatialIndex.Entry> rehome,
        float toleranceUnits)
    {
        var emptied = new HashSet<Primitive>();
        var entries = new List<StretchSpatialIndex.Entry>(stretches.All());

        if (entries.Count < 2)
            return emptied;

        var childrenOf = new Dictionary<Primitive, List<int>>();

        for (var i = 0; i < quadCount; i++)
        {
            Primitive? stretch = stretchOf(i);
            if (stretch == null) continue;

            if (!childrenOf.TryGetValue(stretch, out List<int>? list))
            {
                list = new List<int>(4);
                childrenOf[stretch] = list;
            }

            list.Add(i);
        }

        int ChildCount(StretchSpatialIndex.Entry e)
            => childrenOf.TryGetValue(e.Stretch, out List<int>? l) ? l.Count : 0;

        // Drain the least-used stretches first - cheapest wins, and their children
        // land on popular stretches, making those even better targets.
        entries.Sort((a, b) => ChildCount(a).CompareTo(ChildCount(b)));

        foreach (StretchSpatialIndex.Entry entry in entries)
        {
            if (!childrenOf.TryGetValue(entry.Stretch, out List<int>? kids) || kids.Count == 0)
            {
                emptied.Add(entry.Stretch);
                continue;
            }

            var moves = new List<(int kid, StretchSpatialIndex.Entry target)>(kids.Count);
            var allRehomable = true;

            foreach (int kid in kids)
            {
                (Vector3 vLeft, Vector3 vUp) = diagonalsOf(kid);
                StretchSpatialIndex.Entry? best = null;
                var bestErr = float.MaxValue;

                foreach (StretchSpatialIndex.Entry other in entries)
                {
                    if (ReferenceEquals(other.Stretch, entry.Stretch)) continue;
                    if (emptied.Contains(other.Stretch)) continue;

                    // Only stretches that keep other children are useful targets -
                    // moving everything onto an otherwise-empty stretch saves nothing.
                    if (ChildCount(other) == 0) continue;

                    float err = MaxVertexError(vLeft, vUp, other.Theta, other.Phi);

                    if (err <= toleranceUnits && err < bestErr)
                    {
                        bestErr = err;
                        best = other;
                    }
                }

                if (best == null)
                {
                    allRehomable = false;
                    break;
                }

                moves.Add((kid, best.Value));
            }

            if (!allRehomable) continue;

            foreach ((int kid, StretchSpatialIndex.Entry target) in moves)
            {
                rehome(kid, target);
                childrenOf[target.Stretch].Add(kid);
            }

            kids.Clear();
            emptied.Add(entry.Stretch);
        }

        return emptied;
    }
}