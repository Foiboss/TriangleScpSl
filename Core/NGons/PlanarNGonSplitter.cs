using UnityEngine;

namespace TriangleScpSl.Core.NGons;

// Merges adjacent faces sharing surface continuity (normal agreement) into
// n-gons, then projects cluster vertices onto a best-fit plane for coplanarity.
// Merges purely on normal agreement; per-cluster plane-fit and shrinkage ensure
// vertex displacement stays within planarThreshold.
//
// Pipeline:
// (1) deduplicate vertices,
// (2) compute per-face normals,
// (3) build edge-face map,
// (4) cluster via union-find on normal agreement,
// (5) fit plane and evict worst-fitting faces,
// (6) snap vertices to planes,
// (7) extract boundaries and emit n-gons.
//
// planarThreshold: max vertex displacement during snapping (0 disables).
// NormalEps: per-edge normal agreement tolerance (~0.13 ≈ 30° max deviation).
public static class PlanarNGonSplitter
{
    const float VertexMergeEps = 1e-5f;
    // Per-edge normal continuity. ~0.13 ≈ 30° max deviation.
    const float NormalEps = 0.13f;

    public static List<NGonRaw> SplitAll(IEnumerable<NGonRaw> sources, float planarThreshold = 0f)
    {
        List<NGonRaw> faces = sources as List<NGonRaw> ?? [..sources];
        if (planarThreshold <= 0f) return faces;
        return MergeCoplanar(faces, planarThreshold);
    }

    // -----------------------------------------------------------------------

