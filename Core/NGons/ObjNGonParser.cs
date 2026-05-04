using System.Globalization;
using UnityEngine;

namespace TriangleScpSl.Core.NGons;

public static class ObjNGonParser
{
    public static bool TryParseFile(string objPath, Color defaultColor, out List<NGonRaw> nGons, out string error)
    {
        nGons = [];
        error = string.Empty;

        if (!File.Exists(objPath))
        {
            error = $"File not found: {objPath}";
            return false;
        }

        try
        {
            List<Vector3> vertices = [];
            Dictionary<string, Color> materials = [];
            Color? activeMaterialColor = null;

            string? baseDir = Path.GetDirectoryName(objPath);
            string mtlPath = Path.ChangeExtension(objPath, ".mtl");

            if (!string.IsNullOrEmpty(baseDir))
                mtlPath = Path.Combine(baseDir, Path.GetFileName(mtlPath));

            if (File.Exists(mtlPath))
            {
                ParseMtlFile(mtlPath, materials);

                if (materials.Count == 1)
                    activeMaterialColor = materials.Values.First();
            }

            string[] lines = File.ReadAllLines(objPath);

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string rawLine = lines[lineIndex];
                string line = rawLine.Trim();

                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

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

                var faceVerts = new List<Vector3>(partsFace.Length - 1);

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

                    faceVerts.Add(vertices[resolvedIndex]);
                }

                if (faceVerts.Count >= 3)
                {
                    nGons.Add(new NGonRaw
                    {
                        Vertices = faceVerts,
                        Color = activeMaterialColor ?? defaultColor,
                    });
                }
            }

            if (nGons.Count == 0)
            {
                error = "No polygons parsed from OBJ.";
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

    static Color NormalizeColor(float r, float g, float b)
    {
        if (r > 1f || g > 1f || b > 1f)
            return new Color(Mathf.Clamp01(r / 255f), Mathf.Clamp01(g / 255f), Mathf.Clamp01(b / 255f), 1f);

        return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), 1f);
    }

    static bool TryResolveIndex(int rawIndex, int vertexCount, out int resolvedIndex)
    {
        // OBJ indices are 1-based, negative values are relative to the end
        resolvedIndex = rawIndex > 0 ? rawIndex - 1 : vertexCount + rawIndex;
        return resolvedIndex >= 0 && resolvedIndex < vertexCount;
    }

    static bool TryParseFloat(string token, out float value)
        => float.TryParse(token, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
}