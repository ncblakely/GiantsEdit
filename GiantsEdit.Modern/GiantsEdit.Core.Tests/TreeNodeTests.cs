using GiantsEdit.Core.DataModel;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class TreeNodeTests
{
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
        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, names);
    }

    [TestMethod]
    public void EnumerateLeaves_ReturnsAllLeaves()
    {
        var root = new TreeNode("Root");
        root.AddByte("x", 1);
        root.AddInt32("y", 2);

        var names = root.EnumerateLeaves().Select(l => l.Name).ToList();
        CollectionAssert.AreEqual(new[] { "x", "y" }, names);
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

        CollectionAssert.AreEqual(new[] { "Root", "Child" }, nodeNames);
        CollectionAssert.AreEqual(new[] { "leaf1", "leaf2" }, leafNames);
    }
}
