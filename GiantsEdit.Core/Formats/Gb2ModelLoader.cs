using System.Numerics;
using System.Text;

namespace GiantsEdit.Core.Formats;

/// <summary>
/// A single object parsed from a GB2 file (used for dome, sea, waterfalls).
/// </summary>
public class Gb2Object
{
    public string Name { get; set; } = string.Empty;
    public int Flags { get; set; }
    public float Falloff { get; set; }
    public string TextureName { get; set; } = string.Empty;
    public Vector3[] Vertices { get; set; } = [];
    public float[][] UVs { get; set; } = [];
    public int[] Triangles { get; set; } = [];

    public bool HasUVs => (Flags & 0x0002) != 0;
}

/// <summary>
/// Loads GB2 world object files (World.gb2 from extra.gzp).
/// These contain simple single-part meshes used for the sky dome, sea, etc.
/// </summary>
public static class Gb2ModelLoader
{
    private const int VersionCurrent = unchecked((int)0xAA0100AB);
    private const int FlagNormals = 0x0001;
    private const int FlagUVs = 0x0002;
    private const int FlagRGBs = 0x0004;

    /// <summary>
    /// Loads a named object from a GB2 file.
    /// Returns null if the object is not found or the file is invalid.
    /// </summary>
    public static Gb2Object? Load(byte[] data, string objectName)
    {
        if (data.Length < 8) return null;

        int pos = 0;
        int version = ReadInt32(data, ref pos);
        if (version != VersionCurrent)
            return null;

        int numObjects = ReadInt32(data, ref pos);
        if (numObjects <= 0) return null;

        // Read offset table
        int tableStart = pos;
        for (int i = 0; i < numObjects; i++)
        {
            pos = tableStart + i * 4;
            int offset = ReadInt32(data, ref pos);
            pos = offset;

            string name = ReadFixedString(data, ref pos, 16);
            if (!name.Equals(objectName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Found the object — parse it
            return ParseObject(data, pos, name);
        }

        return null;
    }

    /// <summary>
    /// Lists all object names in a GB2 file.
    /// </summary>
    public static List<string> ListObjects(byte[] data)
    {
        var names = new List<string>();
        if (data.Length < 8) return names;

        int pos = 0;
        int version = ReadInt32(data, ref pos);
        if (version != VersionCurrent)
            return names;

        int numObjects = ReadInt32(data, ref pos);
        int tableStart = pos;
        for (int i = 0; i < numObjects; i++)
        {
            pos = tableStart + i * 4;
            int offset = ReadInt32(data, ref pos);
            pos = offset;
            names.Add(ReadFixedString(data, ref pos, 16));
        }

        return names;
    }

    private static Gb2Object ParseObject(byte[] data, int pos, string name)
    {
        var obj = new Gb2Object { Name = name };

        obj.Flags = ReadInt32(data, ref pos);
        obj.Falloff = ReadSingle(data, ref pos);

        if ((obj.Flags & FlagRGBs) != 0)
            pos += 4; // skip blend float

        pos += 4; // skip matflags int

        if ((obj.Flags & FlagUVs) != 0)
            obj.TextureName = ReadFixedString(data, ref pos, 16);

        int nverts = ReadInt32(data, ref pos);
        int ntris = ReadInt32(data, ref pos);

        // Read vertices
        obj.Vertices = new Vector3[nverts];
        for (int i = 0; i < nverts; i++)
        {
            float x = ReadSingle(data, ref pos);
            float y = ReadSingle(data, ref pos);
            float z = ReadSingle(data, ref pos);
            obj.Vertices[i] = new Vector3(x, y, z);
        }

        // Skip normals if present
        if ((obj.Flags & FlagNormals) != 0)
            pos += nverts * 12;

        // Read UVs
        if ((obj.Flags & FlagUVs) != 0)
        {
            obj.UVs = new float[nverts][];
            for (int i = 0; i < nverts; i++)
            {
                float u = ReadSingle(data, ref pos);
                float v = ReadSingle(data, ref pos);
                obj.UVs[i] = [u, v];
            }
        }

        // Skip RGBs if present
        if ((obj.Flags & FlagRGBs) != 0)
            pos += nverts * 3;

        // Read triangles (int32 indices, unlike GBS which uses uint16)
        obj.Triangles = new int[ntris * 3];
        for (int i = 0; i < ntris * 3; i++)
            obj.Triangles[i] = ReadInt32(data, ref pos);

        return obj;
    }

    #region Read helpers

    private static int ReadInt32(byte[] data, ref int pos)
    {
        int v = BitConverter.ToInt32(data, pos);
        pos += 4;
        return v;
    }

    private static float ReadSingle(byte[] data, ref int pos)
    {
        float v = BitConverter.ToSingle(data, pos);
        pos += 4;
        return v;
    }

    private static string ReadFixedString(byte[] data, ref int pos, int length)
    {
        int end = pos + length;
        int nullPos = Array.IndexOf(data, (byte)0, pos, length);
        int strLen = nullPos >= 0 ? nullPos - pos : length;
        string s = Encoding.ASCII.GetString(data, pos, strLen);
        pos = end;
        return s;
    }

    #endregion
}
