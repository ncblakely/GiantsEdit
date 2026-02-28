using System.Numerics;

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

        var r = new BinaryDataReader(data);
        int version = r.ReadInt32();
        if (version != VersionCurrent)
            return null;

        int numObjects = r.ReadInt32();
        if (numObjects <= 0) return null;

        // Read offset table
        int tableStart = r.Position;
        for (int i = 0; i < numObjects; i++)
        {
            r.Position = tableStart + i * 4;
            int offset = r.ReadInt32();
            r.Position = offset;

            string name = r.ReadFixedString(16);
            if (!name.Equals(objectName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Found the object — parse it
            return ParseObject(r, name);
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

        var r = new BinaryDataReader(data);
        int version = r.ReadInt32();
        if (version != VersionCurrent)
            return names;

        int numObjects = r.ReadInt32();
        int tableStart = r.Position;
        for (int i = 0; i < numObjects; i++)
        {
            r.Position = tableStart + i * 4;
            int offset = r.ReadInt32();
            r.Position = offset;
            names.Add(r.ReadFixedString(16));
        }

        return names;
    }

    private static Gb2Object ParseObject(BinaryDataReader r, string name)
    {
        var obj = new Gb2Object { Name = name };

        obj.Flags = r.ReadInt32();
        obj.Falloff = r.ReadSingle();

        if ((obj.Flags & FlagRGBs) != 0)
            r.Skip(4); // skip blend float

        r.Skip(4); // skip matflags int

        if ((obj.Flags & FlagUVs) != 0)
            obj.TextureName = r.ReadFixedString(16);

        int nverts = r.ReadInt32();
        int ntris = r.ReadInt32();

        // Read vertices
        obj.Vertices = new Vector3[nverts];
        for (int i = 0; i < nverts; i++)
        {
            float x = r.ReadSingle();
            float y = r.ReadSingle();
            float z = r.ReadSingle();
            obj.Vertices[i] = new Vector3(x, y, z);
        }

        // Skip normals if present
        if ((obj.Flags & FlagNormals) != 0)
            r.Skip(nverts * 12);

        // Read UVs
        if ((obj.Flags & FlagUVs) != 0)
        {
            obj.UVs = new float[nverts][];
            for (int i = 0; i < nverts; i++)
            {
                float u = r.ReadSingle();
                float v = r.ReadSingle();
                obj.UVs[i] = [u, v];
            }
        }

        // Skip RGBs if present
        if ((obj.Flags & FlagRGBs) != 0)
            r.Skip(nverts * 3);

        // Read triangles (int32 indices, unlike GBS which uses uint16)
        obj.Triangles = new int[ntris * 3];
        for (int i = 0; i < ntris * 3; i++)
            obj.Triangles[i] = r.ReadInt32();

        return obj;
    }
}
