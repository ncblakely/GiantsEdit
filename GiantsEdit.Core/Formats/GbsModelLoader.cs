using System.Numerics;

namespace GiantsEdit.Core.Formats;

/// <summary>
/// Material/part definition within a GBS model.
/// </summary>
public class GbsMaterial
{
    public string Name { get; set; } = string.Empty;
    public int ObjectIndex;
    public int FaceCount;
    public int WordZ;
    public ushort Refs_;
    public int RefStart;
    public int RefNum;
    public string TextureName { get; set; } = string.Empty;
    public string BumpTextureName { get; set; } = string.Empty;
    public float Falloff;
    public float Blend;
    public int Flags;
    public uint Emissive;
    public uint Ambient;
    public uint Diffuse;
    public uint Specular;
    public float Power;

    /// <summary>Triangle indices — each int[3] is one face referencing point indices.</summary>
    public List<ushort[]> Triangles { get; } = [];
}

/// <summary>
/// In-memory representation of a GBS (Giants Binary Shape) model.
/// </summary>
public class GbsModel
{
    public int Magic;
    /// <summary>
    /// Flags read from the GBS file header.
    /// Determines which data sections are present and whether normals/lighting apply.
    /// </summary>
    public int OptionsFlags;

    public const int HasNormalsFlag = 0x0001;
    public const int HasUVsFlag = 0x0002;
    public const int HasRGBsFlag = 0x0004;
    public const int CalcNormalsFlag = 0x0008;

    /// <summary>
    /// True if the file contains normal definition data (ndefs block present).
    /// </summary>
    public bool HasNormalData => (OptionsFlags & HasNormalsFlag) != 0;

    /// <summary>
    /// True if the model uses vertex normals for lighting (either stored or computed at runtime).
    /// </summary>
    public bool HasNormals => (OptionsFlags & (HasNormalsFlag | CalcNormalsFlag)) != 0;

    /// <summary>Base vertex positions (X, Y, Z).</summary>
    public Vector3[] BasePoints = [];

    // Extended format (u1==7) only
    public int TexPos;
    public int VertexRefCount;
    public ushort[] VertexRefs = [];

    /// <summary>Number of "points" (texture/color vertices).</summary>
    public int PointCount;

    /// <summary>Point-to-basepoint index mapping.</summary>
    public ushort[] PointIndices1 = [];
    /// <summary>Secondary point indices (format v7 only).</summary>
    public ushort[] PointIndices2 = [];

    /// <summary>UV coordinates per point [pointIndex][0=U, 1=V].</summary>
    public float[][] PointUVs = [];

    /// <summary>Vertex colors per point (R, G, B), 3 bytes per point.</summary>
    public byte[] PointColors = [];

    /// <summary>Reference data, 5 ints (20 bytes) per entry.</summary>
    public int[][] Ref1 = [];

    /// <summary>Mesh parts with materials and triangle lists.</summary>
    public List<GbsMaterial> Parts { get; } = [];

    /// <summary>Bounding box min corner.</summary>
    public Vector3 BoundsMin;
    /// <summary>Bounding box max corner.</summary>
    public Vector3 BoundsMax;
    /// <summary>Max distance from origin to any vertex.</summary>
    public float MaxBoundRadius;
}

