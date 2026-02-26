using GiantsEdit.Core.Formats;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class GtiFormatTests
{
    [TestMethod]
    public void CreateNew_HasCorrectDimensions()
    {
        var terrain = GtiFormat.CreateNew(64, 32, "test.tga");

        Assert.AreEqual(64, terrain.Width);
        Assert.AreEqual(32, terrain.Height);
        Assert.AreEqual("test.tga", terrain.TextureName);
        Assert.AreEqual(64 * 32, terrain.Heights.Length);
        Assert.AreEqual(64 * 32, terrain.Triangles.Length);
        Assert.AreEqual(64 * 32 * 3, terrain.LightMap.Length);
    }

    [TestMethod]
    public void SaveLoad_RoundTrips()
    {
        var original = GtiFormat.CreateNew(16, 16, "ground.tga");

        // Set some test data
        original.Header.XOffset = 100.5f;
        original.Header.YOffset = -200.25f;
        original.Header.Stretch = 2.0f;
        original.SetHeight(0, 0, 10.5f);
        original.SetHeight(5, 3, -3.14f);
        original.Triangles[0] = 7;
        original.LightMap[0] = 128;
        original.LightMap[1] = 64;
        original.LightMap[2] = 32;

        byte[] saved = GtiFormat.Save(original);
        var loaded = GtiFormat.Load(saved);

        Assert.AreEqual(16, loaded.Width);
        Assert.AreEqual(16, loaded.Height);
        Assert.AreEqual("ground.tga", loaded.TextureName);
        Assert.AreEqual(100.5f, loaded.Header.XOffset);
        Assert.AreEqual(-200.25f, loaded.Header.YOffset);
        Assert.AreEqual(2.0f, loaded.Header.Stretch);
        Assert.AreEqual(10.5f, loaded.GetHeight(0, 0));
        Assert.AreEqual(-3.14f, loaded.GetHeight(5, 3));
        Assert.AreEqual((byte)7, loaded.Triangles[0]);
        Assert.AreEqual((byte)128, loaded.LightMap[0]);
        Assert.AreEqual((byte)64, loaded.LightMap[1]);
        Assert.AreEqual((byte)32, loaded.LightMap[2]);
    }

    [TestMethod]
    public void Save_UpdatesMinMaxHeight()
    {
        var terrain = GtiFormat.CreateNew(4, 4);
        terrain.SetHeight(0, 0, -50.0f);
        terrain.SetHeight(1, 1, 100.0f);

        byte[] saved = GtiFormat.Save(terrain);
        var loaded = GtiFormat.Load(saved);

        Assert.AreEqual(-50.0f, loaded.Header.MinHeight);
        Assert.AreEqual(100.0f, loaded.Header.MaxHeight);
    }

    [TestMethod]
    public void CreateNew_DefaultLightmapIsWhite()
    {
        var terrain = GtiFormat.CreateNew(2, 2);
        for (int i = 0; i < terrain.LightMap.Length; i++)
            Assert.AreEqual((byte)255, terrain.LightMap[i]);
    }
}
