using System.Runtime.InteropServices;

namespace GiantsEdit.Core.Formats;

/// <summary>
/// GTI terrain file header (96 bytes).
/// Ported from Delphi's tTerrainheader record in Unit1.pas.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
public struct GtiHeader
{
    public const int Signature = -1802088445;
    public const int Size = 96;

    public int SignatureField;
    public int U0;
    public float XOffset;
    public float YOffset;
    public float MinHeight;
    public float MaxHeight;
    public int Width;   // xl
    public int Height;  // yl
    public float Stretch;
    public float U1;    // default 0.5
    public float U2;    // default 0.5
    public float U3;    // default 1.25e-4
    public float U4;    // default 1.25e-4
    public ushort Version; // =3
    public ushort U6;   // default 1
    public float U7;    // default 0
    public float U8;    // default 1
    // 32 bytes for texture name
    // (handled separately since fixed char arrays need special treatment)
}

/// <summary>
/// In-memory terrain data.
/// </summary>
public class TerrainData
{
    public GtiHeader Header;
    public string TextureName = string.Empty;
    public float[] Heights = [];
    public byte[] Triangles = [];
    /// <summary>RGB lightmap, 3 bytes per cell (R, G, B).</summary>
    public byte[] LightMap = [];

    public int Width => Header.Width;
    public int Height => Header.Height;

    public float GetHeight(int x, int y) => Heights[y * Width + x];
    public void SetHeight(int x, int y, float h) => Heights[y * Width + x] = h;
}

/// <summary>
/// Reads and writes GTI terrain files.
/// Ported from Delphi's Unit1.pas DecodeGTI / SaveGTIToStream.
/// </summary>
public static class GtiFormat
{
    private const int TextureNameOffset = 64;
    private const int TextureNameLength = 32;
    private const int HeaderTotalSize = 96; // 64 bytes fields + 32 bytes texture name

