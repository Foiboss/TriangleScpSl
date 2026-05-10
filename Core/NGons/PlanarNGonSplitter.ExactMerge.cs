using UnityEngine;

namespace TriangleScpSl.Core.NGons;

public static partial class PlanarNGonSplitter
{
    const float ExactCoplanarEps = 1e-5f;

    static List<NGonRaw> MergeExactCoplanar(List<NGonRaw> faces)
    {
        int faceCount = faces.Count;
        if (faceCount < 2) return faces;

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

        var faceEligible = new bool[faceCount];
        var faceNormal = new Vector3[faceCount];

        for (var f = 0; f < faceCount; f++)
        {
            Vector3 normal = NGonMath.NewellNormalIndexed(table, faceIdx[f]);

            if (normal.sqrMagnitude < 1e-12f)
            {
                faceEligible[f] = false;
                faceNormal[f] = Vector3.up;
                continue;
            }

            faceEligible[f] = true;
            faceNormal[f] = normal.normalized;
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

                    Vector3 planeP = table[faceIdx[fa][0]];
                    Vector3 planeN = faceNormal[fa];
                    var coplanar = true;

                    foreach (int vi in faceIdx[fb])
                    {
                        if (Mathf.Abs(Vector3.Dot(table[vi] - planeP, planeN)) > ExactCoplanarEps)
                        {
                            coplanar = false;
                            break;
                        }
                    }

                    if (!coplanar) continue;

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
                list = [];
                clusterFaces[r] = list;
            }

            list.Add(f);
        }

        var result = new List<NGonRaw>(faceCount);
        var emitted = new bool[faceCount];

        foreach (KeyValuePair<int, List<int>> kvp in clusterFaces)
        {
            List<int> facesInCluster = kvp.Value;
            if (facesInCluster.Count == 1) continue;

            if (TryExtractBoundary(facesInCluster, faceIdx, table.ToArray(),
                out List<int>? loopIndices))
            {
                if (loopIndices is { Count: >= 3 })
                {
                    var verts = new List<Vector3>(loopIndices.Count);

                    foreach (int vi in loopIndices)
                        verts.Add(table[vi]);

                    result.Add(new NGonRaw(verts, faces[facesInCluster[0]].Color));
                    foreach (int fi in facesInCluster) emitted[fi] = true;
                }
            }
        }

        for (var f = 0; f < faceCount; f++)
        {
            if (!emitted[f])
                result.Add(faces[f]);
        }

        return result;
    }
}