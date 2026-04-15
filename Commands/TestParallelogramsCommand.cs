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
    readonly List<Primitive> _points = [];
    readonly List<Primitive> _parallelograms = [];
    readonly Dictionary<float, Primitive> _stretches = [];
    public string Command { get; } = "TestParallelograms";
    public string[] Aliases { get; } = [];
    public string Description { get; } = "Spawns an example of parallelograms in space using dark magic, second call will destroy it";
    
    void ClearParallelograms()
    {
        foreach (Primitive prim in _parallelograms)
            if (prim?.Base?.gameObject != null)
                prim.Destroy();

        foreach (Primitive prim in _points)
            if (prim?.Base?.gameObject != null)
                prim.Destroy();

        foreach (var stretch in _stretches)
            if (stretch.Value?.Base?.gameObject != null)
                stretch.Value.Destroy();

        _parallelograms.Clear();
        _points.Clear();
        _stretches.Clear();
    }

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!_parallelograms.IsEmpty() || !_points.IsEmpty())
        {
            ClearParallelograms();
            response = "Cleared previous parallelograms";
            return true;
        }
        
        Player? player = Player.Get(sender);

        if (player is null)
        {
            response = "This command can only be used by a player.";
            return false;
        }

        string? amountArgument = arguments.Count > 0 ? arguments.Array?[arguments.Offset] : null;

        if (!int.TryParse(amountArgument, out int amount))
            amount = 1;
        
        for (var i = 0; i < amount; i++)
        {
            Vector3 position = player.Position;
            Vector3 vUp = Random.insideUnitSphere * Random.Range(1f, 10f);
            Vector3 vLeft = Random.insideUnitSphere * Random.Range(1f, 10f);
            
            _points.Add(Primitive.Create(PrimitiveType.Sphere, PrimitiveFlags.Visible, position + vUp, Vector3.zero, Vector3.one * 0.1f, true, Color.red));
            _points.Add(Primitive.Create(PrimitiveType.Sphere, PrimitiveFlags.Visible, position - vUp, Vector3.zero, Vector3.one * 0.1f, true, Color.green));
            _points.Add(Primitive.Create(PrimitiveType.Sphere, PrimitiveFlags.Visible, position + vLeft, Vector3.zero, Vector3.one * 0.1f, true, Color.blue));
            _points.Add(Primitive.Create(PrimitiveType.Sphere, PrimitiveFlags.Visible, position - vLeft, Vector3.zero, Vector3.one * 0.1f, true, Color.yellow));
            
            VectorPhiSolver.Solve(vLeft, vUp, out float phi, out Vector3 v1, out Vector3 v2);

            if (!_stretches.TryGetValue(phi, out Primitive stretch))
            {
                stretch = ParallelogramSpaceUtils.CreateStretch(phi);
                _stretches[phi] = stretch;
            }

            Primitive parallelogram = ParallelogramSpaceUtils.CreateParallelogram(position, v1, v2, stretch, PrimitiveFlags.Visible, Color.white);
            _parallelograms.Add(parallelogram);
        }
        
        
        response = $"Spawned {_parallelograms.Count} parallelograms & {_stretches.Count} stretches";
        return true;
    }
}