using GiantsEdit.Core.Formats;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class BinaryDataRoundTripTests
{
    [TestMethod]
    public void ReadWrite_Byte_RoundTrips()
    {
        var w = new BinaryDataWriter();
        w.WriteByte(0xAB);
        var r = new BinaryDataReader(w.ToArray());
        Assert.AreEqual((byte)0xAB, r.ReadByte());
    }

    [TestMethod]
    public void ReadWrite_Int32_RoundTrips()
    {
        var w = new BinaryDataWriter();
        w.WriteInt32(-12345);
        var r = new BinaryDataReader(w.ToArray());
        Assert.AreEqual(-12345, r.ReadInt32());
    }

    [TestMethod]
    public void ReadWrite_Single_RoundTrips()
    {
        var w = new BinaryDataWriter();
        w.WriteSingle(3.14f);
        var r = new BinaryDataReader(w.ToArray());
        Assert.AreEqual(3.14f, r.ReadSingle());
    }

    [TestMethod]
    public void ReadWrite_String16_RoundTrips()
    {
        var w = new BinaryDataWriter();
        w.WriteString16("hello");
        var data = w.ToArray();
        Assert.AreEqual(16, data.Length);
        var r = new BinaryDataReader(data);
        Assert.AreEqual("hello", r.ReadString16());
    }

    [TestMethod]
    public void ReadWrite_String32_RoundTrips()
    {
        var w = new BinaryDataWriter();
        w.WriteString32("terrain.tga");
        var r = new BinaryDataReader(w.ToArray());
        Assert.AreEqual("terrain.tga", r.ReadString32());
    }

    [TestMethod]
    public void ReadWrite_PChar_RoundTrips()
    {
        var w = new BinaryDataWriter();
        w.WriteString0("box01");
        var r = new BinaryDataReader(w.ToArray());
        Assert.AreEqual("box01", r.ReadPChar());
    }

    [TestMethod]
    public void ReadWrite_BLString_RoundTrips()
    {
        var w = new BinaryDataWriter();
        w.WriteLString0("myfile.tga");
        var r = new BinaryDataReader(w.ToArray());
        Assert.AreEqual("myfile.tga", r.ReadBLString());
    }

    [TestMethod]
    public void ReadWrite_RGB_RoundTrips()
    {
        var w = new BinaryDataWriter();
        w.WriteRgb(10, 20, 30);
        var r = new BinaryDataReader(w.ToArray());
        var (red, green, blue) = r.ReadRgb();
        Assert.AreEqual((byte)10, red);
        Assert.AreEqual((byte)20, green);
        Assert.AreEqual((byte)30, blue);
    }

    [TestMethod]
    public void PatchInt32_OverwritesAtPosition()
    {
        var w = new BinaryDataWriter();
        w.WriteInt32(0); // placeholder
        w.WriteInt32(999);
        w.PatchInt32(0, 42);

        var r = new BinaryDataReader(w.ToArray());
        Assert.AreEqual(42, r.ReadInt32());
        Assert.AreEqual(999, r.ReadInt32());
    }
}
