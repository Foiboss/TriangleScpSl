using System.Collections;
using System.Diagnostics;
using Exiled.API.Features;
using TriangleScpSl.Core.Decomposition.NGonDecomposition.Detectors;
using UnityEngine;

namespace TriangleScpSl.Core.Decomposition.NGonDecomposition;

public static class PrimitiveShapeDetector
{
    const float VertexMergeEps = 1e-5f;

    public static (List<ModelPrimitive> detected, List<NGonRaw> remaining) Detect
    (
        List<NGonRaw> faces,
        NGonModelConfig config,
        ModelSolidVolume? solid = null)
    {
        var detected = new List<ModelPrimitive>();

        if (faces.Count < 3)
            return (detected, faces);

        int faceCount = faces.Count;

        // Deduplicate vertices into a shared table (remove near-duplicates)
        var intern = new VertexInternTable(VertexMergeEps);
        var faceIdx = new int[faceCount][];

        for (var f = 0; f < faceCount; f++)
        {
            List<Vector3> verts = faces[f].Vertices;
            faceIdx[f] = new int[verts.Count];

            for (var v = 0; v < verts.Count; v++)
            {
                faceIdx[f][v] = intern.Intern(verts[v]);
            }
        }

        List<Vector3> table = intern.Table;

        // Build edge-to-faces adjacency map
        var edgeMap = new Dictionary<long, List<int>>();

        for (var f = 0; f < faceCount; f++)
        {
            int[] idx = faceIdx[f];
            int n = idx.Length;

            for (var i = 0; i < n; i++)
            {
                int a = idx[i];
                int b = idx[(i + 1) % n];
                long key = NGonMath.EdgeKey(a, b);

                if (!edgeMap.TryGetValue(key, out List<int>? list))
                {
                    list = new List<int>(2);
                    edgeMap[key] = list;
                }

                list.Add(f);
            }
        }

        // Union-Find: cluster same-color edge-connected faces
        var parent = new int[faceCount];
        var clusterSize = new int[faceCount];

        for (var i = 0; i < faceCount; i++)
        {
            parent[i] = i;
            clusterSize[i] = 1;
        }

        foreach (KeyValuePair<long, List<int>> kv in edgeMap)
        {
            List<int> facesOnEdge = kv.Value;
            if (facesOnEdge.Count < 2) continue;

            for (var i = 0; i < facesOnEdge.Count; i++)
            {
                int fa = facesOnEdge[i];

                for (int j = i + 1; j < facesOnEdge.Count; j++)
                {
                    int fb = facesOnEdge[j];

                    if (!NGonMath.ColorsClose(faces[fa].Color, faces[fb].Color))
                        continue;

                    if (faces[fa].ObjectGroup >= 0 && faces[fb].ObjectGroup >= 0 &&
                        faces[fa].ObjectGroup != faces[fb].ObjectGroup)
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

        // Try fitting each cluster to a primitive shape
        var consumed = new bool[faceCount];

        // Sort clusters by size descending (biggest savings first)
        var sortedClusters = new List<KeyValuePair<int, List<int>>>(clusters);
        sortedClusters.Sort((a, b) => b.Value.Count.CompareTo(a.Value.Count));

        // Pass 1: Exact primitive detection
        foreach (KeyValuePair<int, List<int>> kv in sortedClusters)
        {
            List<int> clusterFaceIndices = kv.Value;

            // Collect cluster faces and unique vertices
            var clusterFaces = new List<NGonRaw>(clusterFaceIndices.Count);
            var vertexSet = new HashSet<int>();

            foreach (int fi in clusterFaceIndices)
            {
                clusterFaces.Add(faces[fi]);

                foreach (int vi in faceIdx[fi])
                    vertexSet.Add(vi);
            }

            var uniqueVerts = new List<Vector3>(vertexSet.Count);

            foreach (int vi in vertexSet)
                uniqueVerts.Add(table[vi]);

            // Try detectors in priority order
            ModelPrimitive? primitive = null;

            if (SphereDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive sphereResult, config, solid))
                primitive = sphereResult;
            else if (CylinderDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive cylResult, config))
                primitive = cylResult;
            else if (CubeDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive cubeResult, config, solid))
                primitive = cubeResult;

            if (primitive == null) continue;

            if (HasForeignVerticesInsidePrimitive(primitive, clusterFaceIndices, faces, consumed, faceCount, config.SurfaceDepthThreshold))
                continue;

            detected.Add(primitive);

            foreach (int fi in clusterFaceIndices)
                consumed[fi] = true;

            Log.Info($"PrimitiveShapeDetector: Detected {primitive.Type} " +
                $"({clusterFaceIndices.Count} faces → 1 primitive)");
        }

        // Pass 1b: Sub-cluster splitting - retry failed clusters by splitting on normal similarity
        {
            var subClustersToTry = new List<List<int>>();

            foreach (KeyValuePair<int, List<int>> kv in sortedClusters)
            {
                List<int> clusterFaceIndices = kv.Value;
                if (clusterFaceIndices.Any(i => consumed[i])) continue;
                if (clusterFaceIndices.Count < 4) continue; // Need enough faces to split meaningfully

                List<List<int>> subs = ExtractSmoothSubClusters(
                    clusterFaceIndices, faces, faceIdx, edgeMap);

                // Only useful if the split produced multiple sub-clusters
                if (subs.Count <= 1) continue;

                foreach (List<int> sub in subs)
                {
                    if (sub.Count >= 3)
                        subClustersToTry.Add(sub);
                }
            }

            // Sort sub-clusters largest first
            subClustersToTry.Sort((a, b) => b.Count.CompareTo(a.Count));

            foreach (List<int> subCluster in subClustersToTry)
            {
                if (subCluster.Any(i => consumed[i])) continue;

                var clusterFaces = new List<NGonRaw>(subCluster.Count);
                var vertexSet = new HashSet<int>();

                foreach (int fi in subCluster)
                {
                    clusterFaces.Add(faces[fi]);

                    foreach (int vi in faceIdx[fi])
                        vertexSet.Add(vi);
                }

                var uniqueVerts = new List<Vector3>(vertexSet.Count);

                foreach (int vi in vertexSet)
                    uniqueVerts.Add(table[vi]);

                ModelPrimitive? primitive = null;

                if (SphereDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive sphereResult2, config, solid))
                    primitive = sphereResult2;
                else if (CylinderDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive cylResult2, config))
                    primitive = cylResult2;
                else if (CubeDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive cubeResult2, config, solid))
                    primitive = cubeResult2;

