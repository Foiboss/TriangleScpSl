using System.Diagnostics;
using MEC;
using Exiled.API.Features;
using TriangleScpSl.Core.Decomposition.NGonDecomposition.Detectors;
using TriangleScpSl.Core.Decomposition.NGonDecomposition.Geometry;
using TriangleScpSl.Core.Decomposition.NGonDecomposition.Parsing;
using UnityEngine;

namespace TriangleScpSl.Core.Decomposition.NGonDecomposition;

public static class PrimitiveShapeDetector
{
    const float VertexMergeEps = 1e-5f;

    // Re-cluster and retry after each round of consumption.
    // Composite clusters usually resolve fully within 2-3 rounds.
    const int MaxDetectionIterations = 3;

    // Relative concavity tolerance for convex-piece splitting: faces stay in the
    // same piece when each face's centroid is at most this fraction of the centroid
    // distance in front of the other face's plane (tolerates mild mesh noise).
    const float ConvexSplitTolerance = 0.01f;

    /// <summary>
    ///     Coroutine version of Detect that yields periodically to avoid freezing.
    /// </summary>
    public static IEnumerator<float> DetectCoroutine
    (
        List<NGonRaw> faces,
        NGonModelConfig config,
        ModelSolidVolume? solid,
        float maxMsPerFrame,
        Action<List<ModelPrimitive>, List<NGonRaw>> onComplete)
    {
        if (faces.Count < 3)
        {
            onComplete([], faces);
            yield break;
        }

        var sw = Stopwatch.StartNew();
        var detected = new List<ModelPrimitive>();
        List<NGonRaw> remaining = faces;

        // Detection is iterative: consuming a primitive shrinks its cluster, and the
        // remaining faces re-clustered often form clean shapes (e.g. a sphere welded
        // to a box: pass 1b extracts the sphere, the next iteration detects the box).
        for (var iteration = 0; iteration < MaxDetectionIterations; iteration++)
        {
            var ctx = new DetectionContext(remaining, config, solid);

            if (sw.Elapsed.TotalMilliseconds >= maxMsPerFrame)
            {
                yield return Timing.WaitForOneFrame;
                sw.Restart();
            }

            // Pass 1: Exact primitive detection on whole clusters
            foreach (List<int> cluster in ctx.SortedClusters)
            {
                ctx.TryDetectExact(cluster, null);

                if (sw.Elapsed.TotalMilliseconds >= maxMsPerFrame)
                {
                    yield return Timing.WaitForOneFrame;
                    sw.Restart();
                }
            }

            // Pass 1b: Sub-cluster splitting - retry failed clusters by splitting on normal similarity
            foreach (List<int> subCluster in ctx.CollectSmoothSubClusters())
            {
                ctx.TryDetectExact(subCluster, "via sub-cluster split");

                if (sw.Elapsed.TotalMilliseconds >= maxMsPerFrame)
                {
                    yield return Timing.WaitForOneFrame;
                    sw.Restart();
                }
            }

            // Pass 1c: Convex-piece splitting - composite blocky clusters (e.g. two
            // stacked boxes) never split on smoothness, but they do split at concave
            // edges into convex pieces that the detectors can fit.
            foreach (List<int> piece in ctx.CollectConvexSubClusters())
            {
                ctx.TryDetectExact(piece, "via convex split");
                ctx.TryDetectPartialBox(piece, "via convex split");

                if (sw.Elapsed.TotalMilliseconds >= maxMsPerFrame)
                {
                    yield return Timing.WaitForOneFrame;
                    sw.Restart();
                }
            }

            // Pass 2: Approximate sphere/cylinder fit with relaxed tolerance
            if (solid != null)
            {
                foreach (List<int> cluster in ctx.SortedClusters)
                {
                    ctx.TryDetectApproximate(cluster);

                    if (sw.Elapsed.TotalMilliseconds >= maxMsPerFrame)
                    {
                        yield return Timing.WaitForOneFrame;
                        sw.Restart();
                    }
                }
            }

            // Pass 3: Partial box detection (visible box faces with hidden faces inside solid)
            if (solid != null)
            {
                foreach (List<int> cluster in ctx.SortedClusters)
                {
                    ctx.TryDetectPartialBox(cluster, null);

                    if (sw.Elapsed.TotalMilliseconds >= maxMsPerFrame)
                    {
                        yield return Timing.WaitForOneFrame;
                        sw.Restart();
                    }
                }
            }

            detected.AddRange(ctx.Detected);
            remaining = ctx.BuildRemaining();

            if (ctx.Detected.Count == 0 || remaining.Count < 2)
                break;
        }

        // Faces fully embedded inside a detected primitive are covered by its opaque
        // convex volume and can never be seen - drop them instead of rendering them.
        int culled = CullFacesHiddenInsidePrimitives(remaining, detected, config.SurfaceDepthThreshold);

        if (culled > 0)
            Log.Info($"PrimitiveShapeDetector: Culled {culled} faces hidden inside detected primitives");

        if (detected.Count > 0)
        {
            Log.Info($"PrimitiveShapeDetector: {detected.Count} primitives detected, " +
                $"{remaining.Count}/{faces.Count} faces remaining");
        }

        onComplete(detected, remaining);
    }

