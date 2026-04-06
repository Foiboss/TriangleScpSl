using System.Globalization;
using Triangle.Core.TriangulatedModel;
using UnityEngine;

namespace Triangle.Core.FileToTriangles;

public static class ObjParser
{
    public static bool TryParseFile(string filePath, out List<ModelTriangle> triangles, out string error)
    {
        triangles = [];
        error = string.Empty;

        if (!File.Exists(filePath))
        {
            error = $"File not found: {filePath}";
            return false;
        }

        try
        {
            List<Vector3> vertices = [];
            List<Color?> vertexColors = [];
            Dictionary<string, Color> materials = [];
            Color? activeMaterialColor = null;
            string? baseDir = Path.GetDirectoryName(filePath);

            string[] lines = File.ReadAllLines(filePath);

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string rawLine = lines[lineIndex];
                string line = rawLine.Trim();

                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                if (line.StartsWith("mtllib ", StringComparison.OrdinalIgnoreCase))
                {
                    ParseMaterialLibraries(line, baseDir, materials);
                    continue;
                }

                if (line.StartsWith("usemtl ", StringComparison.OrdinalIgnoreCase))
                {
                    string materialName = line.Substring(7).Trim();
                    activeMaterialColor = materials.TryGetValue(materialName, out Color materialColor) ? materialColor : null;
                    continue;
                }

                if (line.StartsWith("v ", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length < 4 ||
                        !TryParseFloat(parts[1], out float x) ||
                        !TryParseFloat(parts[2], out float y) ||
                        !TryParseFloat(parts[3], out float z))
                    {
                        error = $"Invalid vertex at line {lineIndex + 1}.";
                        return false;
                    }

                    vertices.Add(new Vector3(x, y, z));

                    if (parts.Length >= 7 &&
                        TryParseFloat(parts[4], out float r) &&
                        TryParseFloat(parts[5], out float g) &&
                        TryParseFloat(parts[6], out float b))
                    {
                        vertexColors.Add(NormalizeColor(r, g, b));
                    }
                    else
                    {
                        vertexColors.Add(null);
                    }

                    continue;
                }

                if (!line.StartsWith("f ", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] partsFace = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

                if (partsFace.Length < 4)
                {
                    error = $"Face with less than 3 vertices at line {lineIndex + 1}.";
                    return false;
                }

                List<int> faceIndices = [];

                for (var i = 1; i < partsFace.Length; i++)
                {
                    string vertexRef = partsFace[i];
                    string indexToken = vertexRef.Split('/')[0];

                    if (!int.TryParse(indexToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rawIndex))
                    {
                        error = $"Invalid face index at line {lineIndex + 1}.";
                        return false;
                    }

                    if (!TryResolveIndex(rawIndex, vertices.Count, out int resolvedIndex))
                    {
                        error = $"Face index out of range at line {lineIndex + 1}.";
                        return false;
                    }

                    faceIndices.Add(resolvedIndex);
                }

                for (var i = 1; i < faceIndices.Count - 1; i++)
                {
                    int i1 = faceIndices[0];
                    int i2 = faceIndices[i];
                    int i3 = faceIndices[i + 1];

                    Color triangleColor = ResolveTriangleColor(i1, i2, i3, activeMaterialColor, vertexColors);
                    triangles.Add(new ModelTriangle(vertices[i1], vertices[i2], vertices[i3], triangleColor));
                }
            }

            if (triangles.Count == 0)
            {
                error = "No triangles parsed from OBJ.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"OBJ parse failed: {ex.Message}";
            return false;
        }
    }

    static void ParseMaterialLibraries(string line, string? baseDir, Dictionary<string, Color> materials)
    {
        string[] tokens = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

        for (var i = 1; i < tokens.Length; i++)
        {
            string mtlName = tokens[i];
            string mtlPath = string.IsNullOrEmpty(baseDir) ? mtlName : Path.Combine(baseDir, mtlName);

            if (!File.Exists(mtlPath))
                continue;

            ParseMtlFile(mtlPath, materials);
        }
    }

    static void ParseMtlFile(string mtlPath, Dictionary<string, Color> materials)
    {
        string? currentMaterial = null;

        foreach (string rawLine in File.ReadAllLines(mtlPath))
        {
            string line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                continue;

            if (line.StartsWith("newmtl ", StringComparison.OrdinalIgnoreCase))
            {
                currentMaterial = line.Substring(7).Trim();
                continue;
            }

            if (currentMaterial is null || !line.StartsWith("Kd ", StringComparison.OrdinalIgnoreCase))
                continue;

            string[] kdParts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

            if (kdParts.Length < 4 ||
                !TryParseFloat(kdParts[1], out float r) ||
                !TryParseFloat(kdParts[2], out float g) ||
                !TryParseFloat(kdParts[3], out float b))
            {
                continue;
            }

            materials[currentMaterial] = NormalizeColor(r, g, b);
        }
    }

    static Color ResolveTriangleColor(int i1, int i2, int i3, Color? materialColor, List<Color?> vertexColors)
    {
        if (materialColor.HasValue)
            return materialColor.Value;

        Color accumulated = Color.black;
        var count = 0;

        Color? c1 = vertexColors[i1];
        Color? c2 = vertexColors[i2];
        Color? c3 = vertexColors[i3];

        if (c1.HasValue)
        {
            accumulated += c1.Value;
            count++;
        }

        if (c2.HasValue)
        {
            accumulated += c2.Value;
            count++;
        }

        if (c3.HasValue)
        {
            accumulated += c3.Value;
            count++;
        }

        return count > 0 ? accumulated / count : Color.white;
    }

    static Color NormalizeColor(float r, float g, float b)
    {
        if (r > 1f || g > 1f || b > 1f)
            return new Color(Mathf.Clamp01(r / 255f), Mathf.Clamp01(g / 255f), Mathf.Clamp01(b / 255f), 1f);

        return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), 1f);
    }

    static bool TryResolveIndex(int rawIndex, int vertexCount, out int resolvedIndex)
    {
        // OBJ indices are 1-based, negative values are relative to the end.
        resolvedIndex = rawIndex > 0 ? rawIndex - 1 : vertexCount + rawIndex;
        return resolvedIndex >= 0 && resolvedIndex < vertexCount;
    }

    static bool TryParseFloat(string token, out float value) => float.TryParse(token, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
}