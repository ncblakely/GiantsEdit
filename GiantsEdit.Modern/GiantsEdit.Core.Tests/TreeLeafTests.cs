using GiantsEdit.Core.DataModel;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class TreeLeafTests
{
    [TestMethod]
    public void ByteValue_StoresAndRetrieves()
    {
        var leaf = new TreeLeaf { Name = "b", PropertyType = PropertyType.Byte };
        leaf.ByteValue = 255;
        Assert.AreEqual((byte)255, leaf.ByteValue);
    }

    [TestMethod]
    public void Int32Value_StoresAndRetrieves()
    {
        var leaf = new TreeLeaf { Name = "i", PropertyType = PropertyType.Int32 };
        leaf.Int32Value = int.MinValue;
        Assert.AreEqual(int.MinValue, leaf.Int32Value);
    }

    [TestMethod]
    public void SingleValue_StoresAndRetrieves()
    {
        var leaf = new TreeLeaf { Name = "f", PropertyType = PropertyType.Single };
        leaf.SingleValue = 1.23456f;
        Assert.AreEqual(1.23456f, leaf.SingleValue);
    }

    [TestMethod]
    public void SingleValue_PreservesBitPattern()
    {
        // The original Delphi code stores Single as its int32 bit pattern.
        // Verify we handle this correctly for special values.
        var leaf = new TreeLeaf { Name = "f", PropertyType = PropertyType.Single };
        leaf.SingleValue = float.NegativeInfinity;
        Assert.AreEqual(float.NegativeInfinity, leaf.SingleValue);

        leaf.SingleValue = 0f;
        Assert.AreEqual(0, leaf.RawInt32);
    }

    [TestMethod]
    public void StringValue_StoresAndRetrieves()
    {
        var leaf = new TreeLeaf { Name = "s", PropertyType = PropertyType.String };
        leaf.MaxLength = -1;
        leaf.StringValue = "test string";
        Assert.AreEqual("test string", leaf.StringValue);
    }

    [TestMethod]
    public void StringValue_WithMaxLength_EnforcesLimit()
    {
        var leaf = new TreeLeaf { Name = "s", PropertyType = PropertyType.String };
        leaf.MaxLength = 5;
        leaf.StringValue = "short";

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            leaf.StringValue = "this is too long");
    }

    [TestMethod]
    public void WrongType_Throws()
    {
        var leaf = new TreeLeaf { Name = "b", PropertyType = PropertyType.Byte };

        Assert.ThrowsExactly<InvalidOperationException>(() => _ = leaf.Int32Value);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = leaf.SingleValue);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = leaf.StringValue);
    }
}
