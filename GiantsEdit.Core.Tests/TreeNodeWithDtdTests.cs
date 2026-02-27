using GiantsEdit.Core.DataModel;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class TreeNodeWithDtdTests
{
    private static DtdNode CreateObjectRule()
    {
        // Mimics a simplified version of the Object node from w.dtd
        var rule = new DtdNode { Name = "Object" };
        rule.SubLeaves.Add(new DtdSubLeaf { Name = "Type", LeafTypeName = "int32", BasicType = DtdBasicType.Int32, Count = DtdCount.Once });
        rule.SubLeaves.Add(new DtdSubLeaf { Name = "X", LeafTypeName = "single", BasicType = DtdBasicType.Single, Count = DtdCount.Optional });
        rule.SubLeaves.Add(new DtdSubLeaf { Name = "Y", LeafTypeName = "single", BasicType = DtdBasicType.Single, Count = DtdCount.Optional });
        rule.SubLeaves.Add(new DtdSubLeaf { Name = "Z", LeafTypeName = "single", BasicType = DtdBasicType.Single, Count = DtdCount.Optional });
        return rule;
    }

    [TestMethod]
    public void AddLeaf_WithDtdRule_ValidName_Succeeds()
    {
        var rule = CreateObjectRule();
        var node = new TreeNode("Object", rule);

        node.AddInt32("Type", 42);
        node.AddSingle("X", 1.0f);

        Assert.AreEqual(2, node.LeafCount);
        Assert.AreEqual(42, node.GetChildLeaf("Type").Int32Value);
        Assert.AreEqual(1.0f, node.GetChildLeaf("X").SingleValue);
    }

    [TestMethod]
    public void AddLeaf_WithDtdRule_InvalidName_Throws()
    {
        var rule = CreateObjectRule();
        var node = new TreeNode("Object", rule);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            node.AddInt32("Nonexistent", 0));
    }

    [TestMethod]
    public void AddLeaf_WithDtdRule_DuplicateOnce_Throws()
    {
        var rule = CreateObjectRule();
        var node = new TreeNode("Object", rule);

        node.AddInt32("Type", 1);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            node.AddInt32("Type", 2));
    }

    [TestMethod]
    public void NodeCount_ByName_ReturnsCorrectCount()
    {
        var objectsRule = new DtdNode { Name = "Objects" };
        var objectRule = new DtdNode { Name = "Object" };
        objectsRule.SubNodes.Add(new DtdSubNode { Name = "Object", NodeTypeName = "Object", Node = objectRule, Count = DtdCount.Any });

        var node = new TreeNode("Objects", objectsRule);
        node.AddNode("Object");
        node.AddNode("Object");
        node.AddNode("Object");

        Assert.AreEqual(3, node.NodeCount_ByName("Object"));
    }

    [TestMethod]
    public void Parse_RealDtd_IntegrationTest()
    {
        // Parse the actual w.dtd file if it exists
        // Try relative to solution root
        var dtdPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "w.dtd"));
        if (!File.Exists(dtdPath))
        {
            Assert.Inconclusive("w.dtd not found for integration test.");
            return;
        }

        var nodes = DtdParser.LoadFromFile(dtdPath);
        Assert.IsTrue(nodes.Count > 0, "Expected at least one DTD node.");

        // Verify MapData root is present
        var mapData = nodes.FirstOrDefault(n => n.Name == "MapData");
        Assert.IsNotNull(mapData, "Expected MapData node in w.dtd.");
        Assert.IsTrue(mapData.SubNodes.Count > 0, "MapData should have sub-nodes.");
    }

    public TestContext TestContext { get; set; } = null!;
}
