using GiantsEdit.Core.DataModel;
using GiantsEdit.Core.Formats;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class BinMissionRoundTripTests
{
    [TestMethod]
    public void LoadSave_SimpleObjects_RoundTrips()
    {
        var root = new TreeNode("Mission data");
        var objects = root.AddNode(BinFormatConstants.GroupObjects);

        // 1-angle object
        var obj1 = objects.AddNode(BinFormatConstants.NodeObject);
        obj1.AddInt32("Type", 50);
        obj1.AddSingle("X", 1.0f);
        obj1.AddSingle("Y", 2.0f);
        obj1.AddSingle("Z", 3.0f);
        obj1.AddSingle("DirFacing", 0.5f);

        // 3-angle object
        var obj2 = objects.AddNode(BinFormatConstants.NodeObject);
        obj2.AddInt32("Type", 100);
        obj2.AddSingle("X", -1.0f);
        obj2.AddSingle("Y", -2.0f);
        obj2.AddSingle("Z", -3.0f);
        obj2.AddSingle("DirFacing", 0.1f);
        obj2.AddSingle("TiltForward", 0.2f);
        obj2.AddSingle("TiltLeft", 0.3f);

        var writer = new BinMissionWriter();
        byte[] data = writer.Save(root);

        var reader = new BinMissionReader();
        var loaded = reader.Load(data);

        Assert.IsNotNull(loaded);
        var objs = loaded!.GetChildNode(BinFormatConstants.GroupObjects).EnumerateNodes().ToList();
        Assert.HasCount(2, objs);

        Assert.AreEqual(50, objs[0].GetChildLeaf("Type").Int32Value);
        Assert.AreEqual(0.5f, objs[0].GetChildLeaf("DirFacing").SingleValue);

        Assert.AreEqual(100, objs[1].GetChildLeaf("Type").Int32Value);
        Assert.AreEqual(0.2f, objs[1].GetChildLeaf("TiltForward").SingleValue);
    }

    [TestMethod]
    public void LoadSave_WithAttributes_RoundTrips()
    {
        var root = new TreeNode("Mission data");
        var objects = root.AddNode(BinFormatConstants.GroupObjects);

        var obj = objects.AddNode(BinFormatConstants.NodeObject);
        obj.AddInt32("Type", 42);
        obj.AddSingle("X", 0f);
        obj.AddSingle("Y", 0f);
        obj.AddSingle("Z", 0f);
        obj.AddSingle("DirFacing", 0f);
        obj.AddByte("AIMode", 3);
        obj.AddInt32("TeamID", 2);
        obj.AddSingle("Scale", 1.5f);

        var writer = new BinMissionWriter();
        byte[] data = writer.Save(root);

        var reader = new BinMissionReader();
        var loaded = reader.Load(data);

        Assert.IsNotNull(loaded);
        var loadedObj = loaded!.GetChildNode(BinFormatConstants.GroupObjects).EnumerateNodes().First();
        Assert.AreEqual((byte)3, loadedObj.GetChildLeaf("AIMode").ByteValue);
        Assert.AreEqual(2, loadedObj.GetChildLeaf("TeamID").Int32Value);
        Assert.AreEqual(1.5f, loadedObj.GetChildLeaf("Scale").SingleValue);
    }

    [TestMethod]
    public void Load_InvalidMagic_ReturnsNull()
    {
        var w = new BinaryDataWriter();
        w.WriteInt32(99); // wrong magic
        w.WriteByte(1);

        var reader = new BinMissionReader();
        Assert.IsNull(reader.Load(w.ToArray()));
    }
}
