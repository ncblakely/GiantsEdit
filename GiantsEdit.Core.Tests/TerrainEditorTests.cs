using System.Numerics;
using GiantsEdit.Core.Formats;
using GiantsEdit.Core.Rendering;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class TerrainEditorTests
{
    private static TerrainData CreateFlatTerrain(int size = 4, float height = 0f)
    {
        int cells = size * size;
        var terrain = new TerrainData
        {
            Header = new GtiHeader { Width = size, Height = size, Stretch = 1.0f, XOffset = 0, YOffset = 0 },
            Heights = new float[cells],
            Triangles = new byte[cells],
            LightMap = new byte[cells * 3],
        };
        Array.Fill(terrain.Heights, height);
        // Set default triangle type 5 (both triangles present)
        Array.Fill(terrain.Triangles, (byte)5);
        return terrain;
    }

    [TestMethod]
    public void ApplyHeightBrush_SinglePixel_ModifiesTargetCell()
    {
        var terrain = CreateFlatTerrain();

        TerrainEditor.ApplyHeightBrush(terrain, 2f, 2f, 10f, 0f, 1.0f);

        Assert.AreEqual(10f, terrain.Heights[2 * 4 + 2], 0.01f, "Target cell should be set to target height");
        Assert.AreEqual(0f, terrain.Heights[1 * 4 + 2], 0.01f, "Neighbor above should remain unchanged");
        Assert.AreEqual(0f, terrain.Heights[2 * 4 + 1], 0.01f, "Neighbor left should remain unchanged");
    }

    [TestMethod]
    public void ApplyHeightBrush_BilinearBrush_AffectsNeighbors()
    {
        var terrain = CreateFlatTerrain();

        TerrainEditor.ApplyHeightBrush(terrain, 1.5f, 1.5f, 10f, 1f, 1.0f);

        Assert.AreNotEqual(0f, terrain.Heights[1 * 4 + 1], "Cell (1,1) should be affected");
        Assert.AreNotEqual(0f, terrain.Heights[1 * 4 + 2], "Cell (2,1) should be affected");
        Assert.AreNotEqual(0f, terrain.Heights[2 * 4 + 1], "Cell (1,2) should be affected");
        Assert.AreNotEqual(0f, terrain.Heights[2 * 4 + 2], "Cell (2,2) should be affected");
    }

    [TestMethod]
    public void ApplyHeightBrush_GaussianBrush_FallsOffWithDistance()
    {
        var terrain = CreateFlatTerrain();

        TerrainEditor.ApplyHeightBrush(terrain, 1.5f, 1.5f, 10f, 2f, 1.0f);

        float centerHeight = terrain.Heights[1 * 4 + 1];
        float edgeHeight = terrain.Heights[0 * 4 + 0];

        Assert.IsTrue(centerHeight > 0f, "Center cell should be raised");
        Assert.IsTrue(centerHeight > edgeHeight,
            $"Center ({centerHeight}) should be affected more than edge ({edgeHeight})");
    }

    [TestMethod]
    public void PickHeight_ReturnsCorrectValue_AtCenter()
    {
        var terrain = CreateFlatTerrain();
        terrain.Heights[1 * 4 + 1] = 42f;

        float picked = TerrainEditor.PickHeight(terrain, 1f, 1f, 0f);

        Assert.AreEqual(42f, picked, 0.01f, "Should return the exact height at the cell");
    }

    [TestMethod]
    public void PickHeight_InterpolatesBetweenCells()
    {
        var terrain = CreateFlatTerrain();
        terrain.Heights[0 * 4 + 0] = 0f;
        terrain.Heights[0 * 4 + 1] = 10f;

        float picked = TerrainEditor.PickHeight(terrain, 0.5f, 0f, 1f);

        Assert.AreEqual(5f, picked, 0.01f, "Should bilinearly interpolate between 0 and 10");
    }

    [TestMethod]
    public void ApplyLightBrush_SinglePixel_SetsColor()
    {
        var terrain = CreateFlatTerrain();

        TerrainEditor.ApplyLightBrush(terrain, 1f, 1f, 255, 128, 64, 0f, 1.0f);

        int ci = 1 * 4 + 1;
        Assert.AreEqual(255, terrain.LightMap[ci * 3 + 0], "Red channel should be set");
        Assert.AreEqual(128, terrain.LightMap[ci * 3 + 1], "Green channel should be set");
        Assert.AreEqual(64, terrain.LightMap[ci * 3 + 2], "Blue channel should be set");
    }

    [TestMethod]
    public void PickLight_ReturnsCorrectColor()
    {
        var terrain = CreateFlatTerrain();
        int ci = 2 * 4 + 2;
        terrain.LightMap[ci * 3 + 0] = 200;
        terrain.LightMap[ci * 3 + 1] = 100;
        terrain.LightMap[ci * 3 + 2] = 50;

        var (r, g, b) = TerrainEditor.PickLight(terrain, 2f, 2f, 0f);

        Assert.AreEqual(200, r, "Red should match");
        Assert.AreEqual(100, g, "Green should match");
        Assert.AreEqual(50, b, "Blue should match");
    }

    [TestMethod]
    public void PaintTriangleSet_LeftClick_SetsTriangleType()
    {
        var terrain = CreateFlatTerrain();
        terrain.Triangles[0] = 0;

        TerrainEditor.PaintTriangleSet(terrain, 0.25f, 0.25f, 0f, false);

        int result = terrain.Triangles[0] & 7;
        Assert.AreNotEqual(0, result, "Triangle type should change from 0 after left-click");
    }

    [TestMethod]
    public void PaintTriangleSet_RightClick_ClearsTriangleType()
    {
        var terrain = CreateFlatTerrain();

        TerrainEditor.PaintTriangleSet(terrain, 0.25f, 0.25f, 0f, true);

        int result = terrain.Triangles[0] & 7;
        Assert.AreNotEqual(5, result, "Triangle type should change from 5 after right-click");
    }

    [TestMethod]
    public void ScreenToTerrain_RayHitsFlat_ReturnsHit()
    {
        var terrain = CreateFlatTerrain();
        var cam = new EditorCamera { ConstrainToDome = false };
        cam.LookAt(new Vector3(1.5f, 1.5f, 0), 50f);

        var hit = TerrainEditor.ScreenToTerrain(400, 300, 800, 600, cam, terrain);

        Assert.IsTrue(hit.Hit, "Ray from camera above terrain should hit");
        Assert.IsTrue(hit.GridX >= 0 && hit.GridX <= 3, $"GridX should be within terrain bounds, got {hit.GridX}");
        Assert.IsTrue(hit.GridY >= 0 && hit.GridY <= 3, $"GridY should be within terrain bounds, got {hit.GridY}");
    }

    [TestMethod]
    public void ScreenToTerrain_RayMisses_ReturnsFalse()
    {
        var terrain = CreateFlatTerrain();
        var cam = new EditorCamera { ConstrainToDome = false };
        cam.Position = new Vector3(1.5f, -1000f, 100f);

        var hit = TerrainEditor.ScreenToTerrain(400, 300, 800, 600, cam, terrain);

        Assert.IsFalse(hit.Hit, "Ray should miss the terrain entirely");
    }
}
