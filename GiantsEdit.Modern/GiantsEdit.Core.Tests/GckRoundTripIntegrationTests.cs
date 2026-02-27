using System.Buffers.Binary;
using GiantsEdit.Core.Formats;
using GiantsEdit.Core.Services;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class GckRoundTripIntegrationTests
{
    private const string TestGckFile = "TestData/W_M_Mecc_L1.gck";

    private static (byte[] originalBin, byte[] originalGti, string binEntry, string gtiEntry) ExtractOriginals()
    {
        var entries = GzpArchive.ListEntries(TestGckFile);

        string binEntry = entries.First(e =>
            Path.GetFileName(e).StartsWith("w_", StringComparison.OrdinalIgnoreCase) &&
            e.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));
        string gtiEntry = entries.First(e =>
            e.EndsWith(".gti", StringComparison.OrdinalIgnoreCase));

        byte[] originalBin = GzpArchive.ExtractFile(TestGckFile, binEntry)!;
        byte[] originalGti = GzpArchive.ExtractFile(TestGckFile, gtiEntry)!;

        return (originalBin, originalGti, binEntry, gtiEntry);
    }

    [TestMethod]
    public void LoadSave_RealMap_BinSectionsAreIdentical()
    {
        if (!File.Exists(TestGckFile))
            Assert.Inconclusive($"Test data not found: {TestGckFile}");

        var (originalBin, _, _, _) = ExtractOriginals();

        var doc = new WorldDocument();
        doc.LoadGck(TestGckFile);

        var writer = new BinWorldWriter();
        byte[] savedBin = writer.Save(doc.WorldRoot!);

        // Dump for debugging
        string tempDir = Path.Combine(Path.GetTempPath(), "giantstest");
        Directory.CreateDirectory(tempDir);
        File.WriteAllBytes(Path.Combine(tempDir, "original.bin"), originalBin);
        File.WriteAllBytes(Path.Combine(tempDir, "saved.bin"), savedBin);

        Assert.AreEqual(originalBin.Length, savedBin.Length, "BIN file sizes must match");

        // Compare header pointers
        int[] origPtrs = ReadPointers(originalBin);
        int[] savedPtrs = ReadPointers(savedBin);
        CollectionAssert.AreEqual(origPtrs, savedPtrs, "Section pointers must match");

        // Compare each non-main-data section byte-for-byte (sections 1-6)
        string[] sectionNames = ["main_data", "textures", "sfx", "objdefs", "fx", "scenerios", "includefiles"];
        for (int i = 1; i <= 6; i++)
        {
            var origSection = ExtractSection(originalBin, origPtrs, i);
            var savedSection = ExtractSection(savedBin, savedPtrs, i);
            Assert.IsTrue(origSection.SequenceEqual(savedSection),
                $"Section {i} ({sectionNames[i]}) differs: orig={origSection.Length} bytes, saved={savedSection.Length} bytes");
        }

        // Verify main data block has same size
        int origBlockLen = BinaryPrimitives.ReadInt32LittleEndian(originalBin.AsSpan(origPtrs[0]));
        int savedBlockLen = BinaryPrimitives.ReadInt32LittleEndian(savedBin.AsSpan(savedPtrs[0]));
        Assert.AreEqual(origBlockLen, savedBlockLen, "Main data block length must match");
    }

    [TestMethod]
    public void LoadSave_RealMap_GtiHeaderAndDataMatch()
    {
        if (!File.Exists(TestGckFile))
            Assert.Inconclusive($"Test data not found: {TestGckFile}");

        var (_, originalGti, _, _) = ExtractOriginals();

        var doc = new WorldDocument();
        doc.LoadGck(TestGckFile);

        byte[] savedGti = GtiFormat.Save(doc.Terrain!);

        // Load both files to compare decoded terrain data
        var origTerrain = GtiFormat.Load(originalGti);
        var savedTerrain = GtiFormat.Load(savedGti);

        // Headers should match (except possibly RLE-derived size)
        Assert.AreEqual(origTerrain.Header.SignatureField, savedTerrain.Header.SignatureField, "Signature");
        Assert.AreEqual(origTerrain.Header.Width, savedTerrain.Header.Width, "Width");
        Assert.AreEqual(origTerrain.Header.Height, savedTerrain.Header.Height, "Height");
        Assert.AreEqual(origTerrain.Header.Stretch, savedTerrain.Header.Stretch, "Stretch");
        Assert.AreEqual(origTerrain.Header.XOffset, savedTerrain.Header.XOffset, "XOffset");
        Assert.AreEqual(origTerrain.Header.YOffset, savedTerrain.Header.YOffset, "YOffset");
        Assert.AreEqual(origTerrain.TextureName, savedTerrain.TextureName, "TextureName");

        // Compare decoded terrain arrays
        int cellCount = origTerrain.Width * origTerrain.Height;

        // Build active mask to only compare cells that matter
        int w = origTerrain.Width;
        int h = origTerrain.Height;
        bool IsActive(byte triType) => (triType & 7) is >= 1 and <= 7;

        int heightDiffs = 0;
        int triDiffs = 0;
        int lightDiffs = 0;
        int activeCells = 0;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int ci = y * w + x;
                bool active = false;

                // Check if this cell is active (used by any neighboring triangle)
                if (IsActive(origTerrain.Triangles[ci])) active = true;
                if (x > 0 && IsActive(origTerrain.Triangles[ci - 1])) active = true;
                if (y > 0 && IsActive(origTerrain.Triangles[(y - 1) * w + x])) active = true;
                if (x > 0 && y > 0 && IsActive(origTerrain.Triangles[(y - 1) * w + x - 1])) active = true;

                if (!active) continue;
                activeCells++;

                if (origTerrain.Heights[ci] != savedTerrain.Heights[ci]) heightDiffs++;
                if (origTerrain.Triangles[ci] != savedTerrain.Triangles[ci]) triDiffs++;
                if (origTerrain.LightMap[ci * 3] != savedTerrain.LightMap[ci * 3] ||
                    origTerrain.LightMap[ci * 3 + 1] != savedTerrain.LightMap[ci * 3 + 1] ||
                    origTerrain.LightMap[ci * 3 + 2] != savedTerrain.LightMap[ci * 3 + 2]) lightDiffs++;
            }
        }

        var errors = new List<string>();
        if (heightDiffs > 0) errors.Add($"Height differences: {heightDiffs}/{activeCells} active cells");
        if (triDiffs > 0) errors.Add($"Triangle differences: {triDiffs}/{activeCells} active cells");
        if (lightDiffs > 0) errors.Add($"Lightmap differences: {lightDiffs}/{activeCells} active cells");

        if (errors.Count > 0)
            Assert.Fail($"GTI terrain data differs after round-trip:\n{string.Join("\n", errors)}");
    }

    private static int[] ReadPointers(byte[] data)
    {
        int[] ptrs = new int[7];
        for (int i = 0; i < 7; i++)
            ptrs[i] = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4 + i * 4));
        return ptrs;
    }

    private static ReadOnlySpan<byte> ExtractSection(byte[] data, int[] ptrs, int sectionIndex)
    {
        int start = ptrs[sectionIndex];
        // Find the next section start after this one
        int end = data.Length;
        foreach (int p in ptrs)
        {
            if (p > start && p < end)
                end = p;
        }
        return data.AsSpan(start, end - start);
    }
}
