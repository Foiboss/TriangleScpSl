using UnityEngine;

namespace TriangleScpSl.Core.NGons;

/// <summary>Determines if a point lies inside the solid material of a 3D mesh using generalized winding numbers.</summary>
public sealed class ModelSolidVolume
{
    // Inside/outside threshold for winding number
    const float InsideThreshold = 0.5f;

    // Tail sample pull-in: lower = stricter, pulls samples away from boundary
    public static readonly float TailPullIn = 0.03f;
    // Grid density for tail sampling: ~N(N+1)/2 interior samples
    public static readonly int TailGridDepth = 2;

    // Triangle vertices for winding number calculation (fan-based from first vertex)
    readonly Vector3[] _tA;
    readonly Vector3[] _tB;
    readonly Vector3[] _tC;
    readonly Bounds _modelBounds;

    ModelSolidVolume(Vector3[] a, Vector3[] b, Vector3[] c, Bounds bounds)
    {
        _tA = a;
        _tB = b;
        _tC = c;
        _modelBounds = bounds;
    }

    public static ModelSolidVolume Build(IEnumerable<NGonRaw> faces)
    {
        var a = new List<Vector3>();
        var b = new List<Vector3>();
        var c = new List<Vector3>();

        var any = false;
        Vector3 mn = Vector3.zero, mx = Vector3.zero;

        foreach (NGonRaw face in faces)
        {
            List<Vector3> v = face.Vertices;
            if (v.Count < 3) continue;

            for (var i = 1; i < v.Count - 1; i++)
            {
                a.Add(v[0]);
                b.Add(v[i]);
                c.Add(v[i + 1]);
            }

            foreach (Vector3 p in v)
            {
                if (!any)
                {
                    mn = p;
                    mx = p;
                    any = true;
                }
                else
                {
                    mn = Vector3.Min(mn, p);
                    mx = Vector3.Max(mx, p);
                }
            }
        }

        var bounds = new Bounds();

        if (any)
        {
            bounds.SetMinMax(mn, mx);
            // Inflate slightly so boundary points are strictly outside
            bounds.Expand(1e-4f);
        }

        return new ModelSolidVolume(a.ToArray(), b.ToArray(), c.ToArray(), bounds);
    }

    /// <summary>Returns true iff point p is strictly inside solid material.</summary>
    public bool IsSolid(Vector3 p)
    {
        if (!_modelBounds.Contains(p)) return false;
        float w = WindingNumber(p);
        return w >= InsideThreshold;
    }

    // Generalized winding number using van Oosterom-Strang solid angle formula
    float WindingNumber(Vector3 p)
    {
        var sum = 0.0;
        int n = _tA.Length;

        for (var i = 0; i < n; i++)
        {
            Vector3 a = _tA[i] - p;
            Vector3 b = _tB[i] - p;
            Vector3 c = _tC[i] - p;

            float la = a.magnitude;
            float lb = b.magnitude;
            float lc = c.magnitude;

            // Skip if point is exactly on a vertex (ambiguous winding number)
            if (la < 1e-7f || lb < 1e-7f || lc < 1e-7f) return 0f;

            float num = Vector3.Dot(a, Vector3.Cross(b, c));

            float den = la * lb * lc
                + Vector3.Dot(a, b) * lc
                + Vector3.Dot(b, c) * la
                + Vector3.Dot(c, a) * lb;

            // Signed solid angle (in radians)
            sum += 2.0 * Math.Atan2(num, den);
        }

        // Divide by 4π to get winding number
        return (float)(sum / (4.0 * Math.PI));
    }

    /// <summary>Check if every sampled point inside the tail triangle is solid material.</summary>
    public bool IsTriangleFullyInsideSolid(Vector3 a, Vector3 b, Vector3 c)
    {
        float pull = Mathf.Clamp(TailPullIn, 0.001f, 0.49f);
        Vector3 g = (a + b + c) / 3f;

        if (!IsSolid(g)) return false;

        // Sample edge midpoints pulled inward
        Vector3 mab = Vector3.Lerp(Vector3.Lerp(a, b, 0.5f), g, pull);
        Vector3 mbc = Vector3.Lerp(Vector3.Lerp(b, c, 0.5f), g, pull);
        Vector3 mca = Vector3.Lerp(Vector3.Lerp(c, a, 0.5f), g, pull);
        if (!IsSolid(mab) || !IsSolid(mbc) || !IsSolid(mca)) return false;

        // Sample vertex-near points pulled inward
        Vector3 va = Vector3.Lerp(a, g, pull);
        Vector3 vb = Vector3.Lerp(b, g, pull);
        Vector3 vc = Vector3.Lerp(c, g, pull);
        if (!IsSolid(va) || !IsSolid(vb) || !IsSolid(vc)) return false;

        // Barycentric grid interior samples (boundary already covered above)
        int n = Mathf.Clamp(TailGridDepth, 1, 8);

        for (var i = 1; i < n; i++)
        {
            for (var j = 1; j < n - i; j++)
            {
                float u = i / (float)n;
                float v = j / (float)n;
                float w = 1f - u - v;
                Vector3 sample = u * a + v * b + w * c;
                sample = Vector3.Lerp(sample, g, pull);
                if (!IsSolid(sample)) return false;
            }
        }

        return true;
    }
}