    static List<NGonRaw> MergeCoplanar(List<NGonRaw> faces, float threshold)
    {
        int faceCount = faces.Count;

        // Step 1: deduplicate vertices into global table
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

        int vertCount = table.Count;

        // Step 2: compute per-face normals and eligibility
        var faceEligible = new bool[faceCount];
        var faceNormal = new Vector3[faceCount];
        var faceCentroid = new Vector3[faceCount];

        for (var f = 0; f < faceCount; f++)
        {
            int[] idx = faceIdx[f];
            Vector3 normal = NewellNormalIndexed(table, idx);

            if (normal.sqrMagnitude < 1e-12f)
            {
                faceEligible[f] = false;
                faceNormal[f] = Vector3.up;
                faceCentroid[f] = Vector3.zero;
                continue;
            }

            normal = normal.normalized;
            Vector3 centroid = Vector3.zero;
            foreach (int vi in idx) centroid += table[vi];
            centroid /= idx.Length;

            faceEligible[f] = true;
            faceNormal[f] = normal;
            faceCentroid[f] = centroid;
        }

        // Step 3: build edge-to-faces adjacency map
        var edgeMap = new Dictionary<long, List<int>>();

        for (var f = 0; f < faceCount; f++)
        {
            int[] idx = faceIdx[f];
            int n = idx.Length;

            for (var i = 0; i < n; i++)
            {
                int a = idx[i];
                int b = idx[(i + 1) % n];
                long key = EdgeKey(a, b);

                if (!edgeMap.TryGetValue(key, out List<int>? list))
                {
                    list = new List<int>(2);
                    edgeMap[key] = list;
                }

                list.Add(f);
            }
        }

        // Step 4: cluster via union-find on normal agreement
        var parent = new int[faceCount];
        var clusterCount = new int[faceCount];

        for (var i = 0; i < faceCount; i++)
        {
            parent[i] = i;
            clusterCount[i] = 1;
        }

        foreach (KeyValuePair<long, List<int>> kv in edgeMap)
        {
            List<int> facesOnEdge = kv.Value;
            if (facesOnEdge.Count < 2) continue;

            for (var i = 0; i < facesOnEdge.Count; i++)
            {
                int fa = facesOnEdge[i];
                if (!faceEligible[fa]) continue;

                for (int j = i + 1; j < facesOnEdge.Count; j++)
                {
                    int fb = facesOnEdge[j];
                    if (!faceEligible[fb]) continue;

                    // Merge if normals agree and colors match
                    if (Vector3.Dot(faceNormal[fa], faceNormal[fb]) < 1f - NormalEps)
                        continue;

                    if (!ColorsClose(faces[fa].Color, faces[fb].Color))
                        continue;

                    Union(parent, clusterCount, fa, fb);
                }
            }
        }

        // Group faces by cluster root.
        var clusterFaces = new Dictionary<int, List<int>>();

        for (var f = 0; f < faceCount; f++)
        {
            if (!faceEligible[f]) continue;
            int r = Find(parent, f);

            if (!clusterFaces.TryGetValue(r, out List<int>? list))
            {
                list = new List<int>();
                clusterFaces[r] = list;
            }

            list.Add(f);
        }

        // Step 5: fit per-cluster planes and evict outlier faces
        var clusterPlaneNormal = new Dictionary<int, Vector3>();
        var clusterPlanePoint = new Dictionary<int, Vector3>();

        foreach (KeyValuePair<int, List<int>> kv in new List<KeyValuePair<int, List<int>>>(clusterFaces))
        {
            int root = kv.Key;
            List<int> facesInCluster = kv.Value;
            int safety = facesInCluster.Count + 1;

            while (safety-- > 0 && facesInCluster.Count >= 1)
            {
                FitPlane(facesInCluster, faceIdx, faceNormal, table,
                    out Vector3 planeN, out Vector3 planeP);

                int worstFace = -1;
                var worstDev = 0f;

                foreach (int fi in facesInCluster)
                {
                    int[] idx = faceIdx[fi];

                    foreach (int vi in idx)
                    {
                        float dev = Mathf.Abs(Vector3.Dot(table[vi] - planeP, planeN));

                        if (dev > worstDev)
                        {
                            worstDev = dev;
                            worstFace = fi;
                        }
                    }
                }

                if (worstDev <= threshold || worstFace < 0)
                {
                    clusterPlaneNormal[root] = planeN;
                    clusterPlanePoint[root] = planeP;
                    break;
                }

                facesInCluster.Remove(worstFace);
            }

            if (facesInCluster.Count == 0)
                clusterFaces.Remove(root);
        }

        // Step 6: snap cluster vertices to their planes
        var snapAccum = new Vector3[vertCount];
        var snapCount = new int[vertCount];

        foreach (KeyValuePair<int, List<int>> kvp in clusterFaces)
        {
            int root = kvp.Key;
            List<int>? facesInCluster = kvp.Value;
            Vector3 planeN = clusterPlaneNormal[root];
            Vector3 planeP = clusterPlanePoint[root];

            // Collect the unique cluster vertex set on the fly.
            var seen = new HashSet<int>();

            foreach (int fi in facesInCluster)
            {
                int[] idx = faceIdx[fi];

                foreach (int vi in idx)
                {
                    if (!seen.Add(vi)) continue;
                    Vector3 v = table[vi];
                    Vector3 target = v - Vector3.Dot(v - planeP, planeN) * planeN;
                    snapAccum[vi] += target;
                    snapCount[vi]++;
                }
            }
        }

        var snapped = new Vector3[vertCount];

        for (var vi = 0; vi < vertCount; vi++)
        {
            snapped[vi] = snapCount[vi] > 0
                ? snapAccum[vi] / snapCount[vi]
                : table[vi];
        }

        // Step 7: rebuild and emit merged n-gons
        var result = new List<NGonRaw>(faceCount);
        var emitted = new bool[faceCount];

        foreach (KeyValuePair<int, List<int>> kvp in clusterFaces)
        {
            int root = kvp.Key;
            List<int>? facesInCluster = kvp.Value;
            if (facesInCluster.Count == 1) continue;

            if (TryExtractBoundary(facesInCluster, faceIdx,
                clusterPlaneNormal[root], snapped,
                out List<int>? loopIndices))
            {
                if (loopIndices is { Count: >= 3 })
                {
                    var verts = new List<Vector3>(loopIndices.Count);
                    verts.AddRange(loopIndices.Select(vi => snapped[vi]));

                    Color color = faces[facesInCluster[0]].Color;
                    result.Add(new NGonRaw(verts, color));

                    foreach (int fi in facesInCluster) emitted[fi] = true;
                    continue;
                }
            }

            // Boundary extraction failed; emit faces individually
            foreach (int fi in facesInCluster)
            {
                int[] idx = faceIdx[fi];
                var verts = new List<Vector3>(idx.Length);
                foreach (int vi in idx) verts.Add(snapped[vi]);
                result.Add(new NGonRaw(verts, faces[fi].Color));
                emitted[fi] = true;
            }
        }

        // Emit singletons and evicted faces with snapped positions
        for (var f = 0; f < faceCount; f++)
        {
            if (emitted[f]) continue;

            int[] idx = faceIdx[f];
            var verts = new List<Vector3>(idx.Length);
            foreach (int vi in idx) verts.Add(snapped[vi]);
            result.Add(new NGonRaw(verts, faces[f].Color));
        }

        return result;
    }

