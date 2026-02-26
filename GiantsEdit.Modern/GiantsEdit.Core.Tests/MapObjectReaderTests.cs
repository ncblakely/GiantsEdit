using GiantsEdit.Core.Formats;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class MapObjectReaderTests
{
    [TestMethod]
    public void Load_ParsesBlockWithTriangles()
    {
        var lines = new[]
        {
            "[roof_1]",
            "<Objects>",
            "100",
            "200",
            "<Colors>",
            "1.0,0.0,0.0",
            "0.0,1.0,0.0",
            "<Vertices>",
            "0.0,0.0,0.0,0",
            "1.0,0.0,0.0,0",
            "0.0,1.0,0.0,1",
            "<Triangles>",
            "0,1,2"
        };

        var reader = new MapObjectReader();
        reader.Load(lines);

        Assert.AreEqual(1, reader.Objects.Count);
        Assert.AreEqual("roof_1", reader.Objects[0].Name);
        Assert.AreEqual(1, reader.Objects[0].Triangles.Count);

        // Check vertex colors
        var tri = reader.Objects[0].Triangles[0];
        Assert.AreEqual((byte)255, tri.V0.R); // color 0: red
        Assert.AreEqual((byte)0, tri.V0.G);
        Assert.AreEqual((byte)0, tri.V2.R); // color 1: green
        Assert.AreEqual((byte)255, tri.V2.G);

        // Check object mapping
        Assert.AreEqual(0, reader.ObjectWrap[100]);
        Assert.AreEqual(0, reader.ObjectWrap[200]);
    }

    [TestMethod]
    public void Load_AllKeyword_MapsAllIds()
    {
        var lines = new[]
        {
            "[default]",
            "<Objects>",
            "All",
            "<Colors>",
            "0.5,0.5,0.5",
            "<Vertices>",
            "0,0,0,0",
            "1,0,0,0",
            "0,1,0,0",
            "<Triangles>",
            "0,1,2"
        };

        var reader = new MapObjectReader();
        reader.Load(lines);

        // "All" maps -256..4095
        Assert.IsTrue(reader.ObjectWrap.ContainsKey(-256));
        Assert.IsTrue(reader.ObjectWrap.ContainsKey(4095));
        Assert.IsTrue(reader.ObjectWrap.ContainsKey(0));
    }

    [TestMethod]
    public void Load_SkipsComments()
    {
        var lines = new[]
        {
            "; This is a comment",
            "[test]",
            "<Objects>",
            "; Another comment",
            "5",
            "<Colors>",
            "1,1,1",
            "<Vertices>",
            "0,0,0,0",
            "1,0,0,0",
            "0,1,0,0",
            "<Triangles>",
            "0,1,2"
        };

        var reader = new MapObjectReader();
        reader.Load(lines);

        Assert.AreEqual(1, reader.Objects.Count);
        Assert.AreEqual(5, reader.ObjectWrap.Keys.First());
    }
}