/// <summary>
/// Loads GBS (Giants Binary Shape) model files.
/// Ported from Delphi's objectmanager.pas LoadModel procedure.
/// </summary>
public static class GbsModelLoader
{
    public static GbsModel Load(byte[] data)
    {
        var model = new GbsModel();
        int pos = 0;

        model.Magic = ReadInt32(data, ref pos);
        model.OptionsFlags = ReadInt32(data, ref pos);
        int basePointCount = ReadInt32(data, ref pos);

        // Read base points (12 bytes each: 3 floats)
        model.BasePoints = new Vector3[basePointCount];
        for (int i = 0; i < basePointCount; i++)
        {
            float x = ReadSingle(data, ref pos);
            float y = ReadSingle(data, ref pos);
            float z = ReadSingle(data, ref pos);
            model.BasePoints[i] = new Vector3(x, y, z);
        }

        // Normal definitions (only present when GBXFlagNormals is set in the file)
        if (model.HasNormalData)
        {
            model.TexPos = ReadInt32(data, ref pos);
            model.VertexRefCount = ReadInt32(data, ref pos);
            model.VertexRefs = new ushort[model.VertexRefCount];
            for (int i = 0; i < model.VertexRefCount; i++)
                model.VertexRefs[i] = ReadUInt16(data, ref pos);
        }

        // Points (texture/color vertices)
        model.PointCount = ReadInt32(data, ref pos);
        int pc = model.PointCount;

        model.PointIndices1 = new ushort[pc];
        for (int i = 0; i < pc; i++)
            model.PointIndices1[i] = ReadUInt16(data, ref pos);

        // Normal indices (only present in file when GBXFlagNormals is set)
        if (model.HasNormalData)
        {
            model.PointIndices2 = new ushort[pc];
            for (int i = 0; i < pc; i++)
                model.PointIndices2[i] = ReadUInt16(data, ref pos);
        }

        // UVs (8 bytes per point)
        model.PointUVs = new float[pc][];
        for (int i = 0; i < pc; i++)
        {
            float u = ReadSingle(data, ref pos);
            float v = ReadSingle(data, ref pos);
            model.PointUVs[i] = [u, v];
        }

        // Vertex colors (3 bytes per point: RGB)
        model.PointColors = new byte[pc * 3];
        Buffer.BlockCopy(data, pos, model.PointColors, 0, pc * 3);
        pos += pc * 3;

        // Ref1 array (20 bytes per entry: 5 ints)
        int ref1Count = ReadInt32(data, ref pos);
        model.Ref1 = new int[ref1Count][];
        for (int i = 0; i < ref1Count; i++)
        {
            model.Ref1[i] = new int[5];
            for (int j = 0; j < 5; j++)
                model.Ref1[i][j] = ReadInt32(data, ref pos);
        }

        // Parts
        int partCount = ReadInt32(data, ref pos);
        for (int p = 0; p < partCount; p++)
        {
            var mat = new GbsMaterial();

            // 46 bytes: objname(32) + objindex(4) + refs(4) + wordz(4) + refs_(2)
            mat.Name = ReadFixedString(data, ref pos, 32);
            mat.ObjectIndex = ReadInt32(data, ref pos);
            mat.FaceCount = ReadInt32(data, ref pos);
            mat.WordZ = ReadInt32(data, ref pos);
            mat.Refs_ = ReadUInt16(data, ref pos);

            // Triangles: FaceCount * 6 bytes (3 ushorts per face)
            for (int f = 0; f < mat.FaceCount; f++)
            {
                ushort v0 = ReadUInt16(data, ref pos);
                ushort v1 = ReadUInt16(data, ref pos);
                ushort v2 = ReadUInt16(data, ref pos);
                mat.Triangles.Add([v0, v1, v2]);
            }

            // 104 bytes: refstart(4) + refnum(4) + texture(32) + bumptexture(32) +
            //   falloff(4) + blend(4) + flags(4) + emissive(4) + ambient(4) + diffuse(4) + specular(4) + power(4)
            mat.RefStart = ReadInt32(data, ref pos);
            mat.RefNum = ReadInt32(data, ref pos);
            mat.TextureName = ReadFixedString(data, ref pos, 32);
            mat.BumpTextureName = ReadFixedString(data, ref pos, 32);
            mat.Falloff = ReadSingle(data, ref pos);
            mat.Blend = ReadSingle(data, ref pos);
            mat.Flags = ReadInt32(data, ref pos);
            mat.Emissive = ReadUInt32(data, ref pos);
            mat.Ambient = ReadUInt32(data, ref pos);
            mat.Diffuse = ReadUInt32(data, ref pos);
            mat.Specular = ReadUInt32(data, ref pos);
            mat.Power = ReadSingle(data, ref pos);

            model.Parts.Add(mat);
        }

        // Compute bounding box
        ComputeBounds(model);

        return model;
    }

    private static void ComputeBounds(GbsModel model)
    {
        if (model.BasePoints.Length == 0) return;

        model.BoundsMin = model.BasePoints[0];
        model.BoundsMax = model.BasePoints[0];
        model.MaxBoundRadius = 0;

        foreach (var pt in model.BasePoints)
        {
            float r = pt.Length();
            if (r > model.MaxBoundRadius) model.MaxBoundRadius = r;

            model.BoundsMin = Vector3.Min(model.BoundsMin, pt);
            model.BoundsMax = Vector3.Max(model.BoundsMax, pt);
        }
    }

    #region Low-level read helpers

    private static int ReadInt32(byte[] data, ref int pos)
    {
        int v = BitConverter.ToInt32(data, pos);
        pos += 4;
        return v;
    }

    private static uint ReadUInt32(byte[] data, ref int pos)
    {
        uint v = BitConverter.ToUInt32(data, pos);
        pos += 4;
        return v;
    }

    private static ushort ReadUInt16(byte[] data, ref int pos)
    {
        ushort v = BitConverter.ToUInt16(data, pos);
        pos += 2;
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
        string s = System.Text.Encoding.ASCII.GetString(data, pos, strLen);
        pos = end;
        return s;
    }

    #endregion
}

/// <summary>
/// Custom LZ77-like decompression used by the Giants engine for GBS files.
/// Ported from Delphi's bin_w_read.pas Decompress procedure.
/// </summary>
public static class GbsDecompressor
{
    private const int VBufStart = 0xFEE; // 4078

    /// <summary>
    /// Decompresses a GBS compressed buffer.
    /// </summary>
    /// <param name="buf">Source buffer containing compressed data.</param>
    /// <param name="start">Start offset within the buffer.</param>
    /// <param name="compressedLength">Length of compressed data.</param>
    /// <param name="finalSize">Expected decompressed size.</param>
    public static byte[] Decompress(byte[] buf, int start, int compressedLength, int finalSize)
    {
        var output = new byte[finalSize];
        int i = start;
        int j = 0;
        int vbufstart = VBufStart;
        int decbits = 8;
        byte decbyte = 0;

        while (j < finalSize && i < start + compressedLength)
        {
            if (decbits == 8)
            {
                decbyte = buf[i++];
                decbits = 0;
            }

            if (((decbyte >> decbits) & 1) == 0)
            {
                // Compressed: copy from history
                if (i + 1 >= buf.Length) break;

                int b0 = buf[i];
                int b1 = buf[i + 1];
                i += 2;

                int decpos = (b0 + ((b1 & 0xF0) << 4) - vbufstart - j) & 0xFFF;
                decpos = decpos - 4096 + j;
                int declen = (b1 & 0x0F) + 3;

                for (int n = 0; n < declen && j < finalSize; n++, decpos++, j++)
                {
                    output[j] = decpos >= 0 && decpos < j ? output[decpos] : (byte)0x20;
                }
            }
            else
            {
                // Literal byte
                output[j++] = buf[i++];
            }

            decbits++;
        }

        return output;
    }
}
