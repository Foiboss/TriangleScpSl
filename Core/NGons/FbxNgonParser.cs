using System.Globalization;
using System.Text;
using UnityEngine;

namespace TriangleScpSl.Core.NGons;

// Raw n-gon from FBX: vertices and face color (Unity Color).
public struct NgonRaw
{
    public List<Vector3> Vertices;
    public Color Color;
}

// ASCII FBX parser that extracts all n-gons and per-face colors from Geometry sections.
// Supported:
//   - ASCII FBX (key:value tree with braces).
//   - Color mapping modes: AllSame, ByPolygon, ByPolygonVertex, ByVertex.
//   - Color reference modes: Direct, IndexToDirect.
//   - Correct PolygonVertexIndex decoding (last index per polygon = ~realIdx).
// Not supported (throws NotSupportedException or falls back to defaultColor):
//   - Binary FBX — re-export as ASCII from Maya/Blender/3ds Max.
//   - Model-node transforms: vertices are returned in geometry local space.
//   - Holes and self-intersecting polygons.
public static class FbxNgonParser
{
    // fbxPath: path to the .fbx file.
    // defaultColor: color assigned to faces when the FBX has no LayerElementColor.
    public static List<NgonRaw> Parse(string fbxPath, Color? defaultColor = null)
    {
        if (!File.Exists(fbxPath))
            throw new FileNotFoundException($"FBX file not found: {fbxPath}");

        byte[] head = ReadHead(fbxPath, 32);

        if (IsBinaryFbx(head))
        {
            throw new NotSupportedException(
                "Binary FBX is not supported by FbxNgonParser. " +
                "Re-export as ASCII FBX (choose 'ASCII' in Maya/Blender/3ds Max export settings).");
        }

        string text = File.ReadAllText(fbxPath);
        return ParseAscii(text, defaultColor ?? Color.white);
    }

    // ----------------------------------------------------------------- detection / IO

    static byte[] ReadHead(string path, int n)
    {
        using FileStream fs = File.OpenRead(path);

        var len = (int)Math.Min(n, fs.Length);
        var buf = new byte[len];
        var read = 0;

        while (read < len)
        {
            int r = fs.Read(buf, read, len - read);
            if (r <= 0) break;
            read += r;
        }

        return buf;
    }

    static bool IsBinaryFbx(byte[] head)
    {
        const string magic = "Kaydara FBX Binary  ";
        if (head.Length < magic.Length) return false;

        for (var i = 0; i < magic.Length; i++)
        {
            if (head[i] != (byte)magic[i]) return false;
        }

        return true;
    }

    // ----------------------------------------------------------------- ASCII parsing

    static List<NgonRaw> ParseAscii(string text, Color defaultColor)
    {
        string clean = StripComments(text);
        var ngons = new List<NgonRaw>();

        var searchFrom = 0;

        while (TryFindBlock(clean, "Geometry", searchFrom, out int contentStart, out int contentEnd, out int afterEnd))
        {
            string body = clean.Substring(contentStart, contentEnd - contentStart);
            ProcessGeometryBody(body, defaultColor, ngons);
            searchFrom = afterEnd;
        }

        return ngons;
    }

