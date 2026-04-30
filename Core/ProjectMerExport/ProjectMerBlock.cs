using AdminToys;
using UnityEngine;

namespace TriangleScpSl.Core.ProjectMerExport;

public sealed class ProjectMerBlock
{
    public string Name { get; init; } = string.Empty;
    public int ObjectId { get; init; }
    public int ParentId { get; init; }
    public Vector3 Position { get; init; }
    public Vector3 Rotation { get; init; }
    public Vector3 Scale { get; init; }
    public int BlockType { get; init; }
    public bool IsPrimitive { get; init; }
    public int PrimitiveType { get; init; }
    public Color PrimitiveColor { get; init; } = Color.white;
    public PrimitiveFlags PrimitiveFlags { get; init; }
    public bool Static { get; init; }
}