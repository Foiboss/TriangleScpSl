using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using Exiled.API.Features.Toys;
using TriangleScpSl.ParallelogramSpace;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TriangleScpSl.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public class TestParallelogramsCommand : ICommand
{
    const float AbsoluteToleranceUnits = 0.02f;

    readonly List<Primitive> _points = [];
    readonly List<Primitive> _parallelograms = [];
    readonly List<(float theta, float phi, Primitive stretch)> _stretches = [];

    public string Command { get; } = "TestParallelograms";
    public string[] Aliases { get; } = [];
    public string Description { get; } = "Parallelograms with adaptive stretch clustering";

    (Primitive stretch, float theta, float phi) FindOrCreateStretch(
        Vector3 v1World, Vector3 v2World, float trueTheta, float truePhi)
    {
        Primitive? best = null;
        float bestT = 0f, bestP = 0f;
        float bestErr = float.MaxValue;

        foreach (var (candT, candP, candStretch) in _stretches)
        {
            float err = ParallelogramSpaceUtils.MaxVertexError(
                v1World, v2World, trueTheta, truePhi, candT, candP);

            if (err <= AbsoluteToleranceUnits && err < bestErr)
            {
                bestErr = err;
                best = candStretch;
                bestT = candT;
                bestP = candP;
            }
        }

        if (best != null) return (best, bestT, bestP);

        Primitive stretch = ParallelogramSpaceUtils.CreateStretch(trueTheta, truePhi);
        _stretches.Add((trueTheta, truePhi, stretch));
        return (stretch, trueTheta, truePhi);
    }

    void ClearAll()
    {
        foreach (Primitive p in _parallelograms)
            if (p?.Base?.gameObject != null) p.Destroy();
        foreach (Primitive p in _points)
            if (p?.Base?.gameObject != null) p.Destroy();
        foreach (var (_, _, s) in _stretches)
            if (s.Base?.gameObject != null) s.Destroy();
        _parallelograms.Clear();
        _points.Clear();
        _stretches.Clear();
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
        if (player is null) { response = "Players only"; return false; }

        if (!int.TryParse(
                arguments.Count > 0 ? arguments.Array?[arguments.Offset] : null,
                out int amount))
            amount = 1;

        for (var i = 0; i < amount; i++)
        {
            Vector3 pos = player.Position;

            Vector3 vUp = Random.insideUnitSphere * Random.Range(1f, 10f);
            Vector3 vLeft = Random.insideUnitSphere * Random.Range(1f, 10f);
            if (!VectorPhiSolver.TrySolve(vLeft, vUp, out float theta, out float phi))
                continue;
            
            _points.Add(Primitive.Create(PrimitiveType.Sphere, PrimitiveFlags.Visible, pos + vUp,   Vector3.zero, Vector3.one * 0.1f, true, Color.red));
            _points.Add(Primitive.Create(PrimitiveType.Sphere, PrimitiveFlags.Visible, pos - vUp,   Vector3.zero, Vector3.one * 0.1f, true, Color.green));
            _points.Add(Primitive.Create(PrimitiveType.Sphere, PrimitiveFlags.Visible, pos + vLeft, Vector3.zero, Vector3.one * 0.1f, true, Color.blue));
            _points.Add(Primitive.Create(PrimitiveType.Sphere, PrimitiveFlags.Visible, pos - vLeft, Vector3.zero, Vector3.one * 0.1f, true, Color.yellow));

            var (stretch, stretchTheta, stretchPhi) = FindOrCreateStretch(vLeft, vUp, theta, phi);
            Vector3 v1ForStretch = ParallelogramSpaceUtils.ForwardTransform(vLeft, stretchTheta, stretchPhi);
            Vector3 v2ForStretch = ParallelogramSpaceUtils.ForwardTransform(vUp, stretchTheta, stretchPhi);

            _parallelograms.Add(
                ParallelogramSpaceUtils.CreateParallelogram(
                    pos, v1ForStretch, v2ForStretch, stretch, PrimitiveFlags.Visible, Color.white));
        }

        response = $"Spawned {_parallelograms.Count} parallelograms, {_stretches.Count} stretches";
        return true;
    }
}