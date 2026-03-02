using GiantsEdit.Core.DataModel;
using GiantsEdit.Core.Formats;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class BinMissionRoundTripTests
{
    private const string TestDataDir = "TestData";

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

        byte[] data = BinMissionWriter.Save(root);

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

        byte[] data = BinMissionWriter.Save(root);

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

    private static IEnumerable<object[]> MissionFiles =>
        Directory.GetFiles(TestDataDir, "wm_*.bin")
            .Select(f => new object[] { Path.GetFileName(f) });

    [TestMethod]
    [DynamicData(nameof(MissionFiles))]
    public void LoadSave_MissionFile_ByteExactRoundTrip(string fileName)
    {
        string path = Path.Combine(TestDataDir, fileName);
        byte[] original = File.ReadAllBytes(path);

        var reader = new BinMissionReader();
        var tree = reader.Load(original);
        Assert.IsNotNull(tree, $"Failed to load {fileName}");

        byte[] resaved = BinMissionWriter.Save(tree!);

        Assert.HasCount(original.Length, resaved,
            $"Byte length mismatch in {fileName}: original={original.Length}, resaved={resaved.Length}");

        int firstDiff = -1;
        for (int i = 0; i < original.Length; i++)
        {
            if (original[i] != resaved[i])
            {
                firstDiff = i;
                break;
            }
        }

        Assert.AreEqual(-1, firstDiff,
            firstDiff >= 0
                ? $"First byte difference in {fileName} at offset {firstDiff}: original=0x{original[firstDiff]:X2}, resaved=0x{resaved[firstDiff]:X2}"
                : "");
    }

    [TestMethod]
    [DynamicData(nameof(MissionFiles))]
    public void LoadSave_MissionFile_TreeRoundTrip(string fileName)
    {
        string path = Path.Combine(TestDataDir, fileName);
        byte[] original = File.ReadAllBytes(path);

        var reader = new BinMissionReader();
        var tree = reader.Load(original);
        Assert.IsNotNull(tree, $"Failed to load {fileName}");

        byte[] resaved = BinMissionWriter.Save(tree!);

        var reloaded = reader.Load(resaved);
        Assert.IsNotNull(reloaded, $"Failed to reload {fileName}");

        var errors = CompareTreeNodes(tree!, reloaded!, "");
        if (errors.Count > 0)
            Assert.Fail($"Tree mismatch in {fileName}:\n{string.Join("\n", errors.Take(20))}");
    }

    [TestMethod]
    [DynamicData(nameof(MissionFiles))]
    public void Load_MissionFile_HasExpectedStructure(string fileName)
    {
        string path = Path.Combine(TestDataDir, fileName);
        byte[] data = File.ReadAllBytes(path);

        var reader = new BinMissionReader();
        var tree = reader.Load(data);
        Assert.IsNotNull(tree, $"Failed to load {fileName}");

        var objects = tree!.FindChildNode(BinFormatConstants.GroupObjects);
        Assert.IsNotNull(objects, $"Missing <Objects> group in {fileName}");

        foreach (var obj in objects!.EnumerateNodes())
        {
            Assert.IsNotNull(obj.FindChildLeaf("Type"), $"Object missing Type leaf in {fileName}");
            Assert.IsNotNull(obj.FindChildLeaf("X"), $"Object missing X leaf in {fileName}");
            Assert.IsNotNull(obj.FindChildLeaf("Y"), $"Object missing Y leaf in {fileName}");
            Assert.IsNotNull(obj.FindChildLeaf("Z"), $"Object missing Z leaf in {fileName}");
            Assert.IsNotNull(obj.FindChildLeaf("DirFacing"), $"Object missing DirFacing leaf in {fileName}");
        }
    }

    private static List<string> CompareTreeNodes(TreeNode orig, TreeNode saved, string path)
    {
        var errors = new List<string>();
        string nodePath = string.IsNullOrEmpty(path) ? orig.Name : $"{path}/{orig.Name}";

        if (orig.Name != saved.Name)
        {
            errors.Add($"Node name mismatch at '{path}': '{orig.Name}' vs '{saved.Name}'");
            return errors;
        }

        var origLeaves = orig.EnumerateLeaves().ToList();
        var savedLeaves = saved.EnumerateLeaves().ToList();
        if (origLeaves.Count != savedLeaves.Count)
        {
            errors.Add($"Leaf count at '{nodePath}': {origLeaves.Count} vs {savedLeaves.Count}");
        }
        else
        {
            for (int i = 0; i < origLeaves.Count; i++)
            {
                var ol = origLeaves[i];
                var sl = savedLeaves[i];
                if (ol.Name != sl.Name)
                    errors.Add($"Leaf name at '{nodePath}[{i}]': '{ol.Name}' vs '{sl.Name}'");
                else if (ol.PropertyType != sl.PropertyType)
                    errors.Add($"Leaf type at '{nodePath}/{ol.Name}': {ol.PropertyType} vs {sl.PropertyType}");
                else if (ol.RawInt32 != sl.RawInt32 && ol.PropertyType != PropertyType.String)
                    errors.Add($"Leaf value at '{nodePath}/{ol.Name}': {ol.RawInt32} vs {sl.RawInt32}");
                else if (ol.PropertyType == PropertyType.String && ol.StringValue != sl.StringValue)
                    errors.Add($"Leaf string at '{nodePath}/{ol.Name}': '{ol.StringValue}' vs '{sl.StringValue}'");
            }
        }

        var origNodes = orig.EnumerateNodes().ToList();
        var savedNodes = saved.EnumerateNodes().ToList();
        if (origNodes.Count != savedNodes.Count)
        {
            errors.Add($"Child node count at '{nodePath}': {origNodes.Count} vs {savedNodes.Count}");
        }
        else
        {
            for (int i = 0; i < origNodes.Count; i++)
                errors.AddRange(CompareTreeNodes(origNodes[i], savedNodes[i], nodePath));
        }

        return errors;
    }
}
