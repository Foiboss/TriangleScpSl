using Exiled.API.Features.Toys;
using UnityEngine;

namespace TriangleScpSl.ParallelogramSpace;

public class StretchSpatialIndex
{
    readonly Dictionary<(int, int), List<Entry>> _cells = new();
    readonly float _cellSize; // radians
    readonly int _searchRadius; // cells to scan in each direction

    /// <param name="cellSize">Size of a cell in radians. 0.05 is a reasonable default</param>
    /// <param name="maxAngularTolerance">
    ///     Worst-case angular distance between an acceptable candidate and the query point
    ///     Determines how many neighboring cells to scan
    /// </param>
    public StretchSpatialIndex(float cellSize = 0.05f, float maxAngularTolerance = 0.1f)
    {
        _cellSize = cellSize;
        _searchRadius = Mathf.Max(1, Mathf.CeilToInt(maxAngularTolerance / cellSize));
    }

    public int Count { get; private set; }

    (int, int) CellOf(float theta, float phi)
        => (Mathf.FloorToInt(theta / _cellSize), Mathf.FloorToInt(phi / _cellSize));

    public void Add(float theta, float phi, Primitive stretch)
    {
        (int, int) key = CellOf(theta, phi);

        if (!_cells.TryGetValue(key, out List<Entry>? list))
        {
            list = new List<Entry>(4);
            _cells[key] = list;
        }

        list.Add(new Entry(theta, phi, stretch));
        Count++;
    }

    /// <summary>
    ///     Enumerate all entries in the cell containing (theta, phi) and its neighbors within _searchRadius
    ///     Caller filters by actual geometric error
    /// </summary>
    public IEnumerable<Entry> QueryNearby(float theta, float phi)
    {
        var (ct, cp) = CellOf(theta, phi);

        for (int dt = -_searchRadius; dt <= _searchRadius; dt++)
        for (int dp = -_searchRadius; dp <= _searchRadius; dp++)
        {
            (int, int) key = (ct + dt, cp + dp);

            if (_cells.TryGetValue(key, out List<Entry>? list))
            {
                foreach (Entry e in list)
                    yield return e;
            }
        }
    }

    public IEnumerable<Entry> All()
    {
        return _cells.Values.SelectMany(list => list);
    }

    public void Clear()
    {
        _cells.Clear();
        Count = 0;
    }

    public readonly struct Entry(float theta, float phi, Primitive stretch)
    {
        public readonly float Theta = theta;
        public readonly float Phi = phi;
        public readonly Primitive Stretch = stretch;
    }
}