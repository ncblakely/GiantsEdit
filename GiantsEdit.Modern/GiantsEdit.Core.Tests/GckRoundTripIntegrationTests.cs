using System.Collections.Concurrent;
using GiantsEdit.Core.Formats;
using GiantsEdit.Core.Services;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class GckRoundTripIntegrationTests
{
    private const string TestDataDir = "TestData";

    private static IEnumerable<string> GetAllGckFiles()
        => Directory.GetFiles(TestDataDir, "*.gck").OrderBy(f => f);

    private static (byte[] bin, byte[] gti) ExtractOriginals(string gckPath)
    {
        var entries = GzpArchive.ListEntries(gckPath);

        string binEntry = entries.First(e =>
            Path.GetFileName(e).StartsWith("w_", StringComparison.OrdinalIgnoreCase) &&
            e.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));
        string gtiEntry = entries.First(e =>
            e.EndsWith(".gti", StringComparison.OrdinalIgnoreCase));

        return (GzpArchive.ExtractFile(gckPath, binEntry)!, GzpArchive.ExtractFile(gckPath, gtiEntry)!);
    }

    [TestMethod]
    public void LoadSave_AllMaps_BinRoundTrip()
    {
        var gckFiles = GetAllGckFiles().ToList();
        if (gckFiles.Count == 0)
            Assert.Inconclusive("No .gck test files found.");

        var failures = new ConcurrentBag<string>();

        Parallel.ForEach(gckFiles, gckPath =>
        {
            string mapName = Path.GetFileNameWithoutExtension(gckPath);
            try
            {
                // Load → Save → Reload → Compare trees
                var doc = new WorldDocument();
                doc.LoadGck(gckPath);

                var writer = new BinWorldWriter();
                byte[] savedBin = writer.Save(doc.WorldRoot!);

                var reader = new BinWorldReader();
                var reloadedTree = reader.Load(savedBin);
                Assert.IsNotNull(reloadedTree, $"{mapName}: Saved BIN failed to reload");

                var errors = CompareTreeNodes(doc.WorldRoot!, reloadedTree, "");
                if (errors.Count > 0)
                    failures.Add($"{mapName}: {string.Join("; ", errors.Take(10))}");
            }
            catch (Exception ex)
            {
                failures.Add($"{mapName}: EXCEPTION — {ex.GetType().Name}: {ex.Message}");
            }
        });

        if (!failures.IsEmpty)
            Assert.Fail($"BIN round-trip failed for {failures.Count}/{gckFiles.Count} maps:\n\n"
                + string.Join("\n\n", failures.OrderBy(f => f)));
    }

    [TestMethod]
    public void LoadSave_AllMaps_GtiRoundTrip()
    {
        var gckFiles = GetAllGckFiles().ToList();
        if (gckFiles.Count == 0)
            Assert.Inconclusive("No .gck test files found.");

        var failures = new ConcurrentBag<string>();

        Parallel.ForEach(gckFiles, gckPath =>
        {
            string mapName = Path.GetFileNameWithoutExtension(gckPath);
            try
            {
                var (_, originalGti) = ExtractOriginals(gckPath);

                var doc = new WorldDocument();
                doc.LoadGck(gckPath);

                byte[] savedGti = GtiFormat.Save(doc.Terrain!);

                var errors = CompareGti(originalGti, savedGti);
                if (errors.Count > 0)
                    failures.Add($"{mapName}: {string.Join("; ", errors)}");
            }
            catch (Exception ex)
            {
                failures.Add($"{mapName}: EXCEPTION — {ex.GetType().Name}: {ex.Message}");
            }
        });

        if (!failures.IsEmpty)
            Assert.Fail($"GTI round-trip failed for {failures.Count}/{gckFiles.Count} maps:\n\n"
                + string.Join("\n\n", failures.OrderBy(f => f)));
    }

    private static List<string> CompareTreeNodes(DataModel.TreeNode orig, DataModel.TreeNode saved, string path)
    {
        var errors = new List<string>();
        string nodePath = string.IsNullOrEmpty(path) ? orig.Name : $"{path}/{orig.Name}";

        if (orig.Name != saved.Name)
        {
            errors.Add($"Node name mismatch at '{path}': '{orig.Name}' vs '{saved.Name}'");
            return errors;
        }

        // Compare leaves
        var origLeaves = orig.EnumerateLeaves().ToList();
        var savedLeaves = saved.EnumerateLeaves().ToList();
        if (origLeaves.Count != savedLeaves.Count)
        {
            errors.Add($"Leaf count at '{nodePath}': {origLeaves.Count} vs {savedLeaves.Count}");
        }
        else
        {
            for (int i = 0; i < origLeaves.Count; i++)
            {
                var ol = origLeaves[i];
                var sl = savedLeaves[i];
                if (ol.Name != sl.Name)
                    errors.Add($"Leaf name at '{nodePath}[{i}]': '{ol.Name}' vs '{sl.Name}'");
                else if (ol.PropertyType != sl.PropertyType)
                    errors.Add($"Leaf type at '{nodePath}/{ol.Name}': {ol.PropertyType} vs {sl.PropertyType}");
                else if (ol.RawInt32 != sl.RawInt32 && ol.PropertyType != DataModel.PropertyType.String)
                    errors.Add($"Leaf value at '{nodePath}/{ol.Name}': {ol.RawInt32} vs {sl.RawInt32}");
                else if (ol.PropertyType == DataModel.PropertyType.String && ol.StringValue != sl.StringValue)
                    errors.Add($"Leaf string at '{nodePath}/{ol.Name}': '{ol.StringValue}' vs '{sl.StringValue}'");
            }
        }

        // Compare child nodes
        var origNodes = orig.EnumerateNodes().ToList();
        var savedNodes = saved.EnumerateNodes().ToList();
        if (origNodes.Count != savedNodes.Count)
        {
            errors.Add($"Child node count at '{nodePath}': {origNodes.Count} vs {savedNodes.Count}");
        }
        else
        {
            for (int i = 0; i < origNodes.Count; i++)
                errors.AddRange(CompareTreeNodes(origNodes[i], savedNodes[i], nodePath));
        }

        return errors;
    }

    private static List<string> CompareGti(byte[] originalGtiBytes, byte[] savedGtiBytes)
    {
        var errors = new List<string>();

        var orig = GtiFormat.Load(originalGtiBytes);
        var saved = GtiFormat.Load(savedGtiBytes);

        if (orig.Header.Width != saved.Header.Width) errors.Add($"Width: {orig.Header.Width}→{saved.Header.Width}");
        if (orig.Header.Height != saved.Header.Height) errors.Add($"Height: {orig.Header.Height}→{saved.Header.Height}");
        if (orig.Header.Stretch != saved.Header.Stretch) errors.Add($"Stretch mismatch");
        if (orig.TextureName != saved.TextureName) errors.Add($"TextureName: '{orig.TextureName}'→'{saved.TextureName}'");

        if (errors.Count > 0) return errors;

        int w = orig.Width, h = orig.Height;
        bool IsActive(byte triType) => (triType & 7) is >= 1 and <= 7;

        int heightDiffs = 0, triDiffs = 0, lightDiffs = 0, activeCells = 0;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int ci = y * w + x;
                bool active = IsActive(orig.Triangles[ci]);
                if (x > 0) active |= IsActive(orig.Triangles[ci - 1]);
                if (y > 0) active |= IsActive(orig.Triangles[(y - 1) * w + x]);
                if (x > 0 && y > 0) active |= IsActive(orig.Triangles[(y - 1) * w + x - 1]);

                if (!active) continue;
                activeCells++;

                if (orig.Heights[ci] != saved.Heights[ci]) heightDiffs++;
                if (orig.Triangles[ci] != saved.Triangles[ci]) triDiffs++;
                if (orig.LightMap[ci * 3] != saved.LightMap[ci * 3] ||
                    orig.LightMap[ci * 3 + 1] != saved.LightMap[ci * 3 + 1] ||
                    orig.LightMap[ci * 3 + 2] != saved.LightMap[ci * 3 + 2]) lightDiffs++;
            }
        }

        if (heightDiffs > 0) errors.Add($"Heights: {heightDiffs}/{activeCells} active cells differ");
        if (triDiffs > 0) errors.Add($"Triangles: {triDiffs}/{activeCells} active cells differ");
        if (lightDiffs > 0) errors.Add($"Lightmap: {lightDiffs}/{activeCells} active cells differ");

        return errors;
    }

}