    /// <summary>
    ///     Splits a cluster into smooth connected sub-clusters based on normal similarity.
    ///     Faces that are edge-adjacent AND have similar normals stay in the same sub-cluster.
    ///     This separates e.g. a sphere that shares edges with a flat wall of the same color.
    /// </summary>
    static List<List<int>> ExtractSmoothSubClusters
    (
        List<int> clusterFaceIndices,
        List<NGonRaw> allFaces,
        int[][] faceIdx,
        Dictionary<long, List<int>> edgeMap)
    {
        int count = clusterFaceIndices.Count;

        // Compute normals for faces in this cluster
        var normals = new Vector3[count];
        var clusterSet = new HashSet<int>(clusterFaceIndices);

        for (var i = 0; i < count; i++)
        {
            int fi = clusterFaceIndices[i];
            List<Vector3> verts = allFaces[fi].Vertices;

            if (verts.Count >= 3)
            {
                Vector3 n = NGonMath.NewellNormal(verts);
                float mag = n.magnitude;
                normals[i] = mag > 1e-10f ? n / mag : Vector3.up;
            }
            else
            {
                normals[i] = Vector3.up;
            }
        }

        // Map face index → local index
        var faceToLocal = new Dictionary<int, int>(count);

        for (var i = 0; i < count; i++)
        {
            faceToLocal[clusterFaceIndices[i]] = i;
        }

        // Adaptive normal threshold: for clusters with many faces, use a stricter
        // threshold to separate curved from flat. For small clusters, be more lenient.
        float normalThreshold = count >= 24 ? 0.25f : 0.40f;
        float cosThreshold = Mathf.Cos(normalThreshold);

        // Union-Find within this cluster, only joining smooth-adjacent pairs
        var subParent = new int[count];
        var subSize = new int[count];

        for (var i = 0; i < count; i++)
        {
            subParent[i] = i;
            subSize[i] = 1;
        }

        // Check adjacency via the existing edge map
        for (var i = 0; i < count; i++)
        {
            int fi = clusterFaceIndices[i];
            int[] idx = faceIdx[fi];
            int n = idx.Length;

            for (var e = 0; e < n; e++)
            {
                int a = idx[e];
                int b = idx[(e + 1) % n];
                long key = NGonMath.EdgeKey(a, b);

                if (!edgeMap.TryGetValue(key, out List<int>? facesOnEdge))
                    continue;

                foreach (int fj in facesOnEdge)
                {
                    if (fj == fi) continue;
                    if (!clusterSet.Contains(fj)) continue;

                    if (!faceToLocal.TryGetValue(fj, out int j))
                        continue;

                    // Only join if normals are similar
                    float dot = Vector3.Dot(normals[i], normals[j]);

                    if (dot >= cosThreshold)
                        NGonMath.Union(subParent, subSize, i, j);
                }
            }
        }

        // Extract connected components
        var components = new Dictionary<int, List<int>>();

        for (var i = 0; i < count; i++)
        {
            int root = NGonMath.Find(subParent, i);

            if (!components.TryGetValue(root, out List<int>? list))
            {
                list = new List<int>();
                components[root] = list;
            }

            list.Add(clusterFaceIndices[i]); // Store original face indices
        }

        var result = new List<List<int>>(components.Count);

        foreach (KeyValuePair<int, List<int>> kv in components)
            result.Add(kv.Value);

        return result;
    }

