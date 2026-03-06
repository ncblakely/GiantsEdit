using GiantsEdit.Core.DataModel;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class TreeNodeTests
{
    private static readonly string[] ExpectedABC = ["A", "B", "C"];
    private static readonly string[] ExpectedXY = ["x", "y"];
    private static readonly string[] ExpectedRootChild = ["Root", "Child"];
    private static readonly string[] ExpectedLeaf1Leaf2 = ["leaf1", "leaf2"];

    [TestMethod]
    public void CreateNode_NoRule_HasSingleSlot()
    {
        var node = new TreeNode("Root");
        Assert.AreEqual("Root", node.Name);
        Assert.AreEqual(0, node.NodeCount);
        Assert.AreEqual(0, node.LeafCount);
    }

    [TestMethod]
    public void AddNode_NoRule_AddsChild()
    {
        var root = new TreeNode("Root");
        var child = root.AddNode("Child");

        Assert.AreEqual(1, root.NodeCount);
        Assert.AreEqual("Child", child.Name);
        Assert.AreSame(root, child.Parent);
    }

    [TestMethod]
    public void AddLeaf_AllTypes_WorkCorrectly()
    {
        var root = new TreeNode("Root");

        root.AddByte("b", 42);
        root.AddInt32("i", -100);
        root.AddSingle("f", 3.14f);
        root.AddString("s", "hello");
        root.AddVoid("v");

        Assert.AreEqual(5, root.LeafCount);

        Assert.AreEqual((byte)42, root.GetChildLeaf("b").ByteValue);
        Assert.AreEqual(-100, root.GetChildLeaf("i").Int32Value);
        Assert.AreEqual(3.14f, root.GetChildLeaf("f").SingleValue);
        Assert.AreEqual("hello", root.GetChildLeaf("s").StringValue);
        Assert.AreEqual(PropertyType.Void, root.GetChildLeaf("v").PropertyType);
    }

    [TestMethod]
    public void EnumerateNodes_ReturnsAllChildren()
    {
        var root = new TreeNode("Root");
        root.AddNode("A");
        root.AddNode("B");
        root.AddNode("C");

        var names = root.EnumerateNodes().Select(n => n.Name).ToList();
        CollectionAssert.AreEqual(ExpectedABC, names);
    }

    [TestMethod]
    public void EnumerateLeaves_ReturnsAllLeaves()
    {
        var root = new TreeNode("Root");
        root.AddByte("x", 1);
        root.AddInt32("y", 2);

        var names = root.EnumerateLeaves().Select(l => l.Name).ToList();
        CollectionAssert.AreEqual(ExpectedXY, names);
    }

    [TestMethod]
    public void FindChildNode_Found_ReturnsNode()
    {
        var root = new TreeNode("Root");
        root.AddNode("Target");

        var found = root.FindChildNode("Target");
        Assert.IsNotNull(found);
        Assert.AreEqual("Target", found.Name);
    }

    [TestMethod]
    public void FindChildNode_NotFound_ReturnsNull()
    {
        var root = new TreeNode("Root");
        Assert.IsNull(root.FindChildNode("Missing"));
    }

    [TestMethod]
    public void GetChildLeaf_NotFound_Throws()
    {
        var root = new TreeNode("Root");
        Assert.ThrowsExactly<KeyNotFoundException>(() => root.GetChildLeaf("Missing"));
    }

    [TestMethod]
    public void HasChildNode_ReturnsCorrectly()
    {
        var root = new TreeNode("Root");
        root.AddNode("Exists");

        Assert.IsTrue(root.HasChildNode("Exists"));
        Assert.IsFalse(root.HasChildNode("Nope"));
    }

    [TestMethod]
    public void RemoveNode_RemovesChild()
    {
        var root = new TreeNode("Root");
        var child = root.AddNode("Child");

        Assert.IsTrue(root.RemoveNode(child));
        Assert.AreEqual(0, root.NodeCount);
        Assert.IsNull(child.Parent);
    }

    [TestMethod]
    public void RemoveLeaf_RemovesLeaf()
    {
        var root = new TreeNode("Root");
        var leaf = root.AddByte("b", 1);

        Assert.IsTrue(root.RemoveLeaf(leaf));
        Assert.AreEqual(0, root.LeafCount);
        Assert.IsNull(leaf.Parent);
    }

    [TestMethod]
    public void GetOrAddNode_ExistingNode_ReturnsSame()
    {
        var root = new TreeNode("Root");
        var first = root.AddNode("X");
        var second = root.GetOrAddNode("X");

        Assert.AreSame(first, second);
        Assert.AreEqual(1, root.NodeCount);
    }

    [TestMethod]
    public void GetOrAddNode_NewNode_Adds()
    {
        var root = new TreeNode("Root");
        var node = root.GetOrAddNode("New");

        Assert.AreEqual("New", node.Name);
        Assert.AreEqual(1, root.NodeCount);
    }

    [TestMethod]
    public void Walk_VisitsAllNodesAndLeaves()
    {
        var root = new TreeNode("Root");
        root.AddByte("leaf1", 1);
        var child = root.AddNode("Child");
        child.AddInt32("leaf2", 2);

        var nodeNames = new List<string>();
        var leafNames = new List<string>();
        root.Walk(n => nodeNames.Add(n.Name), l => leafNames.Add(l.Name));

        CollectionAssert.AreEqual(ExpectedRootChild, nodeNames);
        CollectionAssert.AreEqual(ExpectedLeaf1Leaf2, leafNames);
    }

    #region SetOrAdd tests

    [TestMethod]
    public void SetOrAddSingle_NewLeaf_CreatesIt()
    {
        var root = new TreeNode("Root");
        var leaf = root.SetOrAddSingle("X", 1.5f);

        Assert.AreEqual(1, root.LeafCount);
        Assert.AreEqual(1.5f, leaf.SingleValue);
        Assert.AreEqual(PropertyType.Single, leaf.PropertyType);
    }

    [TestMethod]
    public void SetOrAddSingle_ExistingLeaf_UpdatesValue()
    {
        var root = new TreeNode("Root");
        root.AddSingle("X", 1.0f);
        var leaf = root.SetOrAddSingle("X", 2.0f);

        Assert.AreEqual(1, root.LeafCount);
        Assert.AreEqual(2.0f, leaf.SingleValue);
    }

    [TestMethod]
    public void SetOrAddInt32_NewLeaf_CreatesIt()
    {
        var root = new TreeNode("Root");
        var leaf = root.SetOrAddInt32("Count", 42);

        Assert.AreEqual(1, root.LeafCount);
        Assert.AreEqual(42, leaf.Int32Value);
    }

    [TestMethod]
    public void SetOrAddInt32_ExistingLeaf_UpdatesValue()
    {
        var root = new TreeNode("Root");
        root.AddInt32("Count", 10);
        var leaf = root.SetOrAddInt32("Count", 20);

        Assert.AreEqual(1, root.LeafCount);
        Assert.AreEqual(20, leaf.Int32Value);
    }

    [TestMethod]
    public void SetOrAddByte_NewLeaf_CreatesIt()
    {
        var root = new TreeNode("Root");
        var leaf = root.SetOrAddByte("Mode", 5);

        Assert.AreEqual(1, root.LeafCount);
        Assert.AreEqual((byte)5, leaf.ByteValue);
    }

    [TestMethod]
    public void SetOrAddByte_ExistingLeaf_UpdatesValue()
    {
        var root = new TreeNode("Root");
        root.AddByte("Mode", 1);
        var leaf = root.SetOrAddByte("Mode", 2);

        Assert.AreEqual(1, root.LeafCount);
        Assert.AreEqual((byte)2, leaf.ByteValue);
    }

    [TestMethod]
    public void SetOrAddString_NewLeaf_CreatesIt()
    {
        var root = new TreeNode("Root");
        var leaf = root.SetOrAddString("Name", "hello");

        Assert.AreEqual(1, root.LeafCount);
        Assert.AreEqual("hello", leaf.StringValue);
    }

    [TestMethod]
    public void SetOrAddString_ExistingLeaf_UpdatesValue()
    {
        var root = new TreeNode("Root");
        root.AddString("Name", "old");
        var leaf = root.SetOrAddString("Name", "new");

        Assert.AreEqual(1, root.LeafCount);
        Assert.AreEqual("new", leaf.StringValue);
    }

    [TestMethod]
    public void SetOrAddStringL_RespectsMaxLength()
    {
        var root = new TreeNode("Root");
        var leaf = root.SetOrAddStringL("Tag", "abc", 16);

        Assert.AreEqual(16, leaf.MaxLength);
        Assert.AreEqual("abc", leaf.StringValue);
    }

    #endregion

    #region Typed getter tests

    [TestMethod]
    public void GetSingle_Found_ReturnsValue()
    {
        var root = new TreeNode("Root");
        root.AddSingle("X", 3.14f);
        Assert.AreEqual(3.14f, root.GetSingle("X"));
    }

    [TestMethod]
    public void GetSingle_NotFound_ReturnsDefault()
    {
        var root = new TreeNode("Root");
        Assert.AreEqual(0f, root.GetSingle("X"));
        Assert.AreEqual(1f, root.GetSingle("X", 1f));
    }

    [TestMethod]
    public void GetInt32_Found_ReturnsValue()
    {
        var root = new TreeNode("Root");
        root.AddInt32("Count", 99);
        Assert.AreEqual(99, root.GetInt32("Count"));
    }

    [TestMethod]
    public void GetInt32_NotFound_ReturnsDefault()
    {
        var root = new TreeNode("Root");
        Assert.AreEqual(0, root.GetInt32("Count"));
        Assert.AreEqual(-1, root.GetInt32("Count", -1));
    }

    [TestMethod]
    public void GetByte_Found_ReturnsValue()
    {
        var root = new TreeNode("Root");
        root.AddByte("Mode", 7);
        Assert.AreEqual((byte)7, root.GetByte("Mode"));
    }

    [TestMethod]
    public void GetByte_NotFound_ReturnsDefault()
    {
        var root = new TreeNode("Root");
        Assert.AreEqual((byte)0, root.GetByte("Mode"));
    }

    [TestMethod]
    public void GetString_Found_ReturnsValue()
    {
        var root = new TreeNode("Root");
        root.AddString("Name", "test");
        Assert.AreEqual("test", root.GetString("Name"));
    }

    [TestMethod]
    public void GetString_NotFound_ReturnsDefault()
    {
        var root = new TreeNode("Root");
        Assert.AreEqual("", root.GetString("Name"));
        Assert.AreEqual("fallback", root.GetString("Name", "fallback"));
    }

    #endregion
}
