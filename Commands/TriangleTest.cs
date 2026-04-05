using AdminToys;
using CommandSystem;
using Exiled.API.Features;
using Exiled.API.Features.Toys;
using Triangle.Core.Triangulation.Triangle;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Triangle.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public class TriangleTest : ICommand
{
    public string Command { get; } = "TriangleExample";
    public string[] Aliases { get; } = [];
    public string Description { get; } = "Spawns an example triangle on player position, second call will destroy it";
    List<Primitive> _points = [];
    TrianglePrimitive? _triangle;

    void ClearCurrentTriangle()
    {
        foreach (Primitive point in _points)
            point.Destroy();

        _points.Clear();

        _triangle?.Destroy();
        _triangle = null;
    }
    
    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (_triangle is not null)
        {
            ClearCurrentTriangle();
            response = "Destroyed";
            return true;
        }

        Player? player = Player.Get(sender);

        if (player is null)
        {
            response = "This command can only be used by a player.";
            return false;
        }
        
        Vector3 p1 = player.Position + Random.insideUnitSphere * 2f;
        Vector3 p2 = player.Position + Random.insideUnitSphere * 2f;
        Vector3 p3 = player.Position + Random.insideUnitSphere * 2f;

        _points.Add(Primitive.Create(PrimitiveType.Sphere, PrimitiveFlags.Visible, p1, Vector3.zero, Vector3.one * 0.1f, true, Color.red));
        _points.Add(Primitive.Create(PrimitiveType.Sphere, PrimitiveFlags.Visible, p2, Vector3.zero, Vector3.one * 0.1f, true, Color.green));
        _points.Add(Primitive.Create(PrimitiveType.Sphere, PrimitiveFlags.Visible, p3, Vector3.zero, Vector3.one * 0.1f, true, Color.blue));
        
        _triangle = new(p1, p2, p3, Color.magenta, PrimitiveFlags.Visible);
        
        response = "Done";
        return true;
    }
}