    static void ProcessGeometryBody(string body, Color defaultColor, List<NgonRaw> output)
    {
        List<float>? verts = ExtractFloatArray(body, "Vertices");
        if (verts == null || verts.Count < 9) return;
        List<int>? polyIdx = ExtractIntArray(body, "PolygonVertexIndex");
        if (polyIdx == null || polyIdx.Count < 3) return;

        int vcount = verts.Count / 3;
        var vertices = new Vector3[vcount];

        for (var i = 0; i < vcount; i++)
        {
            vertices[i] = new Vector3(verts[i * 3], verts[i * 3 + 1], verts[i * 3 + 2]);
        }

        // Split PolygonVertexIndex into polygons: negative index = end of polygon, real index = ~raw.
        var polys = new List<List<int>>();
        var current = new List<int>();

        foreach (int raw in polyIdx)
        {
            if (raw >= 0) current.Add(raw);
            else
            {
                current.Add(~raw);
                polys.Add(current);
                current = new List<int>();
            }
        }

        if (polys.Count == 0) return;

        Color[] polyColors = ExtractPolygonColors(body, polys, defaultColor);

        for (var p = 0; p < polys.Count; p++)
        {
            var ngon = new NgonRaw
            {
                Vertices = new List<Vector3>(polys[p].Count),
                Color = polyColors[p],
            };

            foreach (int idx in polys[p])
                if (idx >= 0 && idx < vertices.Length)
                    ngon.Vertices.Add(vertices[idx]);

            if (ngon.Vertices.Count >= 3)
                output.Add(ngon);
        }
    }

    static Color[] ExtractPolygonColors(string body, List<List<int>> polys, Color defaultColor)
    {
        var result = new Color[polys.Count];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = defaultColor;
        }

        string? colorBlock = ExtractBlockBody(body, "LayerElementColor");
        if (colorBlock == null) return result;

        string mapping = ExtractStringValue(colorBlock, "MappingInformationType") ?? "AllSame";
        string reference = ExtractStringValue(colorBlock, "ReferenceInformationType") ?? "Direct";
        List<float>? colorsFloat = ExtractFloatArray(colorBlock, "Colors");
        if (colorsFloat == null || colorsFloat.Count < 4) return result;
        List<int>? colorIndices = ExtractIntArray(colorBlock, "ColorIndex");

        int paletteCount = colorsFloat.Count / 4;
        var palette = new Color[paletteCount];

        for (var i = 0; i < paletteCount; i++)
        {
            palette[i] = new Color(
                colorsFloat[i * 4 + 0],
                colorsFloat[i * 4 + 1],
                colorsFloat[i * 4 + 2],
                colorsFloat[i * 4 + 3]);
        }

        bool indexToDirect = reference == "IndexToDirect" && colorIndices != null;

        if (mapping == "AllSame")
        {
            int idx = indexToDirect && colorIndices!.Count > 0 ? colorIndices[0] : 0;

            if (idx >= 0 && idx < paletteCount)
            {
                for (var i = 0; i < result.Length; i++)
                {
                    result[i] = palette[idx];
                }
            }
        }
        else if (mapping == "ByPolygon")
        {
            for (var p = 0; p < polys.Count; p++)
            {
                int idx = indexToDirect && p < colorIndices!.Count ? colorIndices[p] : p;
                if (idx >= 0 && idx < paletteCount) result[p] = palette[idx];
            }
        }
        else if (mapping == "ByPolygonVertex")
        {
            // Use the color of the first vertex of each polygon.
            var cursor = 0;

            for (var p = 0; p < polys.Count; p++)
            {
                int idx = indexToDirect && cursor < colorIndices!.Count ? colorIndices[cursor] : cursor;
                if (idx >= 0 && idx < paletteCount) result[p] = palette[idx];
                cursor += polys[p].Count;
            }
        }
        else if (mapping == "ByVertex")
        {
            for (var p = 0; p < polys.Count; p++)
            {
                int firstVertex = polys[p][0];

                int idx = indexToDirect && firstVertex < colorIndices!.Count
                    ? colorIndices[firstVertex] : firstVertex;
                if (idx >= 0 && idx < paletteCount) result[p] = palette[idx];
            }
        }

