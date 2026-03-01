using GiantsEdit.Core.Services;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class WorldDocumentTests
{
    [TestMethod]
    public void NewWorld_CreatesValidTree()
    {
        var doc = new WorldDocument();
        doc.NewWorld(64, 64, "test.tga");

        Assert.IsNotNull(doc.WorldRoot);
        Assert.IsNotNull(doc.Terrain);
        Assert.AreEqual(64, doc.Terrain!.Width);
        Assert.IsTrue(doc.IsModified);

        var objNode = doc.WorldRoot!.FindChildNode("<Objects>");
        Assert.IsNotNull(objNode);
    }

    [TestMethod]
    public void AddObject_AddsToTree()
    {
        var doc = new WorldDocument();
        doc.NewWorld(16, 16);

        var obj = doc.AddObject(100, 1.0f, 2.0f, 3.0f, 0.5f);

        Assert.IsNotNull(obj);
        Assert.AreEqual(100, obj!.FindChildLeaf("Type")!.Int32Value);
        Assert.AreEqual(1.0f, obj.FindChildLeaf("X")!.SingleValue);
    }

    [TestMethod]
    public void GetObjectInstances_ReturnsObjects()
    {
        var doc = new WorldDocument();
        doc.NewWorld(16, 16);
        doc.AddObject(50, 10f, 20f, 30f);
        doc.AddObject(60, -5f, -10f, 0f);

        var instances = doc.GetObjectInstances();
        Assert.HasCount(2, instances);
        Assert.AreEqual(50, instances[0].ModelId);
        Assert.AreEqual(10f, instances[0].Position.X);
    }

    [TestMethod]
    public void RemoveSelectedObject_RemovesIt()
    {
        var doc = new WorldDocument();
        doc.NewWorld(16, 16);
        var obj = doc.AddObject(100, 0, 0, 0);
        doc.SelectObject(obj);

        doc.RemoveSelectedObject();

        Assert.IsNull(doc.SelectedObject);
        Assert.IsEmpty(doc.GetObjectInstances());
    }

    [TestMethod]
    public void PaintHeight_ModifiesTerrain()
    {
        var doc = new WorldDocument();
        doc.NewWorld(16, 16);
        doc.BrushRadius = 50f;
        doc.BrushStrength = 1.0f;

        float before = doc.Terrain!.GetHeight(8, 8);
        doc.PaintHeight(
            8 * doc.Terrain.Header.Stretch + doc.Terrain.Header.XOffset,
            8 * doc.Terrain.Header.Stretch + doc.Terrain.Header.YOffset,
            10f);
        float after = doc.Terrain.GetHeight(8, 8);

        Assert.IsGreaterThan(before, after, $"Height should increase: before={before}, after={after}");
    }

    [TestMethod]
    public void BuildTerrainRenderData_ReturnsValidData()
    {
        var doc = new WorldDocument();
        doc.NewWorld(4, 4);
        // Set all triangles to type 1 so we get indices
        for (int i = 0; i < doc.Terrain!.Triangles.Length; i++)
            doc.Terrain.Triangles[i] = 1;

        var renderData = doc.BuildTerrainRenderData();

        Assert.IsNotNull(renderData);
        Assert.AreEqual(16, renderData!.VertexCount);
        Assert.IsGreaterThan(0, renderData.IndexCount);
    }
}