    /// <summary>
    ///     Checks whether any unconsumed, non-cluster face has a vertex that is inside
    ///     the primitive but near its surface. Such vertices were visible before the
    ///     approximation and would be hidden by the smooth primitive - so reject.
    ///     Vertices deep inside are allowed: they were already occluded by the original
    ///     mesh faces before approximation.
    /// </summary>
    static bool HasForeignVerticesInsidePrimitive
    (
        ModelPrimitive primitive,
        List<int> clusterFaceIndices,
        List<NGonRaw> faces,
        bool[] consumed,
        int faceCount,
        float surfaceDepthThreshold)
    {
        var clusterSet = new HashSet<int>(clusterFaceIndices);

        // Pre-compute inverse rotation once (local space transform)
        Quaternion invRot = Quaternion.Inverse(primitive.Rotation);

        for (var f = 0; f < faceCount; f++)
        {
            if (consumed[f] || clusterSet.Contains(f)) continue;

            List<Vector3> verts = faces[f].Vertices;
            if (verts.Count < 3) continue;

            foreach (Vector3 v in verts)
            {
                if (IsPointNearSurfaceInside(v, primitive, invRot, surfaceDepthThreshold))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Returns true if the point is inside the primitive AND near its surface.
    ///     "Near surface" means the normalized depth (0 = surface, 1 = center) is
    ///     below the given threshold.
    ///     Points outside or deep inside return false.
    /// </summary>
    static bool IsPointNearSurfaceInside(Vector3 point, ModelPrimitive prim, Quaternion invRot, float threshold)
        => TryGetInsideDepth(point, prim, invRot, true, out float depth) && depth < threshold;

    /// <summary>
    ///     Removes remaining faces that lie entirely inside one of the detected
    ///     primitives. The primitives are opaque convex solids, so a face whose
    ///     vertices are all inside the same primitive is fully contained (convexity)
    ///     and can never be seen - rendering it would only waste primitives.
    ///     The depth threshold keeps faces lying exactly ON a primitive's surface
    ///     (decals, touching geometry) alive.
    /// </summary>
    static int CullFacesHiddenInsidePrimitives
    (
        List<NGonRaw> remaining,
        List<ModelPrimitive> detected,
        float surfaceDepthThreshold)
    {
        if (detected.Count == 0 || remaining.Count == 0)
            return 0;

        // Never cull at depth 0 even when foreign-vertex rejection is disabled -
        // surface decals must survive.
        float threshold = Mathf.Max(surfaceDepthThreshold, 0.02f);

        var invRots = new Quaternion[detected.Count];

        for (var p = 0; p < detected.Count; p++)
        {
            invRots[p] = Quaternion.Inverse(detected[p].Rotation);
        }

        return remaining.RemoveAll(face =>
        {
            List<Vector3> verts = face.Vertices;
            if (verts.Count < 3) return false;

            for (var p = 0; p < detected.Count; p++)
            {
                var allInside = true;

                foreach (Vector3 v in verts)
                {
                    // Cap depth included so decals on cylinder caps survive too.
                    if (!TryGetInsideDepth(v, detected[p], invRots[p], false, out float depth) ||
                        depth < threshold)
                    {
                        allInside = false;
                        break;
                    }
                }

                if (allInside)
                    return true;
            }

            return false;
        });
    }

    /// <summary>
    ///     Computes the normalized depth (0 = surface, 1 = center/axis) of a point
    ///     inside the primitive. Returns false when the point is outside.
    ///     With <paramref name="cylinderLateralOnly" /> the cylinder depth is measured
    ///     from the lateral (radial) surface only - the primitive adds solid caps that
    ///     the original mesh didn't have, so vertices near caps were already inside
    ///     before approximation. Without it, cap distance is included.
    /// </summary>
    static bool TryGetInsideDepth(Vector3 point, ModelPrimitive prim, Quaternion invRot, bool cylinderLateralOnly, out float depth)
    {
        depth = 0f;

        // Transform point into primitive's local space
        Vector3 local = invRot * (point - prim.Center);

        switch (prim.Type)
        {
            case PrimitiveType.Sphere:
            {
                float rx = prim.Scale.x * 0.5f;
                float ry = prim.Scale.y * 0.5f;
                float rz = prim.Scale.z * 0.5f;

                if (rx < 1e-6f || ry < 1e-6f || rz < 1e-6f) return false;

                float nx = local.x / rx;
                float ny = local.y / ry;
                float nz = local.z / rz;
                float normDistSq = nx * nx + ny * ny + nz * nz;

                // Outside the sphere
                if (normDistSq >= 1f) return false;

                // Normalized depth: 0 at surface, 1 at center
                depth = 1f - Mathf.Sqrt(normDistSq);
                return true;
            }

            case PrimitiveType.Cylinder:
            {
                float radius = prim.Scale.x * 0.5f;
                float halfHeight = prim.Scale.y; // Scale.y IS half-height

                if (radius < 1e-6f || halfHeight < 1e-6f) return false;

                // Outside height bounds - not inside the cylinder at all
                if (Mathf.Abs(local.y) >= halfHeight) return false;

                float radialDist = Mathf.Sqrt(local.x * local.x + local.z * local.z);

                // Outside radial bounds
                if (radialDist >= radius) return false;

                depth = (radius - radialDist) / radius;

                if (!cylinderLateralOnly)
                    depth = Mathf.Min(depth, (halfHeight - Mathf.Abs(local.y)) / halfHeight);

                return true;
            }

            case PrimitiveType.Cube:
            {
                float hx = prim.Scale.x * 0.5f;
                float hy = prim.Scale.y * 0.5f;
                float hz = prim.Scale.z * 0.5f;

                if (hx < 1e-6f || hy < 1e-6f || hz < 1e-6f) return false;

                float dx = hx - Mathf.Abs(local.x);
                float dy = hy - Mathf.Abs(local.y);
                float dz = hz - Mathf.Abs(local.z);

                // Outside the box
                if (dx <= 0f || dy <= 0f || dz <= 0f) return false;

                // Normalized depth from closest face
                float depthX = dx / hx;
                float depthY = dy / hy;
                float depthZ = dz / hz;
                depth = Mathf.Min(depthX, Mathf.Min(depthY, depthZ));
                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>
    ///     Shared state for one detection run: interned vertices, edge adjacency,
    ///     color clusters, and the consumed/detected bookkeeping used by all passes.
    /// </summary>
    sealed class DetectionContext
    {
        readonly List<NGonRaw> _faces;
        readonly NGonModelConfig _config;
        readonly ModelSolidVolume? _solid;
        readonly int[][] _faceIdx;
        readonly List<Vector3> _table;
        readonly Dictionary<long, List<int>> _edgeMap;
        readonly bool[] _consumed;

        public DetectionContext(List<NGonRaw> faces, NGonModelConfig config, ModelSolidVolume? solid)
        {
            _faces = faces;
            _config = config;
            _solid = solid;
            _consumed = new bool[faces.Count];

            // Deduplicate vertices into a shared table (remove near-duplicates)
            var intern = new VertexInternTable(VertexMergeEps);
            _faceIdx = new int[faces.Count][];

            for (var f = 0; f < faces.Count; f++)
            {
                List<Vector3> verts = faces[f].Vertices;
                _faceIdx[f] = new int[verts.Count];

                for (var v = 0; v < verts.Count; v++)
                {
                    _faceIdx[f][v] = intern.Intern(verts[v]);
                }
            }

            _table = intern.Table;
            _edgeMap = BuildEdgeMap();
            SortedClusters = BuildClusters();
        }

        public List<ModelPrimitive> Detected { get; } = [];

        /// <summary>Same-color edge-connected face clusters, biggest first (biggest savings first).</summary>
        public List<List<int>> SortedClusters { get; }

        /// <summary>
        ///     Splits unconsumed clusters into smooth connected sub-clusters worth
        ///     retrying, largest first. Only clusters whose split actually produced
        ///     multiple sub-clusters are included.
        /// </summary>
        public List<List<int>> CollectSmoothSubClusters()
        {
            var subClusters = new List<List<int>>();

            foreach (List<int> cluster in SortedClusters)
            {
                if (cluster.Any(i => _consumed[i])) continue;
                if (cluster.Count < 4) continue; // Need enough faces to split meaningfully

                List<List<int>> subs = ExtractSmoothSubClusters(cluster, _faces, _faceIdx, _edgeMap);

                // Only useful if the split produced multiple sub-clusters
                if (subs.Count <= 1) continue;

                foreach (List<int> sub in subs)
                {
                    if (sub.Count >= 3)
                        subClusters.Add(sub);
                }
            }

            subClusters.Sort((a, b) => b.Count.CompareTo(a.Count));
            return subClusters;
        }

        /// <summary>
        ///     Splits unconsumed clusters at concave edges into convex pieces worth
        ///     retrying, largest first. Smoothness-splitting shatters blocky geometry
        ///     into single faces, but concave seams are exactly where two welded
        ///     convex solids (e.g. stacked boxes) meet.
        /// </summary>
        public List<List<int>> CollectConvexSubClusters()
        {
            var result = new List<List<int>>();

            foreach (List<int> cluster in SortedClusters)
            {
                if (cluster.Any(i => _consumed[i])) continue;
                if (cluster.Count < 4) continue; // Nothing meaningful to split

                List<List<int>> pieces = ExtractConvexPieces(cluster);

                // Only useful if the split produced multiple pieces
                if (pieces.Count <= 1) continue;

                foreach (List<int> piece in pieces)
                {
                    if (piece.Count >= 2) // Partial box detection works from 2 faces
                        result.Add(piece);
                }
            }

            result.Sort((a, b) => b.Count.CompareTo(a.Count));
            return result;
        }

        /// <summary>
        ///     Union-find over the cluster joining faces only across non-concave
        ///     (convex or flat) shared edges. A junction is concave when either
        ///     face's centroid lies in front of the other face's plane.
        /// </summary>
        List<List<int>> ExtractConvexPieces(List<int> clusterFaceIndices)
        {
            int count = clusterFaceIndices.Count;
            var clusterSet = new HashSet<int>(clusterFaceIndices);

            var normals = new Vector3[count];
            var centroids = new Vector3[count];

            for (var i = 0; i < count; i++)
            {
                List<Vector3> verts = _faces[clusterFaceIndices[i]].Vertices;

                Vector3 c = Vector3.zero;
                foreach (Vector3 v in verts) c += v;
                centroids[i] = verts.Count > 0 ? c / verts.Count : Vector3.zero;

                if (verts.Count >= 3)
                {
                    Vector3 n = NGonMath.NewellNormal(verts);
                    float mag = n.magnitude;
                    normals[i] = mag > 1e-10f ? n / mag : Vector3.up;
                }
                else
                {
                    normals[i] = Vector3.up;
                }
            }

            var faceToLocal = new Dictionary<int, int>(count);

            for (var i = 0; i < count; i++)
            {
                faceToLocal[clusterFaceIndices[i]] = i;
            }

            var pieceParent = new int[count];
            var pieceSize = new int[count];

            for (var i = 0; i < count; i++)
            {
                pieceParent[i] = i;
                pieceSize[i] = 1;
            }

            for (var i = 0; i < count; i++)
            {
                int fi = clusterFaceIndices[i];
                int[] idx = _faceIdx[fi];
                int n = idx.Length;

                for (var e = 0; e < n; e++)
                {
                    long key = NGonMath.EdgeKey(idx[e], idx[(e + 1) % n]);

                    if (!_edgeMap.TryGetValue(key, out List<int>? facesOnEdge))
                        continue;

                    foreach (int fj in facesOnEdge)
                    {
                        if (fj == fi) continue;
                        if (!clusterSet.Contains(fj)) continue;
                        if (!faceToLocal.TryGetValue(fj, out int j)) continue;

                        Vector3 between = centroids[j] - centroids[i];
                        float eps = ConvexSplitTolerance * between.magnitude + 1e-6f;

                        // Concave junction: the other face's centroid is in front
                        // of this face's plane (assumes outward normals).
                        bool concave = Vector3.Dot(normals[i], between) > eps ||
                            Vector3.Dot(normals[j], -between) > eps;

                        if (!concave)
                            NGonMath.Union(pieceParent, pieceSize, i, j);
                    }
                }
            }

            var components = new Dictionary<int, List<int>>();

            for (var i = 0; i < count; i++)
            {
                int root = NGonMath.Find(pieceParent, i);

                if (!components.TryGetValue(root, out List<int>? list))
                {
                    list = new List<int>();
                    components[root] = list;
                }

                list.Add(clusterFaceIndices[i]);
            }

            var result = new List<List<int>>(components.Count);

            foreach (KeyValuePair<int, List<int>> kv in components)
                result.Add(kv.Value);

            return result;
        }

        public void TryDetectExact(List<int> cluster, string? note)
        {
            if (cluster.Any(i => _consumed[i])) return;

            (List<NGonRaw> clusterFaces, List<Vector3> uniqueVerts) = Gather(cluster);

            // Try detectors in priority order
            ModelPrimitive? primitive = null;

            if (SphereDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive sphereResult, _config, _solid))
                primitive = sphereResult;
            else if (CylinderDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive cylResult, _config))
                primitive = cylResult;
            else if (CubeDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive cubeResult, _config, _solid))
                primitive = cubeResult;

            if (primitive == null) return;

            if (HasForeignVerticesInsidePrimitive(primitive, cluster, _faces, _consumed, _faces.Count, _config.SurfaceDepthThreshold))
                return;

            string suffix = note == null ? "" : $" {note}";

            Consume(cluster, primitive,
                $"PrimitiveShapeDetector: Detected {primitive.Type}{suffix} " +
                $"({cluster.Count} faces -> 1 primitive)");
        }

        public void TryDetectApproximate(List<int> cluster)
        {
            if (_solid == null) return;
            if (cluster.Count < 6) return;
            if (cluster.Any(i => _consumed[i])) return;

            (List<NGonRaw> clusterFaces, List<Vector3> uniqueVerts) = Gather(cluster);

            ModelPrimitive? primitive = null;

            if (SphereDetector.TryDetectApproximate(clusterFaces, uniqueVerts, _solid,
                out ModelPrimitive sphereResult, _config))
                primitive = sphereResult;
            else if (CylinderDetector.TryDetectApproximate(clusterFaces, uniqueVerts, _solid,
                out ModelPrimitive cylResult, _config))
                primitive = cylResult;

            if (primitive == null) return;

            if (HasForeignVerticesInsidePrimitive(primitive, cluster, _faces, _consumed, _faces.Count, _config.SurfaceDepthThreshold))
                return;

            Consume(cluster, primitive,
                $"PrimitiveShapeDetector: Approximated {primitive.Type} " +
                $"({cluster.Count} faces -> 1 primitive, approx)");
        }

        public void TryDetectPartialBox(List<int> cluster, string? note)
        {
            if (_solid == null) return;
            if (cluster.Count is < 2 or > 48) return;
            if (cluster.Any(i => _consumed[i])) return;

            (List<NGonRaw> clusterFaces, List<Vector3> uniqueVerts) = Gather(cluster);

            if (!CubeDetector.TryDetectPartial(clusterFaces, uniqueVerts, _solid, _config,
                out ModelPrimitive boxResult))
                return;

            if (HasForeignVerticesInsidePrimitive(boxResult, cluster, _faces, _consumed, _faces.Count, _config.SurfaceDepthThreshold))
                return;

            string suffix = note == null ? "" : $" {note}";

            Consume(cluster, boxResult,
                $"PrimitiveShapeDetector: Detected partial {boxResult.Type}{suffix} " +
                $"({cluster.Count} faces -> 1 primitive)");
        }

        public List<NGonRaw> BuildRemaining()
        {
            var remaining = new List<NGonRaw>();

            for (var f = 0; f < _faces.Count; f++)
            {
                if (!_consumed[f])
                    remaining.Add(_faces[f]);
            }

            return remaining;
        }

        Dictionary<long, List<int>> BuildEdgeMap()
        {
            var edgeMap = new Dictionary<long, List<int>>();

            for (var f = 0; f < _faces.Count; f++)
            {
                int[] idx = _faceIdx[f];
                int n = idx.Length;

                for (var i = 0; i < n; i++)
                {
                    long key = NGonMath.EdgeKey(idx[i], idx[(i + 1) % n]);

                    if (!edgeMap.TryGetValue(key, out List<int>? list))
                    {
                        list = new List<int>(2);
                        edgeMap[key] = list;
                    }

                    list.Add(f);
                }
            }

            return edgeMap;
        }

        List<List<int>> BuildClusters()
        {
            int faceCount = _faces.Count;

            // Union-Find: cluster same-color edge-connected faces
            var parent = new int[faceCount];
            var clusterSize = new int[faceCount];

            for (var i = 0; i < faceCount; i++)
            {
                parent[i] = i;
                clusterSize[i] = 1;
            }

            foreach (KeyValuePair<long, List<int>> kv in _edgeMap)
            {
                List<int> facesOnEdge = kv.Value;
                if (facesOnEdge.Count < 2) continue;

                for (var i = 0; i < facesOnEdge.Count; i++)
                {
                    int fa = facesOnEdge[i];

                    for (int j = i + 1; j < facesOnEdge.Count; j++)
                    {
                        int fb = facesOnEdge[j];

                        if (!NGonMath.ColorsClose(_faces[fa].Color, _faces[fb].Color))
                            continue;

                        if (_faces[fa].ObjectGroup >= 0 && _faces[fb].ObjectGroup >= 0 &&
                            _faces[fa].ObjectGroup != _faces[fb].ObjectGroup)
                            continue;

                        NGonMath.Union(parent, clusterSize, fa, fb);
                    }
                }
            }

            // Group faces by cluster root
            var clusters = new Dictionary<int, List<int>>();

            for (var f = 0; f < faceCount; f++)
            {
                int root = NGonMath.Find(parent, f);

                if (!clusters.TryGetValue(root, out List<int>? list))
                {
                    list = new List<int>();
                    clusters[root] = list;
                }

                list.Add(f);
            }

            var sorted = new List<List<int>>(clusters.Values);
            sorted.Sort((a, b) => b.Count.CompareTo(a.Count));
            return sorted;
        }

        (List<NGonRaw> clusterFaces, List<Vector3> uniqueVerts) Gather(List<int> cluster)
        {
            var clusterFaces = new List<NGonRaw>(cluster.Count);
            var vertexSet = new HashSet<int>();

            foreach (int fi in cluster)
            {
                clusterFaces.Add(_faces[fi]);

                foreach (int vi in _faceIdx[fi])
                    vertexSet.Add(vi);
            }

            var uniqueVerts = new List<Vector3>(vertexSet.Count);

            foreach (int vi in vertexSet)
                uniqueVerts.Add(_table[vi]);

            return (clusterFaces, uniqueVerts);
        }

        void Consume(List<int> cluster, ModelPrimitive primitive, string message)
        {
            Detected.Add(primitive);

            foreach (int fi in cluster)
                _consumed[fi] = true;

            Log.Info(message);
        }
    }

    /// <summary>
    ///     Spatial-hash accelerated vertex interning.
    ///     Uses grid cells of size VertexMergeEps to avoid O(n²) linear search.
    /// </summary>
    sealed class VertexInternTable(float eps)
    {
        readonly Dictionary<long, List<int>> _grid = new();
        readonly float _cellSize = Mathf.Max(eps * 2f, 1e-6f);
        readonly float _eps2 = eps * eps;

        public List<Vector3> Table { get; } = [];

        public int Intern(Vector3 v)
        {
            int gx = Mathf.FloorToInt(v.x / _cellSize);
            int gy = Mathf.FloorToInt(v.y / _cellSize);
            int gz = Mathf.FloorToInt(v.z / _cellSize);

            // Check the 3x3x3 neighborhood to handle vertices near cell boundaries
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                long key = CellKey(gx + dx, gy + dy, gz + dz);

                if (!_grid.TryGetValue(key, out List<int>? cell))
                    continue;

                foreach (int idx in cell)
                {
                    if ((Table[idx] - v).sqrMagnitude <= _eps2)
                        return idx;
                }
            }

            int newIdx = Table.Count;
            Table.Add(v);

            long homeKey = CellKey(gx, gy, gz);

            if (!_grid.TryGetValue(homeKey, out List<int>? homeCell))
            {
                homeCell = new List<int>(4);
                _grid[homeKey] = homeCell;
            }

            homeCell.Add(newIdx);
            return newIdx;
        }

        static long CellKey(int x, int y, int z)
        {
            // Pack three ints into a long using hash combination
            unchecked
            {
                long h = x * 73856093L;
                h ^= y * 19349663L;
                h ^= z * 83492791L;
                return h;
            }
        }
    }
}