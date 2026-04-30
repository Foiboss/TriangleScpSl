using System.Collections;
using AdminToys;
using TriangleScpSl.Core.ProjectMerExport;
using UnityEngine;

namespace TriangleScpSl.Core.Models;

public abstract class ModelBase
{
    public abstract int ParallelogramCount { get; }
    public abstract int QuadCount { get; }
    public abstract Vector3 Position { get; set; }
    public abstract Quaternion Rotation { get; set; }
    public abstract Vector3 Scale { get; set; }
    public abstract Transform Transform { get; }
    public abstract Color Color { set; }
    public abstract PrimitiveFlags Flags { get; set; }
    public abstract string ProjectMerDefaultName { get; }

    public abstract Vector3 TransformPoint(Vector3 localPoint);
    public abstract Vector3 InverseTransformPoint(Vector3 worldPoint);
    public abstract IEnumerator BuildTrianglesCoroutine(PrimitiveFlags flags, int trianglesPerFrame);
    public abstract void Destroy();

    public abstract IReadOnlyList<ProjectMerBlock> GetProjectMerBlocks
    (
        int modelObjectId,
        int startObjectId,
        Func<Vector3, Vector3> inverseTransformPoint,
        Quaternion modelRotation);
}