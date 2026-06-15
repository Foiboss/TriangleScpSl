using System.Globalization;
using MEC;
using System.Text;
using TriangleScpSl.Core.Models;
using UnityEngine;

namespace TriangleScpSl.Core.ProjectMerExport;

public static class ProjectMerSchematicExporter
{
    public static IEnumerator<float> ExportCoroutine
    (
        ModelBase model,
        string outputPath,
        string modelName,
        int trianglesPerFrame,
        Action<bool, string> onCompleted)
    {
        Func<Vector3, Vector3> inverseTransformPoint = model.InverseTransformPoint;
        Quaternion modelRotation = model.Rotation;

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

        int objectIdSeed = GeneratePositiveId() % 500000 + 1000;
        int rootObjectId = objectIdSeed++;
        int modelObjectId = objectIdSeed++;
        int objectId = objectIdSeed;

        string resolvedName = string.IsNullOrWhiteSpace(modelName)
            ? model.ProjectMerDefaultName
            : modelName;

        List<ProjectMerBlock> blocks =
        [
            new()
            {
                Name = resolvedName,
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

        IReadOnlyList<ProjectMerBlock> modelBlocks = model.GetProjectMerBlocks(
            modelObjectId,
            objectId,
            inverseTransformPoint,
            modelRotation);

        if (modelBlocks.Count == 0)
        {
            onCompleted(false, "Model contains no exportable primitives.");
            yield break;
        }

        trianglesPerFrame = Mathf.Max(1, trianglesPerFrame);
        var processedTriangles = 0;

        foreach (ProjectMerBlock block in modelBlocks)
        {
            blocks.Add(block);
            processedTriangles++;

            if (processedTriangles >= trianglesPerFrame)
            {
                processedTriangles = 0;
                yield return Timing.WaitForOneFrame;
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

    static string BuildJson(int rootObjectId, IReadOnlyList<ProjectMerBlock> blocks)
    {
        StringBuilder sb = new(1024 + blocks.Count * 256);
        sb.AppendLine("{");
        sb.Append("  \"RootObjectId\": ").Append(rootObjectId).AppendLine(",");
        sb.AppendLine("  \"Blocks\": [");

        for (var i = 0; i < blocks.Count; i++)
        {
            ProjectMerBlock block = blocks[i];
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
                sb.Append("        \"Color\": \"").Append(ToRgbaHex(block.PrimitiveColor)).AppendLine("\",");
                sb.Append("        \"PrimitiveFlags\": ").Append((int)block.PrimitiveFlags).AppendLine(",");
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

    static int GeneratePositiveId() => Guid.NewGuid().GetHashCode() & int.MaxValue;

    static string ToRgbaHex(Color color)
    {
        Color32 c = color;
        return $"{c.r:X2}{c.g:X2}{c.b:X2}{c.a:X2}";
    }

    static string EscapeJson(string value) => value
        .Replace("\\", @"\\")
        .Replace("\"", "\\\"");

    static string FormatFloat(float value)
    {
        if (Mathf.Abs(value) < 0.0000000005f)
            return "0.000000000";

        return value.ToString("0.000000000", CultureInfo.InvariantCulture);
    }
}