    /// <summary>
    /// Loads terrain data from a GTI byte array.
    /// The voxel data after the 96-byte header is RLE-compressed:
    ///   byte b >= 0x80: skip (256-b) cells (fill with defaults)
    ///   byte b &lt; 0x80: read (b+1) literal cells, each 8 bytes (version!=7) or 9 bytes (version==7)
    /// Each literal cell: float height(4) + byte triangle(1) + byte R(1) + byte G(1) + byte B(1)
    /// </summary>
    public static TerrainData Load(byte[] gti)
    {
        if (gti.Length < HeaderTotalSize)
            throw new FormatException("GTI file too small for header.");

        var terrain = new TerrainData();

        // Read header fields
        var reader = new BinaryDataReader(gti);
        terrain.Header.SignatureField = reader.ReadInt32();
        terrain.Header.U0 = reader.ReadInt32();
        terrain.Header.XOffset = reader.ReadSingle();
        terrain.Header.YOffset = reader.ReadSingle();
        terrain.Header.MinHeight = reader.ReadSingle();
        terrain.Header.MaxHeight = reader.ReadSingle();
        terrain.Header.Width = reader.ReadInt32();
        terrain.Header.Height = reader.ReadInt32();
        terrain.Header.Stretch = reader.ReadSingle();
        terrain.Header.U1 = reader.ReadSingle();
        terrain.Header.U2 = reader.ReadSingle();
        terrain.Header.U3 = reader.ReadSingle();
        terrain.Header.U4 = reader.ReadSingle();
        terrain.Header.Version = reader.ReadWord();
        terrain.Header.U6 = reader.ReadWord();
        terrain.Header.U7 = reader.ReadSingle();
        terrain.Header.U8 = reader.ReadSingle();

        // Read texture name (32 bytes at offset 64)
        terrain.TextureName = reader.ReadFixedString(TextureNameLength);

        int w = terrain.Header.Width;
        int h = terrain.Header.Height;
        if (w < 2 || w > 4096 || h < 2 || h > 4096)
            throw new FormatException($"GTI dimensions out of range: {w}x{h}");
        if (terrain.Header.SignatureField != GtiHeader.Signature)
            throw new FormatException($"GTI signature mismatch: {terrain.Header.SignatureField}");

        int cellCount = w * h;
        terrain.Heights = new float[cellCount];
        terrain.Triangles = new byte[cellCount];
        terrain.LightMap = new byte[cellCount * 3];

        // Initialize defaults (matching Delphi: height=minHeight, light=magenta, triangle=0)
        float defaultHeight = terrain.Header.MinHeight;
        for (int i = 0; i < cellCount; i++)
        {
            terrain.Heights[i] = defaultHeight;
            terrain.LightMap[i * 3 + 0] = 255; // R
            terrain.LightMap[i * 3 + 1] = 0;   // G
            terrain.LightMap[i * 3 + 2] = 255; // B
        }

        // Decode RLE voxel data
        int pos = HeaderTotalSize;
        int cellIdx = 0;
        int voxelSize = terrain.Header.Version == 7 ? 9 : 8;

        while (cellIdx < cellCount && pos < gti.Length)
        {
            byte b = gti[pos++];

            if (b >= 0x80)
            {
                // Skip run: skip (256 - b) cells
                int skipCount = 256 - b;
                cellIdx += skipCount;
            }
            else
            {
                // Literal run: read (b + 1) cells
                int litCount = b + 1;
                for (int i = 0; i < litCount && cellIdx < cellCount; i++)
                {
                    if (pos + voxelSize > gti.Length)
                        break;

                    terrain.Heights[cellIdx] = BitConverter.ToSingle(gti, pos);
                    terrain.Triangles[cellIdx] = gti[pos + 4];
                    terrain.LightMap[cellIdx * 3 + 0] = gti[pos + 5];
                    terrain.LightMap[cellIdx * 3 + 1] = gti[pos + 6];
                    terrain.LightMap[cellIdx * 3 + 2] = gti[pos + 7];
                    pos += voxelSize;
                    cellIdx++;
                }
            }
        }

        // Normalize version 7 → 3 (matching Delphi)
        if (terrain.Header.Version == 7)
            terrain.Header.Version = 3;

        return terrain;
    }

