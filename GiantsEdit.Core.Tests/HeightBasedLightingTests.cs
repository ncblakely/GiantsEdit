using GiantsEdit.Core.Formats;
using GiantsEdit.Core.Rendering;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class HeightBasedLightingTests
{
    [TestMethod]
    public void Apply_InterpolatesColorsByHeight()
    {
        var terrain = CreateTerrain(3, 3);
        terrain.Heights[1 * 3 + 1] = 50;
        var settings = new HeightBasedLightingSettings
        {
            LevelCount = 2,
            HeightLevels = [0, 100, 0, 0, 0, 0, 0],
            HeightColors =
            [
                new LightingColor(0, 0, 0),
                new LightingColor(200, 100, 50),
                LightingColor.White,
                LightingColor.White,
                LightingColor.White,
                LightingColor.White,
                LightingColor.White
            ]
        };

        HeightBasedLighting.Apply(terrain, settings);

        int index = (1 * 3 + 1) * 3;
        Assert.AreEqual(100, terrain.LightMap[index]);
        Assert.AreEqual(50, terrain.LightMap[index + 1]);
        Assert.AreEqual(25, terrain.LightMap[index + 2]);
    }

    [TestMethod]
    public void Apply_WithSunSky_UsesTerrainNormalAndClampsOutput()
    {
        var terrain = CreateTerrain(3, 3);
        var settings = new HeightBasedLightingSettings
        {
            LevelCount = 1,
            HeightColors =
            [
                new LightingColor(255, 255, 255),
                LightingColor.White,
                LightingColor.White,
                LightingColor.White,
                LightingColor.White,
                LightingColor.White,
                LightingColor.White
            ],
            EnableSunSky = true,
            SunDirection = new(0, 0, 1),
            SkyColor = new(0, 0, 0),
            TopColor = new(255, 255, 255)
        };

        HeightBasedLighting.Apply(terrain, settings);

        int index = 4 * 3;
        Assert.AreEqual(255, terrain.LightMap[index]);
        Assert.AreEqual(255, terrain.LightMap[index + 1]);
        Assert.AreEqual(255, terrain.LightMap[index + 2]);
    }

    [TestMethod]
    public void Apply_RegionOnlyChangesRequestedCells()
    {
        var terrain = CreateTerrain(3, 3);
        var settings = new HeightBasedLightingSettings
        {
            LevelCount = 1,
            HeightColors =
            [
                new LightingColor(42, 43, 44),
                LightingColor.White,
                LightingColor.White,
                LightingColor.White,
                LightingColor.White,
                LightingColor.White,
                LightingColor.White
            ]
        };

        HeightBasedLighting.Apply(terrain, settings, 1, 1, 2, 2);

        Assert.AreEqual(42, terrain.LightMap[4 * 3]);
        Assert.AreEqual(43, terrain.LightMap[4 * 3 + 1]);
        Assert.AreEqual(44, terrain.LightMap[4 * 3 + 2]);
        Assert.AreEqual(0, terrain.LightMap[0]);
    }

    private static TerrainData CreateTerrain(int width, int height)
    {
        int count = width * height;
        var terrain = new TerrainData
        {
            Header = new GtiHeader { Width = width, Height = height, Stretch = 1f },
            Heights = new float[count],
            Triangles = new byte[count],
            LightMap = new byte[count * 3]
        };
        Array.Fill(terrain.Triangles, (byte)5);
        return terrain;
    }
}
