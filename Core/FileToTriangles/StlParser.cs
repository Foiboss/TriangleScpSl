using System.Globalization;
using System.Text;
using Triangle.Core.TriangulatedModel;
using UnityEngine;

namespace Triangle.Core.FileToTriangles;

public static class StlParser
{
    const int BinaryHeaderLength = 80;
    const int BinaryTriangleLength = 50;

    public static bool TryParseFile(string filePath, Color color, out List<ModelTriangle> triangles, out string error)
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
            using FileStream stream = File.OpenRead(filePath);
            bool looksBinary = LooksLikeBinaryStl(stream);

            if (looksBinary)
            {
                if (TryParseBinary(stream, color, out triangles, out error))
                    return true;

                stream.Position = 0;
                return TryParseAscii(stream, color, out triangles, out error);
            }

            if (TryParseAscii(stream, color, out triangles, out error))
                return true;

            stream.Position = 0;
            return TryParseBinary(stream, color, out triangles, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static bool LooksLikeBinaryStl(Stream stream)
    {
        if (!stream.CanSeek || stream.Length < BinaryHeaderLength + 4)
            return false;

        long originalPosition = stream.Position;

        try
        {
            stream.Position = BinaryHeaderLength;
            byte[] countBytes = new byte[4];

            if (stream.Read(countBytes, 0, countBytes.Length) != 4)
                return false;

            uint triangleCount = BitConverter.ToUInt32(countBytes, 0);
            long expectedLength = BinaryHeaderLength + 4L + triangleCount * BinaryTriangleLength;
            return expectedLength == stream.Length;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    static bool TryParseBinary(Stream stream, Color color, out List<ModelTriangle> triangles, out string error)
    {
        triangles = [];
        error = string.Empty;

        try
        {
            stream.Position = 0;
            using BinaryReader reader = new(stream, Encoding.ASCII, true);
            _ = reader.ReadBytes(BinaryHeaderLength);
            uint triangleCount = reader.ReadUInt32();

            if (triangleCount == 0)
            {
                error = "Binary STL has zero triangles.";
                return false;
            }

            if (stream.CanSeek)
            {
                long requiredLength = BinaryHeaderLength + 4L + triangleCount * BinaryTriangleLength;

                if (stream.Length < requiredLength)
                {
                    error = "Binary STL is truncated.";
                    return false;
                }
            }

            triangles = new List<ModelTriangle>((int)Math.Min(triangleCount, int.MaxValue));

            for (uint i = 0; i < triangleCount; i++)
            {
                // normal
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();

                Vector3 p1 = ReadVector3(reader);
                Vector3 p2 = ReadVector3(reader);
                Vector3 p3 = ReadVector3(reader);

                // attribute byte count
                _ = reader.ReadUInt16();

                triangles.Add(new ModelTriangle(p1, p2, p3, color));
            }

            if (triangles.Count == 0)
            {
                error = "No triangles found in binary STL.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Binary parse failed: {ex.Message}";
            return false;
        }
    }

    static bool TryParseAscii(Stream stream, Color color, out List<ModelTriangle> triangles, out string error)
    {
        triangles = [];
        error = string.Empty;

        try
        {
            stream.Position = 0;
            using StreamReader reader = new(stream, Encoding.UTF8, true, 4096, true);

            List<Vector3> facetVertices = [];

            while (reader.ReadLine() is { } line)
            {
                line = line.Trim();

                if (line.StartsWith("facet", StringComparison.OrdinalIgnoreCase))
                {
                    facetVertices.Clear();
                    continue;
                }

                if (!line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
                {
                    if (!line.StartsWith("endfacet", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Always persist one triangle per facet from the parsed STL text.
                    while (facetVertices.Count < 3)
                        facetVertices.Add(Vector3.zero);

                    triangles.Add(new ModelTriangle(facetVertices[0], facetVertices[1], facetVertices[2], color));
                    facetVertices.Clear();
                    continue;
                }

                string[] parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

                Vector3 vertex = Vector3.zero;

                if (parts.Length >= 4 &&
                    TryParseFloat(parts[1], out float x) &&
                    TryParseFloat(parts[2], out float y) &&
                    TryParseFloat(parts[3], out float z))
                {
                    vertex = new Vector3(x, y, z);
                }

                // Preserve malformed vertex entries so facet triangle count is not reduced.
                facetVertices.Add(vertex);
            }

            if (facetVertices.Count > 0)
            {
                // Persist last unterminated facet if file ended unexpectedly.
                while (facetVertices.Count < 3)
                    facetVertices.Add(Vector3.zero);

                triangles.Add(new ModelTriangle(facetVertices[0], facetVertices[1], facetVertices[2], color));
            }

            if (triangles.Count == 0)
            {
                error = "No triangles parsed from ASCII STL.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"ASCII parse failed: {ex.Message}";
            return false;
        }
    }

    static bool TryParseFloat(string token, out float value) =>
        float.TryParse(token, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

    static Vector3 ReadVector3(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}