using GiantsEdit.Core.Formats;
using GiantsEdit.Core.Rendering;
using GiantsEdit.Core.Services;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class BmpHeightmapTests
{
    [TestMethod]
    public void MapToHeight_BlackIsMinus40_WhiteIs700()
    {
        Assert.AreEqual(-40f, BmpHeightmap.MapToHeight(0), 0.001f);
        Assert.AreEqual(700f, BmpHeightmap.MapToHeight(255), 0.001f);
        Assert.AreEqual(-40f + 128f / 255f * 740f, BmpHeightmap.MapToHeight(128), 0.001f);
    }

    [TestMethod]
    public void Load_24Bit_MapsPixelsWithYFlip()
    {
        // 2x2 image, top-down rows: [10, 20] / [30, 40]
        byte[] bmp = BuildBmp24(2, 2, topDown: false, (x, yTop) =>
            (byte)(yTop == 0 ? (x == 0 ? 10 : 20) : (x == 0 ? 30 : 40)));

        var image = BmpHeightmap.Load(bmp);

        Assert.AreEqual(2, image.Width);
        Assert.AreEqual(2, image.Height);
        // Returned in top-down order.
        CollectionAssert.AreEqual(new byte[] { 10, 20, 30, 40 }, image.Luminance);

        var terrain = BmpHeightmap.ToTerrainData(image);
        Assert.AreEqual(2, terrain.Width);
        Assert.AreEqual(2, terrain.Height);
        // Terrain row 0 (south) = image bottom row.
        Assert.AreEqual(BmpHeightmap.MapToHeight(30), terrain.Heights[0 * 2 + 0], 0.001f);
        Assert.AreEqual(BmpHeightmap.MapToHeight(40), terrain.Heights[0 * 2 + 1], 0.001f);
        Assert.AreEqual(BmpHeightmap.MapToHeight(10), terrain.Heights[1 * 2 + 0], 0.001f);
        Assert.AreEqual(BmpHeightmap.MapToHeight(20), terrain.Heights[1 * 2 + 1], 0.001f);
    }

    [TestMethod]
    public void Load_24Bit_TopDown_ReadsCorrectly()
    {
        byte[] bmp = BuildBmp24(2, 2, topDown: true, (x, yTop) =>
            (byte)(yTop == 0 ? (x == 0 ? 1 : 2) : (x == 0 ? 3 : 4)));

        var image = BmpHeightmap.Load(bmp);

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, image.Luminance);
    }

    [TestMethod]
    public void Load_8Bit_Palette_MapsGreyscale()
    {
        byte[] bmp = BuildBmp8([0, 128, 255], 3, 2);

        var image = BmpHeightmap.Load(bmp);

        Assert.AreEqual(3, image.Width);
        Assert.AreEqual(2, image.Height);
        CollectionAssert.AreEqual(new byte[] { 0, 128, 255, 0, 128, 255 }, image.Luminance);
    }

    [TestMethod]
    public void Load_32Bit_IgnoresAlpha()
    {
        byte[] bmp = BuildBmp32(2, 2);

        var image = BmpHeightmap.Load(bmp);

        Assert.AreEqual(2, image.Width);
        Assert.AreEqual(2, image.Height);
        CollectionAssert.AreEqual(new byte[] { 0, 255, 128, 64 }, image.Luminance);
    }

    [TestMethod]
    public void Load_Color_ConvertsViaLuminance()
    {
        // Pure red, green, blue, white 2x2 (patched in below, bottom-up storage).
        byte[] bmp = BuildBmp24(2, 2, topDown: false, (_, _) => 0);
        // Patch colors in directly (bottom-up storage).
        int off = 54, stride = 8;
        var bgr = new (byte B, byte G, byte R)[] { (0, 0, 255), (0, 255, 0), (255, 0, 0), (255, 255, 255) };
        // Stored row 0 = image bottom row (colors[2], colors[3]).
        SetPixel24(bmp, off, stride, 0, 0, bgr[2]);
        SetPixel24(bmp, off, stride, 1, 0, bgr[3]);
        SetPixel24(bmp, off, stride, 0, 1, bgr[0]);
        SetPixel24(bmp, off, stride, 1, 1, bgr[1]);

        var image = BmpHeightmap.Load(bmp);

        Assert.AreEqual((byte)MathF.Round(0.299f * 255), image.Luminance[0]);
        Assert.AreEqual((byte)MathF.Round(0.587f * 255), image.Luminance[1]);
        Assert.AreEqual((byte)MathF.Round(0.114f * 255), image.Luminance[2]);
        Assert.AreEqual(255, image.Luminance[3]);
    }

    [TestMethod]
    public void Load_InvalidSignature_Throws()
    {
        Assert.Throws<FormatException>(() => BmpHeightmap.Load(new byte[100]));
    }

    [TestMethod]
    public void Load_Compressed_Throws()
    {
        byte[] bmp = BuildBmp24(2, 2, topDown: false, (_, _) => 0);
        BitConverter.GetBytes(1).CopyTo(bmp, 30); // BI_RLE8
        Assert.Throws<FormatException>(() => BmpHeightmap.Load(bmp));
    }

    [TestMethod]
    public void ToTerrainData_ResetsLightmapTrianglesAndAO()
    {
        var image = new BmpHeightmap.HeightmapImage
        {
            Width = 4,
            Height = 3,
            Luminance = new byte[12],
        };

        var terrain = BmpHeightmap.ToTerrainData(image);

        Assert.AreEqual(4, terrain.Width);
        Assert.AreEqual(3, terrain.Height);
        Assert.IsTrue(terrain.Triangles.All(t => t == 5));
        Assert.IsTrue(terrain.LightMap.All(b => b == 255));
        Assert.IsNull(terrain.AmbientOcclusion);
        Assert.AreEqual(-40f, terrain.Header.MinHeight);
        Assert.AreEqual(700f, terrain.Header.MaxHeight);
    }

    [TestMethod]
    public void MapToHeight_CustomRange_Interpolates()
    {
        Assert.AreEqual(0f, BmpHeightmap.MapToHeight(0, 0f, 100f), 0.001f);
        Assert.AreEqual(100f, BmpHeightmap.MapToHeight(255, 0f, 100f), 0.001f);
        Assert.AreEqual(0f, BmpHeightmap.MapToHeight(255, 100f, 0f), 0.001f); // white maps to 0 when inverted
        Assert.AreEqual(100f, BmpHeightmap.MapToHeight(0, 100f, 0f), 0.001f); // black maps to 100 when inverted
    }

    [TestMethod]
    public void ToTerrainData_CustomRange_SetsHeightsAndHeader()
    {
        var image = new BmpHeightmap.HeightmapImage
        {
            Width = 2,
            Height = 2,
            Luminance = [0, 255, 128, 64],
        };

        var terrain = BmpHeightmap.ToTerrainData(image, 10f, 20f);

        Assert.AreEqual(10f, terrain.Header.MinHeight);
        Assert.AreEqual(20f, terrain.Header.MaxHeight);
        Assert.AreEqual(20f, terrain.Heights.Max(), 0.001f);
        Assert.AreEqual(10f, terrain.Heights.Min(), 0.001f);
    }

    [TestMethod]
    public void ToTerrainData_GeneratesFullQuads()
    {
        var image = new BmpHeightmap.HeightmapImage
        {
            Width = 3,
            Height = 3,
            Luminance = new byte[9],
        };

        var terrain = BmpHeightmap.ToTerrainData(image);
        var mesh = TerrainMeshBuilder.Build(terrain);

        // 2x2 cells, full quad each: 2 triangles x 3 indices = 24.
        Assert.AreEqual(24, mesh.IndexCount);
    }

    [TestMethod]
    public void CreateNew_Filled_GeneratesFullQuads()
    {
        var terrain = GtiFormat.CreateNew(3, 3);
        var mesh = TerrainMeshBuilder.Build(terrain);

        Assert.AreEqual(24, mesh.IndexCount);
    }

    [TestMethod]
    public void ImportHeightmap_BotwBmp_CreatesAdequateTerrain()
    {
        string path = Path.Combine("TestData", "BOTW.bmp");
        byte[] bmp = File.ReadAllBytes(path);
        var doc = new WorldDocument();
        doc.ImportHeightmap(bmp);

        var terrain = doc.Terrain;
        Assert.IsNotNull(terrain);
        Assert.AreEqual(256, terrain.Width);
        Assert.AreEqual(256, terrain.Height);
        Assert.HasCount(256 * 256, terrain.Heights);
        Assert.IsTrue(terrain.Heights.All(h => h is >= -40f and <= 700f));
        Assert.AreEqual(-40f, terrain.Heights.Min(), 0.01f);
        Assert.AreEqual(700f, terrain.Heights.Max(), 0.01f);
        // Spot-check known fixture corners (BMP bottom-left=42 -> terrain south-west,
        // BMP top-right=0 -> terrain north-east), verifying the Y-flip.
        Assert.AreEqual(BmpHeightmap.MapToHeight(42), terrain.Heights[0 * 256 + 0], 0.5f);
        Assert.AreEqual(BmpHeightmap.MapToHeight(0), terrain.Heights[255 * 256 + 255], 0.001f);
        Assert.AreEqual(BmpHeightmap.MapToHeight(209), terrain.Heights[255 * 256 + 0], 0.5f);
    }

    private static void SetPixel24(byte[] bmp, int off, int stride, int x, int storedRow, (byte B, byte G, byte R) c)
    {
        int o = off + storedRow * stride + x * 3;
        bmp[o + 0] = c.B;
        bmp[o + 1] = c.G;
        bmp[o + 2] = c.R;
    }

    private static byte[] BuildBmp24(int w, int h, bool topDown, Func<int, int, byte> greyAtTopDown)
    {
        int stride = ((24 * w + 31) / 32) * 4;
        var bmp = new byte[54 + stride * h];
        WriteHeaders(bmp, w, topDown ? -h : h, 24, 54);
        for (int rowTop = 0; rowTop < h; rowTop++)
        {
            int storedRow = topDown ? rowTop : h - 1 - rowTop;
            for (int x = 0; x < w; x++)
            {
                byte v = greyAtTopDown(x, rowTop);
                SetPixel24(bmp, 54, stride, x, storedRow, (v, v, v));
            }
        }
        return bmp;
    }

    private static byte[] BuildBmp8(byte[] paletteGreys, int w, int h)
    {
        int stride = ((8 * w + 31) / 32) * 4;
        int paletteCount = paletteGreys.Length;
        int off = 54 + paletteCount * 4;
        var bmp = new byte[off + stride * h];
        WriteHeaders(bmp, w, h, 8, off);
        for (int i = 0; i < paletteCount; i++)
        {
            bmp[54 + i * 4 + 0] = paletteGreys[i];
            bmp[54 + i * 4 + 1] = paletteGreys[i];
            bmp[54 + i * 4 + 2] = paletteGreys[i];
        }
        for (int rowTop = 0; rowTop < h; rowTop++)
        {
            int storedRow = h - 1 - rowTop;
            for (int x = 0; x < w; x++)
                bmp[off + storedRow * stride + x] = (byte)(x % paletteCount);
        }
        return bmp;
    }

    private static byte[] BuildBmp32(int w, int h)
    {
        // Top-down values: [0, 255] / [128, 64].
        byte[] vals = [0, 255, 128, 64];
        int stride = w * 4;
        var bmp = new byte[54 + stride * h];
        WriteHeaders(bmp, w, h, 32, 54);
        for (int rowTop = 0; rowTop < h; rowTop++)
        {
            int storedRow = h - 1 - rowTop;
            for (int x = 0; x < w; x++)
            {
                byte v = vals[rowTop * w + x];
                int o = 54 + storedRow * stride + x * 4;
                bmp[o + 0] = v;
                bmp[o + 1] = v;
                bmp[o + 2] = v;
                bmp[o + 3] = 255;
            }
        }
        return bmp;
    }

    private static void WriteHeaders(byte[] bmp, int w, int hSigned, ushort bpp, int pixelOffset)
    {
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        BitConverter.GetBytes(pixelOffset).CopyTo(bmp, 10);
        BitConverter.GetBytes(40).CopyTo(bmp, 14);
        BitConverter.GetBytes(w).CopyTo(bmp, 18);
        BitConverter.GetBytes(hSigned).CopyTo(bmp, 22);
        BitConverter.GetBytes((ushort)1).CopyTo(bmp, 26);
        BitConverter.GetBytes(bpp).CopyTo(bmp, 28);
        BitConverter.GetBytes(0).CopyTo(bmp, 30); // BI_RGB
    }
}
