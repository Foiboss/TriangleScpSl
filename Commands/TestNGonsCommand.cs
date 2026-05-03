using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using Exiled.API.Features.Toys;
using TriangleScpSl.Core.NGons;
using TriangleScpSl.Core.Triangulation.Parallelogram;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TriangleScpSl.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public class TestNGonsCommand : ICommand
{
    readonly List<Primitive> _points = [];
    readonly List<ParallelogramPrimitive> _parallelograms = [];

    public string Command { get; } = "TestNGons";
    public string[] Aliases { get; } = [];
    public string Description { get; } = "Creates/Destroys example n-gon";

    void ClearAll()
    {
        foreach (ParallelogramPrimitive p in _parallelograms)
            p.Destroy();

        foreach (Primitive p in _points)
            if (p?.Base?.gameObject != null)
                p.Destroy();

        _parallelograms.Clear();
        _points.Clear();
    }

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (_parallelograms.Count > 0 || _points.Count > 0)
        {
            ClearAll();
            response = "Cleared";
            return true;
        }

        Player? player = Player.Get(sender);

        if (player is null)
        {
            response = "Players only";
            return false;
        }

        if (!int.TryParse(
            arguments.Count > 0 ? arguments.Array?[arguments.Offset] : null,
            out int vertexCount))
            vertexCount = 6;

        // Ensure at least 3 vertices for a valid polygon
        vertexCount = Mathf.Max(3, vertexCount);

        const float maxRadius = 5f;
        List<Vector3> points = [];
        Color color = Color.white;

        // Generate vertices in circular order (like OBJ face vertices)
        Vector3 basePos = player.Position + Vector3.up;
        float angleStep = 360f / vertexCount;

        for (var i = 0; i < vertexCount; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            float radius = Random.Range(1f, maxRadius);
            var offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            points.Add(basePos + offset);
        }

        foreach (Vector3 point in points)
            _points.Add(Primitive.Create(PrimitiveType.Sphere, PrimitiveFlags.Visible, point, Vector3.zero, Vector3.one * 0.05f, true, Color.red));

        var rawNGon = new NGonRaw
        {
            Vertices = points,
            Color = color,
        };

        List<ConvexNGon> convexNGons = [];
        ConvexNGonDecomposer.DecomposeOne(rawNGon, convexNGons);
        List<ModelParallelogram> modelParallelograms = ParallelogramProcessor.Process(convexNGons);

        foreach (ModelParallelogram modelParallelogram in modelParallelograms)
            _parallelograms.Add(ParallelogramPrimitive.Create(modelParallelogram));

        response = "Spawned";
        return true;
    }
}