                if (primitive == null) continue;

                if (HasForeignVerticesInsidePrimitive(primitive, subCluster, faces, consumed, faceCount, config.SurfaceDepthThreshold))
                    continue;

                detected.Add(primitive);

                foreach (int fi in subCluster)
                    consumed[fi] = true;

                Log.Info($"PrimitiveShapeDetector: Detected {primitive.Type} via sub-cluster split " +
                    $"({subCluster.Count} faces → 1 primitive)");
            }
        }

        // Pass 2: Approximate sphere/cylinder fit with relaxed tolerance
        if (solid != null)
        {
            foreach (KeyValuePair<int, List<int>> kv in sortedClusters)
            {
                List<int> clusterFaceIndices = kv.Value;
                if (clusterFaceIndices.Any(i => consumed[i])) continue;
                if (clusterFaceIndices.Count < 6) continue;

                var clusterFaces = new List<NGonRaw>(clusterFaceIndices.Count);
                var vertexSet = new HashSet<int>();

                foreach (int fi in clusterFaceIndices)
                {
                    clusterFaces.Add(faces[fi]);

                    foreach (int vi in faceIdx[fi])
                        vertexSet.Add(vi);
                }

                var uniqueVerts = new List<Vector3>(vertexSet.Count);

                foreach (int vi in vertexSet)
                    uniqueVerts.Add(table[vi]);

                ModelPrimitive? approxPrimitive = null;

                if (SphereDetector.TryDetectApproximate(clusterFaces, uniqueVerts, solid,
                    out ModelPrimitive approxResult, config))
                    approxPrimitive = approxResult;
                else if (CylinderDetector.TryDetectApproximate(clusterFaces, uniqueVerts, solid,
                    out ModelPrimitive cylApproxResult, config))
                    approxPrimitive = cylApproxResult;

                if (approxPrimitive != null &&
                    !HasForeignVerticesInsidePrimitive(approxPrimitive, clusterFaceIndices, faces, consumed, faceCount, config.SurfaceDepthThreshold))
                {
                    detected.Add(approxPrimitive);

                    foreach (int fi in clusterFaceIndices)
                        consumed[fi] = true;

                    Log.Info($"PrimitiveShapeDetector: Approximated {approxPrimitive.Type} " +
                        $"({clusterFaceIndices.Count} faces → 1 primitive, approx)");
                }
            }
        }

        // Pass 3: Partial box detection (3-5 visible faces with hidden faces inside solid)
        if (solid != null)
        {
            foreach (KeyValuePair<int, List<int>> kv in sortedClusters)
            {
                List<int> clusterFaceIndices = kv.Value;
                if (clusterFaceIndices.Any(i => consumed[i])) continue;
                if (clusterFaceIndices.Count is < 3 or > 20) continue;

                var clusterFaces = new List<NGonRaw>(clusterFaceIndices.Count);
                var vertexSet = new HashSet<int>();

                foreach (int fi in clusterFaceIndices)
                {
                    clusterFaces.Add(faces[fi]);

                    foreach (int vi in faceIdx[fi])
                        vertexSet.Add(vi);
                }

                var uniqueVerts = new List<Vector3>(vertexSet.Count);

                foreach (int vi in vertexSet)
                    uniqueVerts.Add(table[vi]);

                if (CubeDetector.TryDetectPartial(clusterFaces, uniqueVerts, solid, config,
                    out ModelPrimitive boxResult))
                {
                    detected.Add(boxResult);

                    foreach (int fi in clusterFaceIndices)
                        consumed[fi] = true;

                    Log.Info($"PrimitiveShapeDetector: Detected partial {boxResult.Type} " +
                        $"({clusterFaceIndices.Count} faces → 1 primitive)");
                }
            }
        }

        // Build remaining faces list
        var remaining = new List<NGonRaw>();

        for (var f = 0; f < faceCount; f++)
        {
            if (!consumed[f])
                remaining.Add(faces[f]);
        }

        if (detected.Count > 0)
        {
            Log.Info($"PrimitiveShapeDetector: {detected.Count} primitives detected, " +
                $"{remaining.Count}/{faceCount} faces remaining");
        }

        return (detected, remaining);
    }

    /// <summary>
    ///     Coroutine version of Detect that yields periodically to avoid freezing.
    /// </summary>
    public static IEnumerator DetectCoroutine
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

        int faceCount = faces.Count;

        var intern = new VertexInternTable(VertexMergeEps);
        var faceIdx = new int[faceCount][];

        for (var f = 0; f < faceCount; f++)
        {
            List<Vector3> verts = faces[f].Vertices;
            faceIdx[f] = new int[verts.Count];

            for (var v = 0; v < verts.Count; v++)
            {
                faceIdx[f][v] = intern.Intern(verts[v]);
            }
        }

        List<Vector3> table = intern.Table;

        var edgeMap = new Dictionary<long, List<int>>();

        for (var f = 0; f < faceCount; f++)
        {
            int[] idx = faceIdx[f];
            int n = idx.Length;

            for (var i = 0; i < n; i++)
            {
                int a = idx[i];
                int b = idx[(i + 1) % n];
                long key = NGonMath.EdgeKey(a, b);

                if (!edgeMap.TryGetValue(key, out List<int>? list))
                {
                    list = new List<int>(2);
                    edgeMap[key] = list;
                }

                list.Add(f);
            }
        }

        var parent = new int[faceCount];
        var clusterSize = new int[faceCount];

        for (var i = 0; i < faceCount; i++)
        {
            parent[i] = i;
            clusterSize[i] = 1;
        }

        foreach (KeyValuePair<long, List<int>> kv in edgeMap)
        {
            List<int> facesOnEdge = kv.Value;
            if (facesOnEdge.Count < 2) continue;

            for (var i = 0; i < facesOnEdge.Count; i++)
            {
                int fa = facesOnEdge[i];

                for (int j = i + 1; j < facesOnEdge.Count; j++)
                {
                    int fb = facesOnEdge[j];

                    if (!NGonMath.ColorsClose(faces[fa].Color, faces[fb].Color))
                        continue;

                    if (faces[fa].ObjectGroup >= 0 && faces[fb].ObjectGroup >= 0 &&
                        faces[fa].ObjectGroup != faces[fb].ObjectGroup)
                        continue;

                    NGonMath.Union(parent, clusterSize, fa, fb);
                }
            }
        }

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

        var consumed = new bool[faceCount];
        var detected = new List<ModelPrimitive>();

        var sortedClusters = new List<KeyValuePair<int, List<int>>>(clusters);
        sortedClusters.Sort((a, b) => b.Value.Count.CompareTo(a.Value.Count));

        if (sw.Elapsed.TotalMilliseconds >= maxMsPerFrame)
        {
            yield return null;
            sw.Restart();
        }

        // Pass 1: Exact primitive detection
        foreach (KeyValuePair<int, List<int>> kv in sortedClusters)
        {
            List<int> clusterFaceIndices = kv.Value;

            var clusterFaces = new List<NGonRaw>(clusterFaceIndices.Count);
            var vertexSet = new HashSet<int>();

            foreach (int fi in clusterFaceIndices)
            {
                clusterFaces.Add(faces[fi]);

                foreach (int vi in faceIdx[fi])
                    vertexSet.Add(vi);
            }

            var uniqueVerts = new List<Vector3>(vertexSet.Count);

            foreach (int vi in vertexSet)
                uniqueVerts.Add(table[vi]);

            ModelPrimitive? primitive = null;

            if (SphereDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive sphereResult, config, solid))
                primitive = sphereResult;
            else if (CylinderDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive cylResult, config))
                primitive = cylResult;
            else if (CubeDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive cubeResult, config, solid))
                primitive = cubeResult;

            if (primitive != null &&
                !HasForeignVerticesInsidePrimitive(primitive, clusterFaceIndices, faces, consumed, faceCount, config.SurfaceDepthThreshold))
            {
                detected.Add(primitive);

                foreach (int fi in clusterFaceIndices)
                    consumed[fi] = true;

                Log.Info($"PrimitiveShapeDetector: Detected {primitive.Type} " +
                    $"({clusterFaceIndices.Count} faces -> 1 primitive)");
            }

            if (sw.Elapsed.TotalMilliseconds >= maxMsPerFrame)
            {
                yield return null;
                sw.Restart();
            }
        }

        // Pass 1b: Sub-cluster splitting
        {
            var subClustersToTry = new List<List<int>>();

            foreach (KeyValuePair<int, List<int>> kv in sortedClusters)
            {
                List<int> clusterFaceIndices = kv.Value;
                if (clusterFaceIndices.Any(i => consumed[i])) continue;
                if (clusterFaceIndices.Count < 4) continue;

                List<List<int>> subs = ExtractSmoothSubClusters(
                    clusterFaceIndices, faces, faceIdx, edgeMap);

                if (subs.Count <= 1) continue;

                foreach (List<int> sub in subs)
                {
                    if (sub.Count >= 3)
                        subClustersToTry.Add(sub);
                }
            }

            subClustersToTry.Sort((a, b) => b.Count.CompareTo(a.Count));

            foreach (List<int> subCluster in subClustersToTry)
            {
                if (subCluster.Any(i => consumed[i])) continue;

                var clusterFaces = new List<NGonRaw>(subCluster.Count);
                var vertexSet = new HashSet<int>();

                foreach (int fi in subCluster)
                {
                    clusterFaces.Add(faces[fi]);

                    foreach (int vi in faceIdx[fi])
                        vertexSet.Add(vi);
                }

                var uniqueVerts = new List<Vector3>(vertexSet.Count);

                foreach (int vi in vertexSet)
                    uniqueVerts.Add(table[vi]);

                ModelPrimitive? primitive = null;

                if (SphereDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive sphereResult2, config, solid))
                    primitive = sphereResult2;
                else if (CylinderDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive cylResult2, config))
                    primitive = cylResult2;
                else if (CubeDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive cubeResult2, config, solid))
                    primitive = cubeResult2;

                if (primitive != null &&
                    !HasForeignVerticesInsidePrimitive(primitive, subCluster, faces, consumed, faceCount, config.SurfaceDepthThreshold))
                {
                    detected.Add(primitive);

                    foreach (int fi in subCluster)
                        consumed[fi] = true;

                    Log.Info($"PrimitiveShapeDetector: Detected {primitive.Type} via sub-cluster split " +
                        $"({subCluster.Count} faces -> 1 primitive)");
                }

                if (sw.Elapsed.TotalMilliseconds >= maxMsPerFrame)
                {
                    yield return null;
                    sw.Restart();
                }
            }
        }

        // Pass 2: Approximate sphere/cylinder fit
        if (solid != null)
        {
            foreach (KeyValuePair<int, List<int>> kv in sortedClusters)
            {
                List<int> clusterFaceIndices = kv.Value;
                if (clusterFaceIndices.Any(i => consumed[i])) continue;
                if (clusterFaceIndices.Count < 6) continue;

                var clusterFaces = new List<NGonRaw>(clusterFaceIndices.Count);
                var vertexSet = new HashSet<int>();

                foreach (int fi in clusterFaceIndices)
                {
                    clusterFaces.Add(faces[fi]);

                    foreach (int vi in faceIdx[fi])
                        vertexSet.Add(vi);
                }

                var uniqueVerts = new List<Vector3>(vertexSet.Count);

                foreach (int vi in vertexSet)
                    uniqueVerts.Add(table[vi]);

                ModelPrimitive? approxPrimitive = null;

                if (SphereDetector.TryDetectApproximate(clusterFaces, uniqueVerts, solid,
                    out ModelPrimitive approxResult, config))
                    approxPrimitive = approxResult;
                else if (CylinderDetector.TryDetectApproximate(clusterFaces, uniqueVerts, solid,
                    out ModelPrimitive cylApproxResult, config))
                    approxPrimitive = cylApproxResult;

                if (approxPrimitive != null &&
                    !HasForeignVerticesInsidePrimitive(approxPrimitive, clusterFaceIndices, faces, consumed, faceCount, config.SurfaceDepthThreshold))
                {
                    detected.Add(approxPrimitive);

                    foreach (int fi in clusterFaceIndices)
                        consumed[fi] = true;

                    Log.Info($"PrimitiveShapeDetector: Approximated {approxPrimitive.Type} " +
                        $"({clusterFaceIndices.Count} faces -> 1 primitive, approx)");
                }

                if (sw.Elapsed.TotalMilliseconds >= maxMsPerFrame)
                {
                    yield return null;
                    sw.Restart();
                }
            }
        }

        // Pass 3: Partial box detection
        if (solid != null)
        {
            foreach (KeyValuePair<int, List<int>> kv in sortedClusters)
            {
                List<int> clusterFaceIndices = kv.Value;
                if (clusterFaceIndices.Any(i => consumed[i])) continue;
                if (clusterFaceIndices.Count < 3 || clusterFaceIndices.Count > 20) continue;

                var clusterFaces = new List<NGonRaw>(clusterFaceIndices.Count);
                var vertexSet = new HashSet<int>();

                foreach (int fi in clusterFaceIndices)
                {
                    clusterFaces.Add(faces[fi]);

                    foreach (int vi in faceIdx[fi])
                        vertexSet.Add(vi);
                }

                var uniqueVerts = new List<Vector3>(vertexSet.Count);

                foreach (int vi in vertexSet)
                    uniqueVerts.Add(table[vi]);

                if (CubeDetector.TryDetectPartial(clusterFaces, uniqueVerts, solid, config,
                    out ModelPrimitive boxResult))
                {
                    detected.Add(boxResult);

                    foreach (int fi in clusterFaceIndices)
                        consumed[fi] = true;

                    Log.Info($"PrimitiveShapeDetector: Detected partial {boxResult.Type} " +
                        $"({clusterFaceIndices.Count} faces -> 1 primitive)");
                }

                if (sw.Elapsed.TotalMilliseconds >= maxMsPerFrame)
                {
                    yield return null;
                    sw.Restart();
                }
            }
        }

        var remaining = new List<NGonRaw>();

        for (var f = 0; f < faceCount; f++)
        {
            if (!consumed[f])
                remaining.Add(faces[f]);
        }

        if (detected.Count > 0)
        {
            Log.Info($"PrimitiveShapeDetector: {detected.Count} primitives detected, " +
                $"{remaining.Count}/{faceCount} faces remaining");
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
    ///     approximation and would be hidden by the smooth primitive — so reject.
    ///     Vertices deep inside are allowed: they were already occluded by the original
    ///     mesh faces before approximation.
    /// </summary>
    /// 
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
    ///     For cylinders, only the lateral (radial) surface is checked — the primitive
    ///     adds solid caps that the original mesh didn't have, so vertices near caps
    ///     were already inside before approximation.
    /// </summary>
    static bool IsPointNearSurfaceInside(Vector3 point, ModelPrimitive prim, Quaternion invRot, float threshold)
    {
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
                float depth = 1f - Mathf.Sqrt(normDistSq);
                return depth < threshold;
            }

            case PrimitiveType.Cylinder:
            {
                float radius = prim.Scale.x * 0.5f;
                float halfHeight = prim.Scale.y; // Scale.y IS half-height

                if (radius < 1e-6f || halfHeight < 1e-6f) return false;

                // Outside height bounds — not inside the cylinder at all
                if (Mathf.Abs(local.y) >= halfHeight) return false;

                float radialDist = Mathf.Sqrt(local.x * local.x + local.z * local.z);

                // Outside radial bounds
                if (radialDist >= radius) return false;

                // Only check depth from the lateral (radial) surface.
                // Caps are synthetic (the original mesh typically has open ends),
                // so vertices near caps were already inside before approximation.
                float radialDepth = (radius - radialDist) / radius;
                return radialDepth < threshold;
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
                float minDepth = Mathf.Min(depthX, Mathf.Min(depthY, depthZ));

                return minDepth < threshold;
            }

            default:
                return false;
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