    // Fit plane to cluster: centroid + face-area-weighted normal.
    static void FitPlane
    (
        List<int> facesInCluster,
        int[][] faceIdx,
        Vector3[] faceNormal,
        List<Vector3> table,
        out Vector3 planeN,
        out Vector3 planeP
    )
    {
        Vector3 centroid = Vector3.zero;
        var seen = new HashSet<int>();

        foreach (int fi in facesInCluster)
        {
            int[] idx = faceIdx[fi];

            foreach (int vi in idx)
            {
                if (!seen.Add(vi)) continue;
                centroid += table[vi];
            }
        }

        if (seen.Count == 0)
        {
            planeN = Vector3.up;
            planeP = Vector3.zero;
            return;
        }

        centroid /= seen.Count;

        Vector3 normal = Vector3.zero;

        foreach (int fi in facesInCluster)
            normal += faceNormal[fi] * faceIdx[fi].Length;

        if (normal.sqrMagnitude < 1e-12f)
            normal = facesInCluster.Count > 0 ? faceNormal[facesInCluster[0]] : Vector3.up;
        planeN = normal.normalized;
        planeP = centroid;
    }

    // Extract cluster boundary as a CCW loop. Returns false on non-manifold geometry.
    static bool TryExtractBoundary
    (
        List<int> facesInCluster,
        int[][] faceIdx,
        Vector3 clusterNormal,
        Vector3[] snapped,
        out List<int>? loop
    )
    {
        loop = null;

        var directed = new HashSet<long>();
        var allEdges = new List<(int a, int b)>();

        foreach (int fi in facesInCluster)
        {
            int[] idx = faceIdx[fi];
            int n = idx.Length;

            for (var i = 0; i < n; i++)
            {
                int a = idx[i];
                int b = idx[(i + 1) % n];
                long k = DirectedKey(a, b);

                if (!directed.Add(k))
                    return false;
                allEdges.Add((a, b));
            }
        }

        var nextOnBoundary = new Dictionary<int, int>();
        var boundaryEdgeCount = 0;

        foreach (var (a, b) in allEdges)
        {
            if (directed.Contains(DirectedKey(b, a))) continue;

            if (nextOnBoundary.ContainsKey(a))
                return false;
            nextOnBoundary[a] = b;
            boundaryEdgeCount++;
        }

        if (boundaryEdgeCount < 3) return false;

        int start = -1;

        foreach (int k in nextOnBoundary.Keys)
        {
            start = k;
            break;
        }

        if (start < 0) return false;

        var result = new List<int>(boundaryEdgeCount);
        int cur = start;

        for (var step = 0; step < boundaryEdgeCount; step++)
        {
            result.Add(cur);
            if (!nextOnBoundary.TryGetValue(cur, out int nxt)) return false;
            cur = nxt;

            if (cur == start)
            {
                if (result.Count != boundaryEdgeCount) return false;
                loop = SimplifyCollinear(result, snapped);
                return loop is { Count: >= 3 };
            }
        }

        return false;
    }

