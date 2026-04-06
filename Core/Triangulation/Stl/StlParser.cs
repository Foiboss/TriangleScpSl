using System.Globalization;
using System.Text;
using UnityEngine;

namespace Triangle.Core.Triangulation.Stl;

public static class StlParser
{
    const int BinaryHeaderLength = 80;
    const int BinaryTriangleLength = 50;

    public static bool TryParseFile(string filePath, out List<StlTriangle> triangles, out string error)
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
                if (TryParseBinary(stream, out triangles, out error))
                    return true;

                stream.Position = 0;
                return TryParseAscii(stream, out triangles, out error);
            }

            if (TryParseAscii(stream, out triangles, out error))
                return true;

            stream.Position = 0;
            return TryParseBinary(stream, out triangles, out error);
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

    static bool TryParseBinary(Stream stream, out List<StlTriangle> triangles, out string error)
    {
        triangles = [];
        error = string.Empty;

        try
        {
            stream.Position = 0;
            using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
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

            triangles = new List<StlTriangle>((int)Math.Min(triangleCount, int.MaxValue));

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

                if (!IsFinite(p1) || !IsFinite(p2) || !IsFinite(p3))
                    continue;

                triangles.Add(new StlTriangle(p1, p2, p3));
            }

            if (triangles.Count == 0)
            {
                error = "No valid triangles found in binary STL.";
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

    static bool TryParseAscii(Stream stream, out List<StlTriangle> triangles, out string error)
    {
        triangles = [];
        error = string.Empty;

        try
        {
            stream.Position = 0;
            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);

            List<Vector3> vertices = [];

            while (reader.ReadLine() is { } line)
            {
                line = line.Trim();
                if (!line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4)
                    continue;

                if (!TryParseFloat(parts[1], out float x) ||
                    !TryParseFloat(parts[2], out float y) ||
                    !TryParseFloat(parts[3], out float z))
                {
                    continue;
                }

                Vector3 vertex = new(x, y, z);
                if (!IsFinite(vertex))
                    continue;

                vertices.Add(vertex);
            }

            if (vertices.Count < 3)
            {
                error = "ASCII STL does not contain enough vertices.";
                return false;
            }

            triangles = new List<StlTriangle>(vertices.Count / 3);
            for (int i = 0; i + 2 < vertices.Count; i += 3)
                triangles.Add(new StlTriangle(vertices[i], vertices[i + 1], vertices[i + 2]));

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

    static bool IsFinite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);
}



