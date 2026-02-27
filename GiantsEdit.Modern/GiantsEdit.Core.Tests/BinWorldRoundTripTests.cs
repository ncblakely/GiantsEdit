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
        var fileStart = loaded!.GetChildNode("[FileStart]");
        Assert.AreEqual("box01", fileStart.GetChildLeaf("Box").StringValue);
        Assert.AreEqual("test.gti", fileStart.GetChildLeaf("GtiName").StringValue);

        // Verify Tiling
        var tiling = loaded.GetChildNode("Tiling");
        Assert.AreEqual(1.0f, tiling.GetChildLeaf("p0").SingleValue);

        // Verify Fog
        var fog = loaded.GetChildNode("Fog");
        Assert.AreEqual(100.0f, fog.GetChildLeaf("Near distance").SingleValue);
        Assert.AreEqual(500.0f, fog.GetChildLeaf("Far distance").SingleValue);
        Assert.AreEqual((byte)128, fog.GetChildLeaf("Red").ByteValue);

        // Verify [sfxlist]
        var sfx = loaded.GetChildNode("[sfxlist]");
        Assert.AreEqual(42, sfx.GetChildLeaf("p0").Int32Value);

        // Verify [textures]
        var tex = loaded.GetChildNode("[textures]");
        Assert.AreEqual(1, tex.NodeCount);

        // Verify [includefiles]
        var inc = loaded.GetChildNode("[includefiles]");
        Assert.AreEqual(1, inc.LeafCount);
    }

    [TestMethod]
    public void LoadSave_WithObjects_RoundTrips()
    {
        var root = BuildMinimalWorld();

        // Add objects
        var objects = root.AddNode("<Objects>");
        var obj1 = objects.AddNode("Object");
        obj1.AddInt32("Type", 102);
        obj1.AddSingle("X", 10.0f);
        obj1.AddSingle("Y", 20.0f);
        obj1.AddSingle("Z", 30.0f);
        obj1.AddSingle("Angle", 1.57f);

        var obj2 = objects.AddNode("Object");
        obj2.AddInt32("Type", 200);
        obj2.AddSingle("X", -5.0f);
        obj2.AddSingle("Y", -10.0f);
        obj2.AddSingle("Z", 0.0f);
        obj2.AddSingle("Angle", 0.1f);
        obj2.AddSingle("Tilt Forward", 0.2f);
        obj2.AddSingle("Tilt Left", 0.3f);

        var writer = new BinWorldWriter();
        byte[] data = writer.Save(root);

        var reader = new BinWorldReader();
        var loaded = reader.Load(data);
        Assert.IsNotNull(loaded);

        var loadedObjs = loaded!.GetChildNode("<Objects>");
        var nodes = loadedObjs.EnumerateNodes().ToList();
        Assert.AreEqual(2, nodes.Count);

        // First object (1-angle)
        Assert.AreEqual(102, nodes[0].GetChildLeaf("Type").Int32Value);
        Assert.AreEqual(10.0f, nodes[0].GetChildLeaf("X").SingleValue);
        Assert.AreEqual(1.57f, nodes[0].GetChildLeaf("Angle").SingleValue);

        // Second object (with tilt)
        Assert.AreEqual(200, nodes[1].GetChildLeaf("Type").Int32Value);
        Assert.AreEqual(0.2f, nodes[1].GetChildLeaf("Tilt Forward").SingleValue);
    }

    private static DataModel.TreeNode BuildMinimalWorld()
    {
        var root = new DataModel.TreeNode("Map data");

        var fs = root.AddNode("[FileStart]");
        fs.AddString("Box", "box01");
        fs.AddString("GtiName", "test.gti");

        var tiling = root.AddNode("Tiling");
        for (int i = 0; i < 7; i++)
            tiling.AddSingle($"p{i}", (float)(i + 1));

        var fog = root.AddNode("Fog");
        fog.AddSingle("Near distance", 100.0f);
        fog.AddSingle("Far distance", 500.0f);
        fog.AddByte("Red", 128);
        fog.AddByte("Green", 200);
        fog.AddByte("Blue", 255);

        var wfog = root.AddNode("WaterFog");
        wfog.AddSingle("Near distance", 50.0f);
        wfog.AddSingle("Far distance", 300.0f);
        wfog.AddByte("Red", 0);
        wfog.AddByte("Green", 100);
        wfog.AddByte("Blue", 200);

        var tex = root.AddNode("[textures]");
        var t1 = tex.AddNode("texture");
        t1.AddByte("Unknown", 0);
        t1.AddByte("IsSkyDome", 1);
        t1.AddString("Name", "sky.tga");

        var sfx = root.AddNode("[sfxlist]");
        for (int i = 0; i < 5; i++)
            sfx.AddInt32($"p{i}", 42 + i);

        var unkn = root.AddNode("[unknown]");
        unkn.AddByte("unknown", 0);

        var fx = root.AddNode("[fx]");
        fx.AddInt32("p0", 1);

        var scen = root.AddNode("[scenerios]");

        var inc = root.AddNode("[includefiles]");
        inc.AddString("Name", "default.inc");

        return root;
    }
}