    // Drop collinear vertices from outline to reduce n-gon vertex count.
    static List<int> SimplifyCollinear(List<int> loop, Vector3[] pos)
    {
        int n = loop.Count;
        if (n < 4) return loop;

        const float sinEps = 0.01f; // ~0.5°
        var keep = new bool[n];

        for (var i = 0; i < n; i++)
        {
            keep[i] = true;
        }

        var changed = true;

        while (changed)
        {
            changed = false;

            for (var i = 0; i < n; i++)
            {
                if (!keep[i]) continue;

                int prev = PrevKept(keep, i, n);
                int next = NextKept(keep, i, n);
                if (prev == i || next == i || prev == next) break;

                Vector3 a = pos[loop[prev]];
                Vector3 b = pos[loop[i]];
                Vector3 c = pos[loop[next]];

                Vector3 ab = b - a;
                Vector3 bc = c - b;
                float lenab = ab.magnitude;
                float lenbc = bc.magnitude;

                if (lenab < 1e-7f || lenbc < 1e-7f)
                {
                    keep[i] = false;
                    changed = true;
                    continue;
                }

                float sinAngle = Vector3.Cross(ab, bc).magnitude / (lenab * lenbc);

                if (sinAngle < sinEps)
                {
                    keep[i] = false;
                    changed = true;
                }
            }
        }

        var result = new List<int>(n);

        for (var i = 0; i < n; i++)
        {
            if (keep[i]) result.Add(loop[i]);
        }

        return result;
    }

    static int PrevKept(bool[] keep, int i, int n)
    {
        for (var s = 1; s <= n; s++)
        {
            int k = (i - s + n) % n;
            if (keep[k]) return k;
        }

        return i;
    }

    static int NextKept(bool[] keep, int i, int n)
    {
        for (var s = 1; s <= n; s++)
        {
            int k = (i + s) % n;
            if (keep[k]) return k;
        }

        return i;
    }


    static int Find(int[] parent, int x)
    {
        while (parent[x] != x)
        {
            parent[x] = parent[parent[x]];
            x = parent[x];
        }

        return x;
    }

    static void Union(int[] parent, int[] count, int a, int b)
    {
        int ra = Find(parent, a);
        int rb = Find(parent, b);
        if (ra == rb) return;
        // Union by size.
        if (count[ra] < count[rb]) (ra, rb) = (rb, ra);
        parent[rb] = ra;
        count[ra] += count[rb];
    }

    static long EdgeKey(int a, int b)
    {
        if (a > b) (a, b) = (b, a);
        return ((long)a << 32) | (uint)b;
    }

    static long DirectedKey(int a, int b)
        => ((long)a << 32) | (uint)b;

    static bool ColorsClose(Color a, Color b, float eps = 1e-3f)
        => Mathf.Abs(a.r - b.r) < eps
            && Mathf.Abs(a.g - b.g) < eps
            && Mathf.Abs(a.b - b.b) < eps
            && Mathf.Abs(a.a - b.a) < eps;

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

    static Vector3 NewellNormalIndexed(List<Vector3> table, int[] indices)
    {
        Vector3 n = Vector3.zero;
        int count = indices.Length;

        for (var i = 0; i < count; i++)
        {
            Vector3 c = table[indices[i]];
            Vector3 nx = table[indices[(i + 1) % count]];
            n.x += (c.y - nx.y) * (c.z + nx.z);
            n.y += (c.z - nx.z) * (c.x + nx.x);
            n.z += (c.x - nx.x) * (c.y + nx.y);
        }

        return n;
    }
}