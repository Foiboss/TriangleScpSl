using Exiled.API.Features;
using Exiled.API.Features.Toys;
using TriangleScpSl.Core.Models.ApproximateModel;
using UnityEngine;

namespace TriangleScpSl.Core.Models.HierarchicalModel;

public partial class HierarchicalModel
{
    /// <summary>
    ///     Iteratively moves stretch-children onto visible quads.
    ///     Sweep 0: full scan - every stretch-child checks every non-stretch-child as parent.
    ///     Sweep 1+: only checks quads that were newly reparented or became parents in the
    ///     previous sweep (their compound transform changed, opening new possibilities).
    ///     Reparenting: SetParent(newParent, true) to preserve world position, then
    ///     override localRotation + localScale + localPosition for precision.
    ///     Nothing is destroyed - only parent pointers change.
    /// </summary>
    void RunOptimizationSweeps()
    {
        if (_optimizationPasses <= 0) return;

        // Seed: only quads that are stretch-free in their ancestry.
        var newCandidates = new HashSet<int>();

        for (var i = 0; i < _parallelograms.Count; i++)
        {
            if (IsStretchFreeInHierarchy(i))
                newCandidates.Add(i);
        }

        for (var pass = 0; pass < _optimizationPasses; pass++)
        {
            bool fullScan = pass == 0;

            var reparentedThisPass = new List<int>();
            var newParentsThisPass = new HashSet<int>();

            for (var ci = 0; ci < _parallelograms.Count; ci++)
            {
                // Only stretch-children are candidates for reparenting.
                if (_hierarchicalParents.ContainsKey(ci)) continue;
                if (_quadBuildInfos[ci].Stretch == null) continue;

                QuadBuildInfo info = _quadBuildInfos[ci];
                Vector3 wc0 = info.Center + info.VUp, wc1 = info.Center + info.VLeft;
                Vector3 wc2 = info.Center - info.VUp, wc3 = info.Center - info.VLeft;

                int bestIdx = -1;
                var bestErr = float.MaxValue;
                Vector3 bestLp = default;
                Quaternion bestLr = default;
                Vector3 bestLs = default;

                for (var pi = 0; pi < _parallelograms.Count; pi++)
                {
                    if (pi == ci) continue;
                    if (!IsStretchFreeInHierarchy(pi)) continue;
                    if (!fullScan && !newCandidates.Contains(pi)) continue;

                    if (TryFitUnderQuad(_parallelograms[pi].Transform, wc0, wc1, wc2, wc3,
                            out Vector3 lp, out Quaternion lr, out Vector3 ls, out float err) && err < bestErr)
                    {
                        bestErr = err;
                        bestIdx = pi;
                        bestLp = lp;
                        bestLr = lr;
                        bestLs = ls;
                    }
                }

                if (bestIdx < 0) continue;

                // Reparent in place - no destroy, no recreate.
                Primitive quad = _parallelograms[ci];
                quad.Transform.SetParent(_parallelograms[bestIdx].Transform, true);
                quad.Transform.localPosition = bestLp;
                quad.Transform.localRotation = bestLr;
                quad.Transform.localScale = bestLs;

                _hierarchicalParents[ci] = bestIdx;
                ReparentedCount++;
                _hierarchyDepths[ci] = (_hierarchyDepths.TryGetValue(bestIdx, out int pd) ? pd : 0) + 1;
                _quadBuildInfos[ci] = new QuadBuildInfo(info.VLeft, info.VUp, info.Center, null);

                reparentedThisPass.Add(ci);
                newParentsThisPass.Add(bestIdx);
            }

            if (reparentedThisPass.Count == 0) break;

            Log.Debug($"[HierarchicalModel] Sweep {pass + 1}: reparented {reparentedThisPass.Count} quads.");

            // Next pass: only test newly-involved stretch-free quads.
            newCandidates = new HashSet<int>();

            foreach (int pi in newParentsThisPass)
                if (IsStretchFreeInHierarchy(pi))
                    newCandidates.Add(pi);

            foreach (int ri in reparentedThisPass)
                if (IsStretchFreeInHierarchy(ri))
                    newCandidates.Add(ri);
        }
    }

    /// <summary>
    ///     Drains sparsely-used stretches by rehoming their remaining quads onto other
    ///     stretches within tolerance. Emptied stretches end up with no children, so
    ///     MarkUsedStretches/DestroyUnusedStretches removes them - one primitive each.
    /// </summary>
    void ConsolidateStretches()
    {
        ApproximateModelUtils.ConsolidateStretches(
            _stretches,
            _parallelograms.Count,
            i => _quadBuildInfos[i].Stretch,
            i => (_quadBuildInfos[i].VLeft, _quadBuildInfos[i].VUp),
            RehomeQuad,
            _absoluteToleranceUnits);
    }

    void RehomeQuad(int index, StretchSpatialIndex.Entry target)
    {
        QuadBuildInfo info = _quadBuildInfos[index];
        Vector3 v1 = ApproximateModelUtils.ForwardTransform(info.VLeft, target.Theta, target.Phi);
        Vector3 v2 = ApproximateModelUtils.ForwardTransform(info.VUp, target.Theta, target.Phi);
        ApproximateModelUtils.ReparentToStretch(_parallelograms[index], target.Stretch, v1, v2);
        _quadBuildInfos[index] = new QuadBuildInfo(info.VLeft, info.VUp, info.Center, target.Stretch);
    }
}