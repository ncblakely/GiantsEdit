namespace GiantsEdit.Core.Formats;

/// <summary>
/// Reads greyscale (or color) BMP files as terrain heightmaps.
/// Each pixel maps to one terrain vertex, interpolating between the
/// caller-supplied black-point and white-point heights.
/// Supports 8-bit paletted, 24-bit and 32-bit uncompressed (BI_RGB) BMPs,
/// both bottom-up and top-down row orders.
/// </summary>
public static class BmpHeightmap
{
    public const float DefaultBlackHeight = -40f;
    public const float DefaultWhiteHeight = 700f;

    /// <summary>
    /// Luminance image in top-down row order (row 0 = image top).
    /// </summary>
    public sealed class HeightmapImage
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public byte[] Luminance { get; init; } = [];
    }

    /// <summary>
    /// Parses BMP bytes into a luminance image (top-down row order).
    /// </summary>
    public static HeightmapImage Load(byte[] bmp)
    {
        if (bmp.Length < 54)
            throw new FormatException("BMP file too small for header.");

        if (bmp[0] != (byte)'B' || bmp[1] != (byte)'M')
            throw new FormatException("Not a BMP file (missing 'BM' signature).");

        int pixelOffset = BitConverter.ToInt32(bmp, 10);
        int dibSize = BitConverter.ToInt32(bmp, 14);
        if (dibSize < 40)
            throw new FormatException($"Unsupported BMP DIB header size: {dibSize}.");
        if (bmp.Length < 14 + dibSize)
            throw new FormatException("BMP file truncated in DIB header.");

        int w = BitConverter.ToInt32(bmp, 18);
        int hSigned = BitConverter.ToInt32(bmp, 22);
        ushort planes = BitConverter.ToUInt16(bmp, 26);
        ushort bpp = BitConverter.ToUInt16(bmp, 28);
        int compression = BitConverter.ToInt32(bmp, 30);

        if (planes != 1)
            throw new FormatException($"Unsupported BMP planes value: {planes}.");
        if (compression != 0)
            throw new FormatException($"Unsupported BMP compression ({compression}); only uncompressed BI_RGB is supported.");
        if (bpp is not (8 or 24 or 32))
            throw new FormatException($"Unsupported BMP bit depth: {bpp}. Only 8, 24 and 32 bits per pixel are supported.");

        bool topDown = hSigned < 0;
        int h = Math.Abs(hSigned);
        if (w < 2 || w > 4096 || h < 2 || h > 4096)
            throw new FormatException($"BMP dimensions out of range: {w}x{h}.");

        int stride = ((bpp * w + 31) / 32) * 4;
        long needed = (long)pixelOffset + (long)stride * h;
        if (pixelOffset < 0 || needed > bmp.Length)
            throw new FormatException("BMP pixel data is truncated.");

        var luminance = new byte[w * h];

        if (bpp == 8)
        {
            int paletteOffset = 14 + dibSize;
            int clrUsed = BitConverter.ToInt32(bmp, 46);
            int paletteAvailable = (pixelOffset - paletteOffset) / 4;
            if (paletteAvailable <= 0)
                throw new FormatException("BMP palette is truncated.");
            int paletteCount = clrUsed is > 0 and <= 256
                ? Math.Min(clrUsed, paletteAvailable)
                : Math.Min(256, paletteAvailable);

            Span<byte> palR = stackalloc byte[256];
            Span<byte> palG = stackalloc byte[256];
            Span<byte> palB = stackalloc byte[256];
            for (int i = 0; i < paletteCount; i++)
            {
                palB[i] = bmp[paletteOffset + i * 4 + 0];
                palG[i] = bmp[paletteOffset + i * 4 + 1];
                palR[i] = bmp[paletteOffset + i * 4 + 2];
            }

            for (int row = 0; row < h; row++)
            {
                int storedRow = topDown ? row : h - 1 - row;
                int srcBase = pixelOffset + storedRow * stride;
                int dstBase = row * w;
                for (int x = 0; x < w; x++)
                {
                    byte idx = bmp[srcBase + x];
                    if (idx >= paletteCount)
                        throw new FormatException("BMP pixel index exceeds palette size.");
                    luminance[dstBase + x] = ToLuminance(palR[idx], palG[idx], palB[idx]);
                }
            }
        }
        else
        {
            int bytesPerPixel = bpp / 8;
            for (int row = 0; row < h; row++)
            {
                int storedRow = topDown ? row : h - 1 - row;
                int srcBase = pixelOffset + storedRow * stride;
                int dstBase = row * w;
                for (int x = 0; x < w; x++)
                {
                    int o = srcBase + x * bytesPerPixel;
                    byte b = bmp[o + 0];
                    byte g = bmp[o + 1];
                    byte r = bmp[o + 2];
                    luminance[dstBase + x] = ToLuminance(r, g, b);
                }
            }
        }

        return new HeightmapImage { Width = w, Height = h, Luminance = luminance };
    }

    /// <summary>
    /// Maps a luminance value (0-255) to a terrain height by linear
    /// interpolation between the black-point and white-point heights.
    /// </summary>
    public static float MapToHeight(byte v, float blackHeight = DefaultBlackHeight, float whiteHeight = DefaultWhiteHeight) =>
        blackHeight + (v / 255f) * (whiteHeight - blackHeight);

    /// <summary>
    /// Builds a TerrainData from a luminance image. The terrain always
    /// resizes to exactly match the image (1 pixel = 1 vertex).
    /// An existing header may be supplied to preserve stretch/offsets style;
    /// lightmap/triangles/AO are reset to new-map defaults (full quads).
    /// </summary>
    public static TerrainData ToTerrainData(HeightmapImage image, float blackHeight = DefaultBlackHeight, float whiteHeight = DefaultWhiteHeight, GtiHeader? templateHeader = null, string textureName = "useless")
    {
        int w = image.Width;
        int h = image.Height;

        var header = templateHeader ?? GtiFormat.CreateNew(w, h).Header;
        header.Width = w;
        header.Height = h;
        header.MinHeight = Math.Min(blackHeight, whiteHeight);
        header.MaxHeight = Math.Max(blackHeight, whiteHeight);
        if (header.Version == 7)
            header.Version = 3;

        float stretch = header.Stretch is > 0 ? header.Stretch : 40f;
        header.Stretch = stretch;
        header.XOffset = w * stretch * -0.5f;
        header.YOffset = h * stretch * -0.5f;

        string tex = !string.IsNullOrEmpty(textureName) ? textureName : "useless";

        var terrain = new TerrainData
        {
            Header = header,
            TextureName = tex,
            Heights = new float[w * h],
            Triangles = new byte[w * h],
            LightMap = new byte[w * h * 3],
            AmbientOcclusion = null,
            AODirections = 0,
            AORadius = 0,
        };

        // New-map defaults: full-quad triangles (type 5, TL-BR diagonal),
        // matching ApplyCircularFill and the subdivider convention. Types 1-4
        // render only a single triangle per cell, so must not be used here.
        Array.Fill(terrain.Triangles, (byte)5);
        Array.Fill(terrain.LightMap, (byte)255);

        // Image row 0 is the top; terrain row 0 is the south (bottom),
        // mirroring ExportBitmapAsync which flips Y on export.
        for (int y = 0; y < h; y++)
        {
            int srcRow = h - 1 - y;
            for (int x = 0; x < w; x++)
                terrain.Heights[y * w + x] = MapToHeight(image.Luminance[srcRow * w + x], blackHeight, whiteHeight);
        }

        return terrain;
    }

    private static byte ToLuminance(byte r, byte g, byte b)
    {
        if (r == g && g == b)
            return r;
        return (byte)Math.Clamp(MathF.Round(0.299f * r + 0.587f * g + 0.114f * b), 0f, 255f);
    }
}
