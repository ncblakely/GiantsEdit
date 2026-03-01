using GiantsEdit.Core.Formats;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class BinaryDataReaderTests
{
    [TestMethod]
    public void ReadByte_EmptyData_Throws()
    {
        var reader = new BinaryDataReader([]);
        Assert.ThrowsExactly<EndOfStreamException>(() => reader.ReadByte());
    }

    [TestMethod]
    public void ReadInt32_InsufficientBytes_Throws()
    {
        var reader = new BinaryDataReader(new byte[3]);
        Assert.ThrowsExactly<EndOfStreamException>(() => reader.ReadInt32());
    }

    [TestMethod]
    public void ReadSingle_InsufficientBytes_Throws()
    {
        var reader = new BinaryDataReader(new byte[2]);
        Assert.ThrowsExactly<EndOfStreamException>(() => reader.ReadSingle());
    }

    [TestMethod]
    public void ReadWord_InsufficientBytes_Throws()
    {
        var reader = new BinaryDataReader(new byte[1]);
        Assert.ThrowsExactly<EndOfStreamException>(() => reader.ReadWord());
    }

    [TestMethod]
    public void ReadUInt32_InsufficientBytes_Throws()
    {
        var reader = new BinaryDataReader(new byte[3]);
        Assert.ThrowsExactly<EndOfStreamException>(() => reader.ReadUInt32());
    }

    [TestMethod]
    public void ReadBLString_InsufficientBytes_Throws()
    {
        var reader = new BinaryDataReader([]);
        Assert.ThrowsExactly<EndOfStreamException>(() => reader.ReadBLString());
    }

    [TestMethod]
    public void ReadRgb_InsufficientBytes_Throws()
    {
        var reader = new BinaryDataReader(new byte[2]);
        Assert.ThrowsExactly<EndOfStreamException>(() => reader.ReadRgb());
    }

    [TestMethod]
    public void ReadByte_ValidData_ReturnsCorrectValue()
    {
        var reader = new BinaryDataReader([0xAB]);
        Assert.AreEqual((byte)0xAB, reader.ReadByte());
    }

    [TestMethod]
    public void ReadInt32_ValidData_ReturnsCorrectValue()
    {
        var reader = new BinaryDataReader(BitConverter.GetBytes(42));
        Assert.AreEqual(42, reader.ReadInt32());
    }

    [TestMethod]
    public void ReadSingle_ValidData_ReturnsCorrectValue()
    {
        var reader = new BinaryDataReader(BitConverter.GetBytes(3.14f));
        Assert.AreEqual(3.14f, reader.ReadSingle(), 0.001f);
    }

    [TestMethod]
    public void ReadWord_ValidData_ReturnsCorrectValue()
    {
        var reader = new BinaryDataReader(BitConverter.GetBytes((ushort)1234));
        Assert.AreEqual((ushort)1234, reader.ReadWord());
    }

    [TestMethod]
    public void ReadUInt32_ValidData_ReturnsCorrectValue()
    {
        var reader = new BinaryDataReader(BitConverter.GetBytes(123456u));
        Assert.AreEqual(123456u, reader.ReadUInt32());
    }

    [TestMethod]
    public void ReadFixedString_ValidData_ReturnsCorrectValue()
    {
        var data = new byte[] { 0x48, 0x69, 0x00, 0x00, 0x00 }; // "Hi" + null padding
        var reader = new BinaryDataReader(data);
        Assert.AreEqual("Hi", reader.ReadFixedString(5));
    }

    [TestMethod]
    public void ReadPChar_ValidData_ReadsUntilNull()
    {
        var data = new byte[] { 0x41, 0x42, 0x43, 0x00 }; // "ABC\0"
        var reader = new BinaryDataReader(data);
        Assert.AreEqual("ABC", reader.ReadPChar());
    }

    [TestMethod]
    public void HasMore_AfterReadingAll_ReturnsFalse()
    {
        var reader = new BinaryDataReader([0x01]);
        reader.ReadByte();
        Assert.IsFalse(reader.HasMore);
    }

    [TestMethod]
    public void Position_SetAndGet_Works()
    {
        var reader = new BinaryDataReader(new byte[10]);
        reader.Position = 5;
        Assert.AreEqual(5, reader.Position);
    }

    [TestMethod]
    public void MultipleReads_AdvancesPosition()
    {
        var data = new byte[8];
        BitConverter.GetBytes(42).CopyTo(data, 0);
        BitConverter.GetBytes(99).CopyTo(data, 4);
        var reader = new BinaryDataReader(data);

        Assert.AreEqual(0, reader.Position);
        reader.ReadInt32();
        Assert.AreEqual(4, reader.Position);
        reader.ReadInt32();
        Assert.AreEqual(8, reader.Position);
    }
}