        return result;
    }

    // ----------------------------------------------------------------- low-level helpers

    static string StripComments(string text)
    {
        var sb = new StringBuilder(text.Length);

        foreach (string line in text.Split('\n'))
        {
            int semi = IndexOfOutsideQuotes(line, ';');
            sb.AppendLine(semi >= 0 ? line.Substring(0, semi) : line);
        }

        return sb.ToString();
    }

    static int IndexOfOutsideQuotes(string s, char ch)
    {
        var inQuotes = false;

        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '"') inQuotes = !inQuotes;
            else if (!inQuotes && s[i] == ch) return i;
        }

        return -1;
    }

    // Finds a block of the form `keyword: ... { ... }` starting at from.
    // Returns the content range (between braces) and the position just after the closing brace.
    // Correctly handles nested blocks and quoted strings.
    static bool TryFindBlock
    (string text, string keyword, int from,
        out int contentStart, out int contentEnd, out int afterEnd)
    {
        contentStart = -1;
        contentEnd = -1;
        afterEnd = -1;
        int idx = from;

        while ((idx = text.IndexOf(keyword, idx, StringComparison.Ordinal)) >= 0)
        {
            // Skip if part of a longer identifier.
            if (idx > 0 && (char.IsLetterOrDigit(text[idx - 1]) || text[idx - 1] == '_'))
            {
                idx++;
                continue;
            }

            int p = idx + keyword.Length;
            while (p < text.Length && char.IsWhiteSpace(text[p])) p++;

            if (p >= text.Length || text[p] != ':')
            {
                idx++;
                continue;
            }

            int brace = text.IndexOf('{', p);
            if (brace < 0) return false;

            var depth = 1;
            int q = brace + 1;

            while (q < text.Length && depth > 0)
            {
                char c = text[q];

                switch (c)
                {
                    case '"':
                    {
                        q++;
                        while (q < text.Length && text[q] != '"') q++;
                        break;
                    }
                    case '{':
                        depth++;
                        break;
                    case '}':
                        depth--;
                        break;
                }

                q++;
            }

            contentStart = brace + 1;
            contentEnd = q - 1;
            afterEnd = q;
            return true;
        }

        return false;
    }

    static string? ExtractBlockBody(string text, string keyword) => TryFindBlock(text, keyword, 0, out int s, out int e, out _)
        ? text.Substring(s, e - s) : null;

    static List<float>? ExtractFloatArray(string body, string key)
    {
        string? content = ExtractBlockBody(body, key);
        if (content == null) return null;

        int aIdx = content.IndexOf("a:", StringComparison.Ordinal);
        if (aIdx < 0) return null;

        var values = new List<float>();
        string[] parts = content.Substring(aIdx + 2).Split(',');

        foreach (string part in parts)
        {
            string s = part.Trim();
            if (s.Length == 0) continue;
            var end = 0;

            while (end < s.Length &&
                (char.IsDigit(s[end]) || s[end] == '-' || s[end] == '+' ||
                    s[end] == '.' || s[end] == 'e' || s[end] == 'E')) end++;
            if (end == 0) continue;

            if (float.TryParse(s.Substring(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                values.Add(v);
        }

        return values;
    }

    static List<int>? ExtractIntArray(string body, string key)
    {
        List<float>? floats = ExtractFloatArray(body, key);
        if (floats == null) return null;
        var ints = new List<int>(floats.Count);
        foreach (float f in floats) ints.Add((int)f);
        return ints;
    }

    static string? ExtractStringValue(string body, string key)
    {
        var idx = 0;

        while ((idx = body.IndexOf(key, idx, StringComparison.Ordinal)) >= 0)
        {
            if (idx > 0 && (char.IsLetterOrDigit(body[idx - 1]) || body[idx - 1] == '_'))
            {
                idx++;
                continue;
            }

            int p = idx + key.Length;
            while (p < body.Length && char.IsWhiteSpace(body[p])) p++;

            if (p >= body.Length || body[p] != ':')
            {
                idx++;
                continue;
            }

            p++;
            while (p < body.Length && char.IsWhiteSpace(body[p])) p++;

            if (p >= body.Length || body[p] != '"')
            {
                idx++;
                continue;
            }

            int qStart = p + 1;
            int qEnd = body.IndexOf('"', qStart);
            if (qEnd < 0) return null;
            return body.Substring(qStart, qEnd - qStart);
        }

        return null;
    }
}
