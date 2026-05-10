using UnityEngine;

namespace TriangleScpSl.Core.NGons;

public static partial class PlanarNGonSplitter
{
    static List<NGonRaw> MergeCoplanar(List<NGonRaw> faces, float threshold)
    {
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

        int vertCount = table.Count;

        var faceEligible = new bool[faceCount];
        var faceNormal = new Vector3[faceCount];

        for (var f = 0; f < faceCount; f++)
        {
            int[] idx = faceIdx[f];
            Vector3 normal = NGonMath.NewellNormalIndexed(table, idx);

            if (normal.sqrMagnitude < 1e-12f)
            {
                faceEligible[f] = false;
                faceNormal[f] = Vector3.up;
                continue;
            }

            normal = normal.normalized;
            Vector3 centroid = Vector3.zero;
            foreach (int vi in idx) centroid += table[vi];

            faceEligible[f] = true;
            faceNormal[f] = normal;
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

                    if (Vector3.Dot(faceNormal[fa], faceNormal[fb]) < 1f - NormalEps)
                        continue;

                    if (!NGonMath.ColorsClose(faces[fa].Color, faces[fb].Color))
                        continue;

                    NGonMath.Union(parent, clusterCount, fa, fb);
                }
            }
        }

        var clusterFaces = new Dictionary<int, List<int>>();

        for (var f = 0; f < faceCount; f++)
        {
            if (!faceEligible[f]) continue;
            int r = NGonMath.Find(parent, f);

            if (!clusterFaces.TryGetValue(r, out List<int>? list))
            {
                list = new List<int>();
                clusterFaces[r] = list;
            }

            list.Add(f);
        }

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

        var snapAccum = new Vector3[vertCount];
        var snapCount = new int[vertCount];

        foreach (KeyValuePair<int, List<int>> kvp in clusterFaces)
        {
            int root = kvp.Key;
            List<int>? facesInCluster = kvp.Value;
            Vector3 planeN = clusterPlaneNormal[root];
            Vector3 planeP = clusterPlanePoint[root];

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

        var result = new List<NGonRaw>(faceCount);
        var emitted = new bool[faceCount];

        foreach (KeyValuePair<int, List<int>> kvp in clusterFaces)
        {
            List<int>? facesInCluster = kvp.Value;
            if (facesInCluster.Count == 1) continue;

            if (TryExtractBoundary(facesInCluster, faceIdx, snapped,
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

            foreach (int fi in facesInCluster)
            {
                int[] idx = faceIdx[fi];
                var verts = new List<Vector3>(idx.Length);
                foreach (int vi in idx) verts.Add(snapped[vi]);
                result.Add(new NGonRaw(verts, faces[fi].Color));
                emitted[fi] = true;
            }
        }

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

    static void FitPlane
    (
        List<int> facesInCluster,
        int[][] faceIdx,
        Vector3[] faceNormal,
        List<Vector3> table,
        out Vector3 planeN,
        out Vector3 planeP)
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
}