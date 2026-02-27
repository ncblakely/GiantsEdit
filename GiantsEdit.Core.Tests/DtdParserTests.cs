using GiantsEdit.Core.DataModel;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class DtdParserTests
{
    [TestMethod]
    public void Parse_SimpleNode_ParsesCorrectly()
    {
        var lines = new[]
        {
            "Node Root {",
            "  Leaf string \"Name\" once",
            "  Leaf int32 \"Value\" optional",
            "}"
        };

        var nodes = DtdParser.Parse(lines);

        Assert.AreEqual(1, nodes.Count);
        Assert.AreEqual("Root", nodes[0].Name);
        Assert.AreEqual(0, nodes[0].SubNodes.Count);
        Assert.AreEqual(2, nodes[0].SubLeaves.Count);

        Assert.AreEqual("Name", nodes[0].SubLeaves[0].Name);
        Assert.AreEqual(DtdBasicType.String, nodes[0].SubLeaves[0].BasicType);
        Assert.AreEqual(DtdCount.Once, nodes[0].SubLeaves[0].Count);

        Assert.AreEqual("Value", nodes[0].SubLeaves[1].Name);
        Assert.AreEqual(DtdBasicType.Int32, nodes[0].SubLeaves[1].BasicType);
        Assert.AreEqual(DtdCount.Optional, nodes[0].SubLeaves[1].Count);
    }

    [TestMethod]
    public void Parse_SubNodeReference_LinksCorrectly()
    {
        var lines = new[]
        {
            "Node Parent {",
            "  Node Child \"ChildEntry\" any",
            "}",
            "",
            "Node Child {",
            "  Leaf byte \"Data\" once",
            "}"
        };

        var nodes = DtdParser.Parse(lines);

        Assert.AreEqual(2, nodes.Count);
        Assert.AreEqual(1, nodes[0].SubNodes.Count);
        Assert.AreEqual("ChildEntry", nodes[0].SubNodes[0].Name);
        Assert.AreEqual(DtdCount.Any, nodes[0].SubNodes[0].Count);
        Assert.IsNotNull(nodes[0].SubNodes[0].Node);
        Assert.AreEqual("Child", nodes[0].SubNodes[0].Node!.Name);
    }

    [TestMethod]
    public void Parse_DisplayNameVsTypeName()
    {
        var lines = new[]
        {
            "Node Root {",
            "  Node Objects \"<Objects>\" once",
            "}",
            "Node Objects {",
            "}"
        };

        var nodes = DtdParser.Parse(lines);
        var sub = nodes[0].SubNodes[0];

        Assert.AreEqual("Objects", sub.NodeTypeName);
        Assert.AreEqual("<Objects>", sub.Name);
    }

    [TestMethod]
    public void Parse_AllCountTypes()
    {
        var lines = new[]
        {
            "Node Test {",
            "  Leaf byte \"a\" any",
            "  Leaf byte \"b\" once",
            "  Leaf byte \"c\" optional",
            "  Leaf byte \"d\" multiple",
            "}"
        };

        var nodes = DtdParser.Parse(lines);
        Assert.AreEqual(DtdCount.Any, nodes[0].SubLeaves[0].Count);
        Assert.AreEqual(DtdCount.Once, nodes[0].SubLeaves[1].Count);
        Assert.AreEqual(DtdCount.Optional, nodes[0].SubLeaves[2].Count);
        Assert.AreEqual(DtdCount.Multiple, nodes[0].SubLeaves[3].Count);
    }

    [TestMethod]
    public void Parse_AllBasicTypes()
    {
        var lines = new[]
        {
            "Node Test {",
            "  Leaf byte \"a\" once",
            "  Leaf int32 \"b\" once",
            "  Leaf single \"c\" once",
            "  Leaf string \"d\" once",
            "  Leaf void \"e\" once",
            "}"
        };

        var nodes = DtdParser.Parse(lines);
        Assert.AreEqual(DtdBasicType.Byte, nodes[0].SubLeaves[0].BasicType);
        Assert.AreEqual(DtdBasicType.Int32, nodes[0].SubLeaves[1].BasicType);
        Assert.AreEqual(DtdBasicType.Single, nodes[0].SubLeaves[2].BasicType);
        Assert.AreEqual(DtdBasicType.String, nodes[0].SubLeaves[3].BasicType);
        Assert.AreEqual(DtdBasicType.Void, nodes[0].SubLeaves[4].BasicType);
    }

    [TestMethod]
    public void Parse_EmptyLines_Ignored()
    {
        var lines = new[]
        {
            "",
            "Node Root {",
            "",
            "  Leaf byte \"x\" once",
            "",
            "}",
            ""
        };

        var nodes = DtdParser.Parse(lines);
        Assert.AreEqual(1, nodes.Count);
        Assert.AreEqual(1, nodes[0].SubLeaves.Count);
    }

    [TestMethod]
    public void Parse_NodeWithoutDisplayName_UsesTypeName()
    {
        var lines = new[]
        {
            "Node Root {",
            "  Node Sub any",
            "}",
            "Node Sub {",
            "}"
        };

        var nodes = DtdParser.Parse(lines);
        Assert.AreEqual("Sub", nodes[0].SubNodes[0].Name);
        Assert.AreEqual("Sub", nodes[0].SubNodes[0].NodeTypeName);
    }
}
