using Exiled.API.Features;
using System.Collections;
using System.Diagnostics;
using TriangleScpSl.Core.NGons.Detectors;
using UnityEngine;

namespace TriangleScpSl.Core.NGons;

public static class PrimitiveShapeDetector
{
    const float VertexMergeEps = 1e-5f;

    public static (List<ModelPrimitive> detected, List<NGonRaw> remaining) Detect
    (
        List<NGonRaw> faces,
        ModelSolidVolume? solid = null,
        float smoothMaxAngle = SmoothnessCheck.DefaultMaxAngle,
        float smoothMinFraction = SmoothnessCheck.DefaultMinFraction)
    {
        var detected = new List<ModelPrimitive>();

        if (faces.Count < 6)
            return (detected, faces);

        int faceCount = faces.Count;

        // Deduplicate vertices into a shared table (remove near-duplicates)
        var table = new List<Vector3>();
        var faceIdx = new int[faceCount][];

        for (var f = 0; f < faceCount; f++)
        {
            List<Vector3> verts = faces[f].Vertices;
            faceIdx[f] = new int[verts.Count];

            for (var v = 0; v < verts.Count; v++)
            {
                faceIdx[f][v] = Intern(table, verts[v]);
            }
        }

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

            if (SphereDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive sphereResult, solid, smoothMaxAngle, smoothMinFraction))
                primitive = sphereResult;
            else if (CylinderDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive cylResult, smoothMaxAngle, smoothMinFraction))
                primitive = cylResult;
            else if (CubeDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive cubeResult))
                primitive = cubeResult;

            if (primitive == null) continue;

            detected.Add(primitive);

            foreach (int fi in clusterFaceIndices)
                consumed[fi] = true;

            Log.Info($"PrimitiveShapeDetector: Detected {primitive.Type} " +
                $"({clusterFaceIndices.Count} faces → 1 primitive)");
        }

        // Pass 2: Approximate sphere/cylinder fit with relaxed tolerance
        if (solid != null)
        {
            foreach (KeyValuePair<int, List<int>> kv in sortedClusters)
            {
                List<int> clusterFaceIndices = kv.Value;
                if (clusterFaceIndices.Any(i => consumed[i])) continue;
                if (clusterFaceIndices.Count < 8) continue;

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
                    out ModelPrimitive approxResult, smoothMaxAngle, smoothMinFraction))
                    approxPrimitive = approxResult;
                else if (CylinderDetector.TryDetectApproximate(clusterFaces, uniqueVerts, solid,
                    out ModelPrimitive cylApproxResult, smoothMaxAngle, smoothMinFraction))
                    approxPrimitive = cylApproxResult;

                if (approxPrimitive != null)
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

                if (CubeDetector.TryDetectPartial(clusterFaces, uniqueVerts, solid,
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
        ModelSolidVolume? solid,
        float smoothMaxAngle,
        float smoothMinFraction,
        float maxMsPerFrame,
        Action<List<ModelPrimitive>, List<NGonRaw>> onComplete)
    {
        if (faces.Count < 6)
        {
            onComplete([], faces);
            yield break;
        }

        var sw = Stopwatch.StartNew();

        int faceCount = faces.Count;

        var table = new List<Vector3>();
        var faceIdx = new int[faceCount][];

        for (var f = 0; f < faceCount; f++)
        {
            List<Vector3> verts = faces[f].Vertices;
            faceIdx[f] = new int[verts.Count];

            for (var v = 0; v < verts.Count; v++)
            {
                faceIdx[f][v] = Intern(table, verts[v]);
            }
        }

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

                    if (NGonMath.ColorsClose(faces[fa].Color, faces[fb].Color))
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

            if (SphereDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive sphereResult, solid, smoothMaxAngle, smoothMinFraction))
                primitive = sphereResult;
            else if (CylinderDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive cylResult, smoothMaxAngle, smoothMinFraction))
                primitive = cylResult;
            else if (CubeDetector.TryDetect(clusterFaces, uniqueVerts, out ModelPrimitive cubeResult))
                primitive = cubeResult;

            if (primitive != null)
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

        // Pass 2: Approximate sphere/cylinder fit
        if (solid != null)
        {
            foreach (KeyValuePair<int, List<int>> kv in sortedClusters)
            {
                List<int> clusterFaceIndices = kv.Value;
                if (clusterFaceIndices.Any(i => consumed[i])) continue;
                if (clusterFaceIndices.Count < 8) continue;

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
                    out ModelPrimitive approxResult, smoothMaxAngle, smoothMinFraction))
                    approxPrimitive = approxResult;
                else if (CylinderDetector.TryDetectApproximate(clusterFaces, uniqueVerts, solid,
                    out ModelPrimitive cylApproxResult, smoothMaxAngle, smoothMinFraction))
                    approxPrimitive = cylApproxResult;

                if (approxPrimitive != null)
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

                if (CubeDetector.TryDetectPartial(clusterFaces, uniqueVerts, solid,
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

    static int Intern(List<Vector3> table, Vector3 v)
    {
        float eps2 = VertexMergeEps * VertexMergeEps;

        for (var i = 0; i < table.Count; i++)
        {
            if ((table[i] - v).sqrMagnitude <= eps2)
                return i;
        }

        table.Add(v);
        return table.Count - 1;
    }
}