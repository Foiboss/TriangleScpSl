using System.Collections;
using System.Globalization;
using System.Text;
using AdminToys;
using TriangleScpSl.Core.ModelFactory;
using TriangleScpSl.Core.Triangulation.Parallelogram;
using TriangleScpSl.Core.Triangulation.Triangle;
using UnityEngine;

namespace TriangleScpSl.Core.ProjectMerExport;

public static class ProjectMerSchematicExporter
{
    public static bool TryExport(TriangulatedModel.TriangulatedModel model, string outputPath, string modelName, out string error)
        => TryExportInternal(model.GetTriangleSnapshot(), model.InverseTransformPoint, model.Rotation, outputPath, modelName, out error);

    public static bool TryExport(ParallelogramSpace.ParallelogramSpace model, string outputPath, string modelName, out string error)
        => TryExportParallelogramSpaceInternal(model.GetPrimitiveSnapshot(), model.InverseTransformPoint, model.Rotation, outputPath, modelName, out error);

    public static IEnumerator ExportCoroutine
    (
        TriangulatedModel.TriangulatedModel model,
        string outputPath,
        string modelName,
        int trianglesPerFrame,
        Action<bool, string> onCompleted)
    {
        IReadOnlyList<(ModelTriangle Triangle, PrimitiveFlags Flags)> triangles = model.GetTriangleSnapshot();
        Func<Vector3, Vector3> inverseTransformPoint = model.InverseTransformPoint;
        Quaternion modelRotation = model.Rotation;

        if (triangles.Count == 0)
        {
            onCompleted(false, "Model contains no triangles.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            onCompleted(false, "Output path is empty.");
            yield break;
        }

        string? outputDirectory = Path.GetDirectoryName(outputPath);

        try
        {
            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception ex)
        {
            onCompleted(false, ex.Message);
            yield break;
        }

        trianglesPerFrame = Mathf.Max(1, trianglesPerFrame);

        int objectIdSeed = GeneratePositiveId() % 500000 + 1000;
        int rootObjectId = objectIdSeed++;
        int modelObjectId = objectIdSeed++;
        int objectId = objectIdSeed;

        List<SchematicBlock> blocks =
        [
            new()
            {
                Name = string.IsNullOrWhiteSpace(modelName) ? "TriangulatedModel" : modelName,
                ObjectId = modelObjectId,
                ParentId = rootObjectId,
                Position = Vector3.zero,
                Rotation = Vector3.zero,
                Scale = Vector3.one,
                BlockType = 0,
                IsPrimitive = false,
                Static = false,
            },
        ];

        var processedTriangles = 0;

        for (var triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
        {
            (ModelTriangle tri, PrimitiveFlags flags) = triangles[triangleIndex];
            Vector3[][] parallelograms = TriangleParallelogramBuilder.GetParallelogramsInfo(tri.P1, tri.P2, tri.P3);

            for (var part = 0; part < 3; part++)
            {
                Vector3 vUp = parallelograms[part][0];
                Vector3 vLeft = parallelograms[part][1];
                Vector3 center = parallelograms[part][2];

                BuildParallelogramTransforms(vUp, vLeft, center, out Vector3 basePos, out Quaternion baseRot, out Vector3 baseScale, out Quaternion childLocalRot, out Vector3 childLocalScale);

                int baseId = objectId++;
                int quadId = objectId++;

                blocks.Add(new SchematicBlock
                {
                    Name = $"(T.{triangleIndex + 1})P{part + 1}.Base",
                    ObjectId = baseId,
                    ParentId = modelObjectId,
                    Position = inverseTransformPoint(basePos),
                    Rotation = (Quaternion.Inverse(modelRotation) * baseRot).eulerAngles,
                    Scale = baseScale,
                    BlockType = 0,
                    IsPrimitive = false,
                    Static = false,
                });

                blocks.Add(new SchematicBlock
                {
                    Name = $"(T.{triangleIndex + 1})P{part + 1}",
                    ObjectId = quadId,
                    ParentId = baseId,
                    Position = Vector3.zero,
                    Rotation = childLocalRot.eulerAngles,
                    Scale = childLocalScale,
                    BlockType = 1,
                    IsPrimitive = true,
                    PrimitiveType = (int)PrimitiveType.Quad,
                    PrimitiveColor = ToRgbaHex(tri.Color),
                    PrimitiveFlags = (int)flags,
                    Static = false,
                });
            }

            processedTriangles++;

            if (processedTriangles >= trianglesPerFrame)
            {
                processedTriangles = 0;
                yield return null;
            }
        }

        try
        {
            string json = BuildJson(rootObjectId, blocks);
            File.WriteAllText(outputPath, json, Encoding.UTF8);
            onCompleted(true, string.Empty);
        }
        catch (Exception ex)
        {
            onCompleted(false, ex.Message);
        }
    }

    public static IEnumerator ExportCoroutine
    (
        ParallelogramSpace.ParallelogramSpace model,
        string outputPath,
        string modelName,
        int primitivesPerFrame,
        Action<bool, string> onCompleted)
    {
        IReadOnlyList<ParallelogramSpace.ParallelogramSpace.PrimitiveSnapshot> primitives = model.GetPrimitiveSnapshot();
        Func<Vector3, Vector3> inverseTransformPoint = model.InverseTransformPoint;
        Quaternion modelRotation = model.Rotation;

        if (primitives.Count == 0)
        {
            onCompleted(false, "Model contains no primitives.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            onCompleted(false, "Output path is empty.");
            yield break;
        }

        string? outputDirectory = Path.GetDirectoryName(outputPath);

        try
        {
            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception ex)
        {
            onCompleted(false, ex.Message);
            yield break;
        }

        primitivesPerFrame = Mathf.Max(1, primitivesPerFrame);

        int objectIdSeed = GeneratePositiveId() % 500000 + 1000;
        int rootObjectId = objectIdSeed++;
        int modelObjectId = objectIdSeed++;
        int objectId = objectIdSeed;

        List<SchematicBlock> blocks =
        [
            new()
            {
                Name = string.IsNullOrWhiteSpace(modelName) ? "ParallelogramSpace" : modelName,
                ObjectId = modelObjectId,
                ParentId = rootObjectId,
                Position = Vector3.zero,
                Rotation = Vector3.zero,
                Scale = Vector3.one,
                BlockType = 0,
                IsPrimitive = false,
                Static = false,
            },
        ];

        List<int> primitiveObjectIds = new(primitives.Count);

        for (var i = 0; i < primitives.Count; i++)
        {
            primitiveObjectIds.Add(objectId++);
        }

        var processedPrimitives = 0;

        for (var i = 0; i < primitives.Count; i++)
        {
            ParallelogramSpace.ParallelogramSpace.PrimitiveSnapshot primitive = primitives[i];

            int parentId = primitive.ParentIndex >= 0 && primitive.ParentIndex < primitiveObjectIds.Count
                ? primitiveObjectIds[primitive.ParentIndex]
                : modelObjectId;

            Vector3 position = primitive.ParentIndex >= 0
                ? primitive.LocalPosition
                : inverseTransformPoint(primitive.Position);

            Vector3 rotation = primitive.ParentIndex >= 0
                ? primitive.LocalRotation.eulerAngles
                : (Quaternion.Inverse(modelRotation) * primitive.Rotation).eulerAngles;

            Vector3 scale = primitive.ParentIndex >= 0
                ? primitive.LocalScale
                : primitive.Scale;

            blocks.Add(new SchematicBlock
            {
                Name = $"(Q.{i + 1}){primitive.Kind}",
                ObjectId = primitiveObjectIds[i],
                ParentId = parentId,
                Position = position,
                Rotation = rotation,
                Scale = scale,
                BlockType = 1,
                IsPrimitive = true,
                PrimitiveType = (int)PrimitiveType.Quad,
                PrimitiveColor = ToRgbaHex(primitive.Color),
                PrimitiveFlags = (int)primitive.Flags,
                Static = false,
            });

            processedPrimitives++;

            if (processedPrimitives >= primitivesPerFrame)
            {
                processedPrimitives = 0;
                yield return null;
            }
        }

        try
        {
            string json = BuildJson(rootObjectId, blocks);
            File.WriteAllText(outputPath, json, Encoding.UTF8);
            onCompleted(true, string.Empty);
        }
        catch (Exception ex)
        {
            onCompleted(false, ex.Message);
        }
    }

    static bool TryExportInternal
    (
        IReadOnlyList<(ModelTriangle Triangle, PrimitiveFlags Flags)> triangles,
        Func<Vector3, Vector3> inverseTransformPoint,
        Quaternion modelRotation,
        string outputPath,
        string modelName,
        out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            error = "Output path is empty.";
            return false;
        }

        try
        {
            if (triangles.Count == 0)
            {
                error = "Model contains no triangles.";
                return false;
            }

            string? outputDirectory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            int objectIdSeed = GeneratePositiveId() % 500000 + 1000;
            int rootObjectId = objectIdSeed++;
            int modelObjectId = objectIdSeed++;
            int objectId = objectIdSeed;

            List<SchematicBlock> blocks =
            [
                new()
                {
                    Name = string.IsNullOrWhiteSpace(modelName) ? "TriangulatedModel" : modelName,
                    ObjectId = modelObjectId,
                    ParentId = rootObjectId,
                    // MER schematics should be stored in local coordinates.
                    Position = Vector3.zero,
                    Rotation = Vector3.zero,
                    Scale = Vector3.one,
                    BlockType = 0,
                    IsPrimitive = false,
                    Static = false,
                },
            ];

            for (var triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
            {
                (ModelTriangle tri, PrimitiveFlags flags) = triangles[triangleIndex];
                Vector3[][] parallelograms = TriangleParallelogramBuilder.GetParallelogramsInfo(tri.P1, tri.P2, tri.P3);

                for (var part = 0; part < 3; part++)
                {
                    Vector3 vUp = parallelograms[part][0];
                    Vector3 vLeft = parallelograms[part][1];
                    Vector3 center = parallelograms[part][2];

                    BuildParallelogramTransforms(vUp, vLeft, center, out Vector3 basePos, out Quaternion baseRot, out Vector3 baseScale, out Quaternion childLocalRot, out Vector3 childLocalScale);

                    int baseId = objectId++;
                    int quadId = objectId++;

                    blocks.Add(new SchematicBlock
                    {
                        Name = $"(T.{triangleIndex + 1})P{part + 1}.Base",
                        ObjectId = baseId,
                        ParentId = modelObjectId,
                        Position = inverseTransformPoint(basePos),
                        Rotation = (Quaternion.Inverse(modelRotation) * baseRot).eulerAngles,
                        Scale = baseScale,
                        BlockType = 0,
                        IsPrimitive = false,
                        Static = false,
                    });

                    blocks.Add(new SchematicBlock
                    {
                        Name = $"(T.{triangleIndex + 1})P{part + 1}",
                        ObjectId = quadId,
                        ParentId = baseId,
                        Position = Vector3.zero,
                        Rotation = childLocalRot.eulerAngles,
                        Scale = childLocalScale,
                        BlockType = 1,
                        IsPrimitive = true,
                        PrimitiveType = (int)PrimitiveType.Quad,
                        PrimitiveColor = ToRgbaHex(tri.Color),
                        PrimitiveFlags = (int)flags,
                        Static = false,
                    });
                }
            }

            string json = BuildJson(rootObjectId, blocks);
            File.WriteAllText(outputPath, json, Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static bool TryExportParallelogramSpaceInternal
    (
        IReadOnlyList<ParallelogramSpace.ParallelogramSpace.PrimitiveSnapshot> primitives,
        Func<Vector3, Vector3> inverseTransformPoint,
        Quaternion modelRotation,
        string outputPath,
        string modelName,
        out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            error = "Output path is empty.";
            return false;
        }

        try
        {
            if (primitives.Count == 0)
            {
                error = "Model contains no primitives.";
                return false;
            }

            string? outputDirectory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            int objectIdSeed = GeneratePositiveId() % 500000 + 1000;
            int rootObjectId = objectIdSeed++;
            int modelObjectId = objectIdSeed++;
            int objectId = objectIdSeed;

            List<SchematicBlock> blocks =
            [
                new()
                {
                    Name = string.IsNullOrWhiteSpace(modelName) ? "ParallelogramSpace" : modelName,
                    ObjectId = modelObjectId,
                    ParentId = rootObjectId,
                    Position = Vector3.zero,
                    Rotation = Vector3.zero,
                    Scale = Vector3.one,
                    BlockType = 0,
                    IsPrimitive = false,
                    Static = false,
                },
            ];

            List<int> primitiveObjectIds = new(primitives.Count);

            for (var i = 0; i < primitives.Count; i++)
            {
                primitiveObjectIds.Add(objectId++);
            }

            for (var i = 0; i < primitives.Count; i++)
            {
                ParallelogramSpace.ParallelogramSpace.PrimitiveSnapshot? primitive = primitives[i];

                int parentId = primitive.ParentIndex >= 0 && primitive.ParentIndex < primitiveObjectIds.Count
                    ? primitiveObjectIds[primitive.ParentIndex]
                    : modelObjectId;

                Vector3 position = primitive.ParentIndex >= 0
                    ? primitive.LocalPosition
                    : inverseTransformPoint(primitive.Position);

                Vector3 rotation = primitive.ParentIndex >= 0
                    ? primitive.LocalRotation.eulerAngles
                    : (Quaternion.Inverse(modelRotation) * primitive.Rotation).eulerAngles;

                Vector3 scale = primitive.ParentIndex >= 0
                    ? primitive.LocalScale
                    : primitive.Scale;

                blocks.Add(new SchematicBlock
                {
                    Name = $"(Q.{i + 1}){primitive.Kind}",
                    ObjectId = primitiveObjectIds[i],
                    ParentId = parentId,
                    Position = position,
                    Rotation = rotation,
                    Scale = scale,
                    BlockType = 1,
                    IsPrimitive = true,
                    PrimitiveType = (int)PrimitiveType.Quad,
                    PrimitiveColor = ToRgbaHex(primitive.Color),
                    PrimitiveFlags = (int)primitive.Flags,
                    Static = false,
                });
            }

            string json = BuildJson(rootObjectId, blocks);
            File.WriteAllText(outputPath, json, Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static string BuildJson(int rootObjectId, IReadOnlyList<SchematicBlock> blocks)
    {
        StringBuilder sb = new(1024 + blocks.Count * 256);
        sb.AppendLine("{");
        sb.Append("  \"RootObjectId\": ").Append(rootObjectId).AppendLine(",");
        sb.AppendLine("  \"Blocks\": [");

        for (var i = 0; i < blocks.Count; i++)
        {
            SchematicBlock block = blocks[i];
            sb.AppendLine("    {");
            sb.Append("      \"Name\": \"").Append(EscapeJson(block.Name)).AppendLine("\",");
            sb.Append("      \"ObjectId\": ").Append(block.ObjectId).AppendLine(",");
            sb.Append("      \"ParentId\": ").Append(block.ParentId).AppendLine(",");
            AppendVector3(sb, "Position", block.Position, 6);
            sb.AppendLine(",");
            AppendVector3(sb, "Rotation", block.Rotation, 6);
            sb.AppendLine(",");
            AppendVector3(sb, "Scale", block.Scale, 6);
            sb.AppendLine(",");
            sb.Append("      \"BlockType\": ").Append(block.BlockType).AppendLine(",");
            sb.AppendLine("      \"Properties\": {");

            if (block.IsPrimitive)
            {
                sb.Append("        \"PrimitiveType\": ").Append(block.PrimitiveType).AppendLine(",");
                sb.Append("        \"Color\": \"").Append(block.PrimitiveColor).AppendLine("\",");
                sb.Append("        \"PrimitiveFlags\": ").Append(block.PrimitiveFlags).AppendLine(",");
                sb.Append("        \"Static\": ").Append(block.Static ? "true" : "false").AppendLine();
            }
            else
            {
                sb.Append("        \"Static\": ").Append(block.Static ? "true" : "false").AppendLine();
            }

            sb.AppendLine("      }");
            sb.Append("    }");

            if (i < blocks.Count - 1)
                sb.Append(',');
            sb.AppendLine();
        }

        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static void AppendVector3(StringBuilder sb, string fieldName, Vector3 value, int indent)
    {
        string pad = new(' ', indent);
        sb.Append(pad).Append("\"").Append(fieldName).AppendLine("\": {");
        sb.Append(pad).Append("  \"x\": ").Append(FormatFloat(value.x)).AppendLine(",");
        sb.Append(pad).Append("  \"y\": ").Append(FormatFloat(value.y)).AppendLine(",");
        sb.Append(pad).Append("  \"z\": ").Append(FormatFloat(value.z)).AppendLine();
        sb.Append(pad).Append('}');
    }

    static void BuildParallelogramTransforms
    (
        Vector3 vUp,
        Vector3 vLeft,
        Vector3 center,
        out Vector3 basePosition,
        out Quaternion baseRotation,
        out Vector3 baseScale,
        out Quaternion childLocalRotation,
        out Vector3 childLocalScale)
    {
        if (Mathf.Abs(Vector3.Dot(vLeft, vUp)) > vUp.sqrMagnitude)
            (vUp, vLeft) = (vLeft, -vUp);

        (float a, float b, float x) = ParallelogramHelpUtils.GetAffineComponents(vUp, vLeft);
        float angleDeg = Mathf.Atan2(b, a) * Mathf.Rad2Deg;
        Vector3 vNormal = Vector3.Cross(ParallelogramHelpUtils.PerpSameHalfPlane(vUp, vLeft), vUp.normalized);

        basePosition = center;
        baseRotation = Quaternion.LookRotation(vNormal, vUp);
        baseScale = new Vector3(x, 1f, 1f);

        childLocalRotation = Quaternion.Euler(0f, 0f, -angleDeg);
        childLocalScale = new Vector3(b, a, 1f);
    }

    static int GeneratePositiveId() => Guid.NewGuid().GetHashCode() & int.MaxValue;

    static string ToRgbaHex(Color color)
    {
        Color32 c = color;
        return $"{c.r:X2}{c.g:X2}{c.b:X2}{c.a:X2}";
    }

    static string EscapeJson(string value) => value
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"");

    static string FormatFloat(float value)
    {
        if (Mathf.Abs(value) < 0.0000000005f)
            return "0.000000000";

        return value.ToString("0.000000000", CultureInfo.InvariantCulture);
    }

    sealed class SchematicBlock
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
        public string PrimitiveColor { get; init; } = "FFFFFFFF";
        public int PrimitiveFlags { get; init; }
        public bool Static { get; init; }
    }
}