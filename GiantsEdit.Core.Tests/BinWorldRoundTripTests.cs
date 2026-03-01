using GiantsEdit.Core.Formats;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class BinWorldRoundTripTests
{
    [TestMethod]
    public void LoadSave_MinimalWorld_RoundTrips()
    {
        // Build a minimal world tree manually and save it, then load it back
        var root = BuildMinimalWorld();

        var writer = new BinWorldWriter();
        byte[] data = writer.Save(root);

        var reader = new BinWorldReader();
        var loaded = reader.Load(data);

        Assert.IsNotNull(loaded);

        // Verify [FileStart]
        var fileStart = loaded!.GetChildNode(BinFormatConstants.SectionFileStart);
        Assert.AreEqual("box01", fileStart.GetChildLeaf("Box").StringValue);
        Assert.AreEqual("test.gti", fileStart.GetChildLeaf("GtiName").StringValue);

        // Verify Tiling
        var tiling = loaded.GetChildNode(BinFormatConstants.NodeTiling);
        Assert.AreEqual(1.0f, tiling.GetChildLeaf("Obsolete0").SingleValue);

        // Verify Fog
        var fog = loaded.GetChildNode(BinFormatConstants.NodeFog);
        Assert.AreEqual(100.0f, fog.GetChildLeaf("FogMin").SingleValue);
        Assert.AreEqual(500.0f, fog.GetChildLeaf("FogMax").SingleValue);
        Assert.AreEqual((byte)128, fog.GetChildLeaf("Red").ByteValue);

        // Verify [sfx]
        var sfx = loaded.GetChildNode(BinFormatConstants.SectionSfx);
        Assert.AreEqual(42, sfx.GetChildLeaf("NumOrVersion").Int32Value);

        // Verify [textures]
        var tex = loaded.GetChildNode(BinFormatConstants.SectionTextures);
        Assert.AreEqual(1, tex.NodeCount);

        // Verify [includefiles]
        var inc = loaded.GetChildNode(BinFormatConstants.SectionIncludeFiles);
        Assert.AreEqual(1, inc.LeafCount);
    }

    [TestMethod]
    public void LoadSave_WithObjects_RoundTrips()
    {
        var root = BuildMinimalWorld();

        // Add objects
        var objects = root.AddNode(BinFormatConstants.GroupObjects);
        var obj1 = objects.AddNode(BinFormatConstants.NodeObject);
        obj1.AddInt32("Type", 102);
        obj1.AddSingle("X", 10.0f);
        obj1.AddSingle("Y", 20.0f);
        obj1.AddSingle("Z", 30.0f);
        obj1.AddSingle("DirFacing", 1.57f);

        var obj2 = objects.AddNode(BinFormatConstants.NodeObject);
        obj2.AddInt32("Type", 200);
        obj2.AddSingle("X", -5.0f);
        obj2.AddSingle("Y", -10.0f);
        obj2.AddSingle("Z", 0.0f);
        obj2.AddSingle("DirFacing", 0.1f);
        obj2.AddSingle("TiltForward", 0.2f);
        obj2.AddSingle("TiltLeft", 0.3f);

        var writer = new BinWorldWriter();
        byte[] data = writer.Save(root);

        var reader = new BinWorldReader();
        var loaded = reader.Load(data);
        Assert.IsNotNull(loaded);

        var loadedObjs = loaded!.GetChildNode(BinFormatConstants.GroupObjects);
        var nodes = loadedObjs.EnumerateNodes().ToList();
        Assert.HasCount(2, nodes);

        // First object (1-angle)
        Assert.AreEqual(102, nodes[0].GetChildLeaf("Type").Int32Value);
        Assert.AreEqual(10.0f, nodes[0].GetChildLeaf("X").SingleValue);
        Assert.AreEqual(1.57f, nodes[0].GetChildLeaf("DirFacing").SingleValue);

        // Second object (with tilt)
        Assert.AreEqual(200, nodes[1].GetChildLeaf("Type").Int32Value);
        Assert.AreEqual(0.2f, nodes[1].GetChildLeaf("TiltForward").SingleValue);
    }

    private static DataModel.TreeNode BuildMinimalWorld()
    {
        var root = new DataModel.TreeNode("Map data");

        var fs = root.AddNode(BinFormatConstants.SectionFileStart);
        fs.AddString("Box", "box01");
        fs.AddString("GtiName", "test.gti");

        var tiling = root.AddNode(BinFormatConstants.NodeTiling);
        tiling.AddSingle("Obsolete0", 1.0f);
        tiling.AddSingle("Obsolete1", 2.0f);
        tiling.AddSingle("Obsolete2", 3.0f);
        tiling.AddSingle("MixNear", 4.0f);
        tiling.AddSingle("MixFar", 5.0f);
        tiling.AddSingle("MixNearBlend", 6.0f);
        tiling.AddSingle("MixFarBlend", 7.0f);

        var fog = root.AddNode(BinFormatConstants.NodeFog);
        fog.AddSingle("FogMin", 100.0f);
        fog.AddSingle("FogMax", 500.0f);
        fog.AddByte("Red", 128);
        fog.AddByte("Green", 200);
        fog.AddByte("Blue", 255);

        var wfog = root.AddNode(BinFormatConstants.NodeWaterFog);
        wfog.AddSingle("FogMin", 50.0f);
        wfog.AddSingle("FogMax", 300.0f);
        wfog.AddByte("Red", 0);
        wfog.AddByte("Green", 100);
        wfog.AddByte("Blue", 200);

        var tex = root.AddNode(BinFormatConstants.SectionTextures);
        var t1 = tex.AddNode("texture");
        t1.AddByte("Unknown", 0);
        t1.AddByte("IsSkyDome", 1);
        t1.AddString("Name", "sky.tga");

        var sfx = root.AddNode(BinFormatConstants.SectionSfx);
        sfx.AddInt32("NumOrVersion", 42);
        sfx.AddInt32("SfxVersion", 43);
        sfx.AddInt32("Count", 44);
        sfx.AddInt32("EntrySize", 45);
        sfx.AddInt32("DataSize", 46);

        var obj = root.AddNode(BinFormatConstants.SectionObjDefs);
        obj.AddByte("data", 0);

        var fx = root.AddNode(BinFormatConstants.SectionFx);
        fx.AddInt32("EnvironmentType", 1);

        var scen = root.AddNode(BinFormatConstants.SectionScenerios);

        var inc = root.AddNode(BinFormatConstants.SectionIncludeFiles);
        inc.AddString("Name", "default.inc");

        return root;
    }
}