    /// <summary>
    /// Saves terrain data to a GTI byte array with RLE compression.
    /// Builds a mask of "active" vertices (those used by any non-empty triangle),
    /// then encodes: literal runs (b = count-1, then count×8 bytes) and
    /// skip runs (b = 256-count).
    /// </summary>
    public static byte[] Save(TerrainData terrain)
    {
        int w = terrain.Width;
        int h = terrain.Height;
        int cellCount = w * h;

        // Update min/max height
        terrain.Header.MinHeight = float.MaxValue;
        terrain.Header.MaxHeight = float.MinValue;
        for (int i = 0; i < cellCount; i++)
        {
            float ht = terrain.Heights[i];
            if (ht < terrain.Header.MinHeight) terrain.Header.MinHeight = ht;
            if (ht > terrain.Header.MaxHeight) terrain.Header.MaxHeight = ht;
        }

        if (terrain.Header.Version == 7)
            terrain.Header.Version = 3;

        // Max possible size: header + 1 byte per cell + 8 bytes per cell
        var writer = new BinaryDataWriter(HeaderTotalSize + cellCount * 9 + 256);

        // Write header
        writer.WriteInt32(terrain.Header.SignatureField);
        writer.WriteInt32(terrain.Header.U0);
        writer.WriteSingle(terrain.Header.XOffset);
        writer.WriteSingle(terrain.Header.YOffset);
        writer.WriteSingle(terrain.Header.MinHeight);
        writer.WriteSingle(terrain.Header.MaxHeight);
        writer.WriteInt32(terrain.Header.Width);
        writer.WriteInt32(terrain.Header.Height);
        writer.WriteSingle(terrain.Header.Stretch);
        writer.WriteSingle(terrain.Header.U1);
        writer.WriteSingle(terrain.Header.U2);
        writer.WriteSingle(terrain.Header.U3);
        writer.WriteSingle(terrain.Header.U4);
        writer.WriteWord(terrain.Header.Version);
        writer.WriteWord(terrain.Header.U6);
        writer.WriteSingle(terrain.Header.U7);
        writer.WriteSingle(terrain.Header.U8);
        writer.WriteFixedString(terrain.TextureName, TextureNameLength);

        // Build active mask (matching Delphi's SaveGTIToStream logic)
        var mask = new bool[cellCount];
        bool IsActive(int triType) => (triType & 7) is >= 1 and <= 7;

        for (int y = 1; y < h; y++)
            for (int x = 1; x < w; x++)
                mask[y * w + x] =
                    IsActive(terrain.Triangles[y * w + x]) ||
                    IsActive(terrain.Triangles[y * w + (x - 1)]) ||
                    IsActive(terrain.Triangles[(y - 1) * w + x]) ||
                    IsActive(terrain.Triangles[(y - 1) * w + (x - 1)]);

        // Edge cases: first column (x=0), first row (y=0), origin
        for (int y = 1; y < h; y++)
            mask[y * w] =
                IsActive(terrain.Triangles[y * w]) ||
                IsActive(terrain.Triangles[(y - 1) * w]);
        for (int x = 1; x < w; x++)
            mask[x] =
                IsActive(terrain.Triangles[x]) ||
                IsActive(terrain.Triangles[x - 1]);
        mask[0] = IsActive(terrain.Triangles[0]);

        // RLE encode
        int runStart = 0;
        bool runActive = mask[0];
        // Buffer for literal voxel data (max 128 cells per run)
        var voxelBuf = new byte[128 * 8];

        for (int i = 0; i <= cellCount; i++)
        {
            bool currentActive = i < cellCount && mask[i];
            int runLen = i - runStart;

            if (i == cellCount || currentActive != runActive || runLen == 128)
            {
                if (runLen > 0)
                {
                    if (runActive)
                    {
                        // Literal run: write (runLen-1), then runLen × 8 bytes
                        writer.WriteByte((byte)(runLen - 1));
                        for (int j = 0; j < runLen; j++)
                        {
                            int ci = runStart + j;
                            writer.WriteSingle(terrain.Heights[ci]);
                            writer.WriteByte(terrain.Triangles[ci]);
                            writer.WriteByte(terrain.LightMap[ci * 3 + 0]);
                            writer.WriteByte(terrain.LightMap[ci * 3 + 1]);
                            writer.WriteByte(terrain.LightMap[ci * 3 + 2]);
                        }
                    }
                    else
                    {
                        // Skip run: write (256 - runLen)
                        writer.WriteByte((byte)(256 - runLen));
                    }
                }

                runStart = i;
                if (i < cellCount)
                    runActive = currentActive;
            }
        }

        return writer.ToArray();
    }

    /// <summary>
    /// Creates a new empty terrain with default header values.
    /// </summary>
    public static TerrainData CreateNew(int width, int height, string textureName = "")
    {
        const float stretch = 40.0f;

        var terrain = new TerrainData
        {
            Header = new GtiHeader
            {
                SignatureField = GtiHeader.Signature,
                Width = width,
                Height = height,
                Stretch = stretch,
                XOffset = width * stretch * -0.5f,
                YOffset = height * stretch * -0.5f,
                MinHeight = -40f,
                MaxHeight = 1000f,
                U1 = 0.5f,
                U2 = 0.5f,
                U3 = 1.25e-4f,
                U4 = 1.25e-4f,
                Version = 3,
                U6 = 1,
                U7 = 0f,
                U8 = 1f
            },
            TextureName = textureName,
            Heights = new float[width * height],
            Triangles = new byte[width * height],
            LightMap = new byte[width * height * 3]
        };

        // Default lightmap to white
        Array.Fill(terrain.LightMap, (byte)255);

        // Default all cells to triangle type 1 (both triangles, TL-BR diagonal)
        Array.Fill(terrain.Triangles, (byte)1);

        return terrain;
    }
}
