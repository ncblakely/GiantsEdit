using GiantsEdit.Core.DataModel;

namespace GiantsEdit.Core.Formats;

/// <summary>
/// Reads and writes mission .bin files (wm_*.bin).
/// Ported from Delphi's bin_w_read.pas LoadBinWM / ReadMissionChunk.
/// Mission files have a simpler format: magic=4, version=1, chunks $03-$26, terminator $02.
/// </summary>
public class BinMissionReader
{
    private BinaryDataReader _r = null!;
    private TreeNode _currentObject = null!;

    /// <summary>
    /// Loads a mission .bin file and returns the mission tree root, or null if invalid.
    /// </summary>
    public TreeNode? Load(byte[] data)
    {
        _r = new BinaryDataReader(data);

        int magic = _r.ReadInt32();
        byte version = _r.ReadByte();

        if (magic != 4 || version != 1)
            return null;

        var root = new TreeNode("Mission data");
        var objects = root.AddNode(BinFormatConstants.GroupObjects);

        _currentObject = objects;

        while (_r.Position < data.Length)
        {
            byte chunkId = _r.ReadByte();
            if (chunkId == 0x02)
                break;
            ReadMissionChunk(chunkId, root, objects);
        }

        return root;
    }

    private void ReadMissionChunk(byte chunkId, TreeNode root, TreeNode objects)
    {
        switch (chunkId)
        {
            case 0x03: // 1-angled object
                {
                    var obj = objects.AddNode("Object");
                    _currentObject = obj;
                    obj.AddInt32("Type", _r.ReadInt32());
                    obj.AddSingle("X", _r.ReadSingle());
                    obj.AddSingle("Y", _r.ReadSingle());
                    obj.AddSingle("Z", _r.ReadSingle());
                    obj.AddSingle("DirFacing", _r.ReadSingle());
                    break;
                }
            case 0x04: // 3-angled object
                {
                    var obj = objects.AddNode("Object");
                    _currentObject = obj;
                    obj.AddInt32("Type", _r.ReadInt32());
                    obj.AddSingle("X", _r.ReadSingle());
                    obj.AddSingle("Y", _r.ReadSingle());
                    obj.AddSingle("Z", _r.ReadSingle());
                    obj.AddSingle("DirFacing", _r.ReadSingle());
                    obj.AddSingle("TiltForward", _r.ReadSingle());
                    obj.AddSingle("TiltLeft", _r.ReadSingle());
                    break;
                }
            case 0x05:
                _currentObject.AddByte("AIMode", _r.ReadByte());
                break;
            case 0x06:
                _currentObject.AddSingle("OData1", _r.ReadSingle());
                _currentObject.AddSingle("OData2", _r.ReadSingle());
                _currentObject.AddSingle("OData3", _r.ReadSingle());
                break;
            case 0x07:
                _currentObject.AddInt32("TeamID", _r.ReadInt32());
                break;
            case 0x08:
                _currentObject.AddByte("TriggerType", _r.ReadByte());
                break;
            case 0x09:
                _currentObject.AddSingle("Scale", _r.ReadSingle());
                break;
            case 0x0A:
                {
                    int count = _r.ReadByte();
                    _currentObject.AddByte("AIDataCount", (byte)count);
                    for (int i = 0; i < count; i++)
                        _currentObject.AddSingle("AIData", _r.ReadSingle());
                    break;
                }
            case 0x0B:
                {
                    var opts = root.GetOrAddNode("<Options>");
                    opts.AddInt32("Character", _r.ReadInt32());
                    opts.AddInt32("TeamMembers", _r.ReadInt32());
                    opts.AddInt32("MarkerID", _r.ReadInt32());
                    opts.AddInt32("KabutoSize", _r.ReadInt32());
                    break;
                }
            case 0x0C:
                root.GetOrAddNode("<Options>").AddInt32("SmartieType", _r.ReadInt32());
                break;
            case 0x0D:
                root.GetOrAddNode("<Options>").AddInt32("SpecialText", _r.ReadInt32());
                break;
            case 0x0E:
                {
                    var opts = root.GetOrAddNode("<Options>");
                    int count = _r.ReadInt32();
                    opts.AddInt32("IconCount", count);
                    for (int i = 0; i < count; i++)
                        opts.AddInt32("Icons", _r.ReadInt32());
                    break;
                }
            case 0x0F:
                root.GetOrAddNode("<Options>").AddInt32("VimpMeat", _r.ReadInt32());
                break;
            case 0x10:
                root.GetOrAddNode("<Options>").AddInt32("NoNitro", _r.ReadInt32());
                break;
            case 0x11:
                root.GetOrAddNode("<Options>").AddInt32("NoJetpack", _r.ReadInt32());
                break;
            case 0x12: // FailTime
                root.GetOrAddNode("<Options>").AddInt32("FailTime", _r.ReadInt32());
                break;
            case 0x13:
                root.GetOrAddNode("<Options>").AddString("FailFlick", _r.ReadString32());
                break;
            case 0x14:
                _currentObject.AddString("FlickUsed", _r.ReadString32());
                break;
            case 0x15:
                root.GetOrAddNode("<Options>").AddString("StartFlick", _r.ReadString32());
                break;
            case 0x16:
                {
                    var lockNode = _currentObject.AddNode("Lock");
                    _currentObject = lockNode;
                    lockNode.AddInt32("Type", _r.ReadInt32());
                    lockNode.AddByte("LockRefSrc", _r.ReadByte());
                    lockNode.AddByte("LockRefDst", _r.ReadByte());
                    break;
                }
            case 0x17: // Pop lock
                if (_currentObject.Parent != null)
                    _currentObject = _currentObject.Parent;
                break;
            case 0x18:
                _currentObject.AddSingle("Directions 0", _r.ReadSingle());
                _currentObject.AddSingle("Directions 1", _r.ReadSingle());
                _currentObject.AddSingle("Directions 2", _r.ReadSingle());
                break;
            case 0x19:
                root.GetOrAddNode("<Options>").AddString("EndFlick", _r.ReadString32());
                break;
            case 0x1A:
                _currentObject.AddSingle("SplineScale 0", _r.ReadSingle());
                _currentObject.AddSingle("SplineScale 1", _r.ReadSingle());
                break;
            case 0x1B: // SplineTangents
                _currentObject.AddSingle("SplineTangent 0", _r.ReadSingle());
                _currentObject.AddSingle("SplineTangent 1", _r.ReadSingle());
                break;
            case 0x1C:
                _currentObject.AddVoid("SplinePath3D");
                break;
            case 0x1D: // SplineJet
                _currentObject.AddVoid("SplineJet");
                break;
            case 0x1E:
                {
                    int count = _r.ReadInt32();
                    _currentObject.AddInt32("MinishopRIconCount", count);
                    for (int i = 0; i < count; i++)
                        _currentObject.AddInt32("MinishopRIcon", _r.ReadInt32());
                    break;
                }
            case 0x1F:
                {
                    int count = _r.ReadInt32();
                    _currentObject.AddInt32("MinishopMIconCount", count);
                    for (int i = 0; i < count; i++)
                        _currentObject.AddInt32("MinishopMIcon", _r.ReadInt32());
                    break;
                }
            case 0x20:
                _currentObject.AddByte("SplineStartID", _r.ReadByte());
                break;
            case 0x21:
                _currentObject.AddInt32("KeyTime", _r.ReadInt32());
                break;
            case 0x22:
                root.GetOrAddNode("<Options>").AddVoid("JetskiRace");
                break;
            case 0x23: // BumpClampValue
                root.GetOrAddNode("<Options>").AddSingle("BumpClampValue", _r.ReadSingle());
                break;
            case 0x24: // ZeroVimpMeat
                root.GetOrAddNode("<Options>").AddVoid("ZeroVimpMeat");
                break;
            case 0x25:
                root.GetOrAddNode("<Options>").AddVoid("GrabSmartie");
                break;
            case 0x26:
                {
                    var opts = root.GetOrAddNode("<Options>");
                    opts.AddSingle("SuccessDelay 0", _r.ReadSingle());
                    opts.AddSingle("SuccessDelay 1", _r.ReadSingle());
                    break;
                }
            default:
                throw new FormatException($"Unknown mission chunk ID: 0x{chunkId:X2}");
        }
    }
}

/// <summary>
/// Writes mission .bin files.
/// </summary>
public class BinMissionWriter
{
    public byte[] Save(TreeNode root)
    {
        var w = new BinaryDataWriter();

        // Header: magic=4, version=1
        w.WriteInt32(4);
        w.WriteByte(1);

        // Write objects
        var objects = root.FindChildNode(BinFormatConstants.GroupObjects);
        if (objects != null)
        {
            foreach (var obj in objects.EnumerateNodes())
                WriteObject(w, obj);
        }

        // Write options after objects
        var options = root.FindChildNode("<Options>");
        if (options != null)
            WriteOptions(w, options);

        // Terminator
        w.WriteByte(0x02);

        return w.ToArray();
    }

    private void WriteOptions(BinaryDataWriter w, TreeNode options)
    {
        var leaves = options.EnumerateLeaves().ToList();

        foreach (var leaf in leaves)
        {
            // Skip TeamMembers/MarkerID/KabutoSize, written with Character
            if (leaf.Name is "TeamMembers" or "MarkerID" or "KabutoSize") continue;
            // Skip SuccessDelay 1, written with SuccessDelay 0
            if (leaf.Name == "SuccessDelay 1") continue;

            switch (leaf.Name)
            {
                case "Character":
                    w.WriteByte(0x0B);
                    w.WriteInt32(leaf.Int32Value);
                    w.WriteInt32(options.GetChildLeaf("TeamMembers").Int32Value);
                    w.WriteInt32(options.GetChildLeaf("MarkerID").Int32Value);
                    w.WriteInt32(options.GetChildLeaf("KabutoSize").Int32Value);
                    break;
                case "SmartieType":
                    w.WriteByte(0x0C);
                    w.WriteInt32(leaf.Int32Value);
                    break;
                case "SpecialText":
                    w.WriteByte(0x0D);
                    w.WriteInt32(leaf.Int32Value);
                    break;
                case "IconCount":
                    {
                        w.WriteByte(0x0E);
                        w.WriteInt32(leaf.Int32Value);
                        foreach (var icon in leaves.Where(l => l.Name == "Icons"))
                            w.WriteInt32(icon.Int32Value);
                    }
                    break;
                case "Icons":
                    break; // written with IconCount
                case "VimpMeat":
                    w.WriteByte(0x0F);
                    w.WriteInt32(leaf.Int32Value);
                    break;
                case "NoNitro":
                    w.WriteByte(0x10);
                    w.WriteInt32(leaf.Int32Value);
                    break;
                case "NoJetpack":
                    w.WriteByte(0x11);
                    w.WriteInt32(leaf.Int32Value);
                    break;
                case "FailTime":
                    w.WriteByte(0x12);
                    w.WriteInt32(leaf.Int32Value);
                    break;
                case "FailFlick":
                    w.WriteByte(0x13);
                    w.WriteString32(leaf.StringValue);
                    break;
                case "StartFlick":
                    w.WriteByte(0x15);
                    w.WriteString32(leaf.StringValue);
                    break;
                case "EndFlick":
                    w.WriteByte(0x19);
                    w.WriteString32(leaf.StringValue);
                    break;
                case "JetskiRace":
                    w.WriteByte(0x22);
                    break;
                case "BumpClampValue":
                    w.WriteByte(0x23);
                    w.WriteSingle(leaf.SingleValue);
                    break;
                case "ZeroVimpMeat":
                    w.WriteByte(0x24);
                    break;
                case "GrabSmartie":
                    w.WriteByte(0x25);
                    break;
                case "SuccessDelay 0":
                    w.WriteByte(0x26);
                    w.WriteSingle(leaf.SingleValue);
                    w.WriteSingle(options.GetChildLeaf("SuccessDelay 1").SingleValue);
                    break;
            }
        }
    }

    private void WriteObject(BinaryDataWriter w, TreeNode obj)
    {
        bool hasTilt = obj.FindChildLeaf("TiltForward") != null;

        if (hasTilt)
        {
            w.WriteByte(0x04);
            w.WriteInt32(obj.GetChildLeaf("Type").Int32Value);
            w.WriteSingle(obj.GetChildLeaf("X").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("Y").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("Z").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("DirFacing").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("TiltForward").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("TiltLeft").SingleValue);
        }
        else
        {
            w.WriteByte(0x03);
            w.WriteInt32(obj.GetChildLeaf("Type").Int32Value);
            w.WriteSingle(obj.GetChildLeaf("X").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("Y").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("Z").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("DirFacing").SingleValue);
        }

        var headerNames = new HashSet<string> { "Type", "X", "Y", "Z", "DirFacing", "TiltForward", "TiltLeft" };
        WriteAttributeLeaves(w, obj, headerNames);

        // Locks (recursive)
        foreach (var child in obj.EnumerateNodes())
        {
            if (child.Name == "Lock")
                WriteLock(w, child);
        }
    }

    /// <summary>
    /// Writes non-header attribute leaves from a node, preserving tree order.
    /// Shared between object and lock writing.
    /// </summary>
    private void WriteAttributeLeaves(BinaryDataWriter w, TreeNode node, HashSet<string> skipNames)
    {
        var leaves = node.EnumerateLeaves().ToList();

        foreach (var leaf in leaves)
        {
            if (skipNames.Contains(leaf.Name)) continue;

            // Skip individual values, written with their count sentinel
            if (leaf.Name is "AIData" or "MinishopRIcon" or "MinishopMIcon") continue;

            switch (leaf.Name)
            {
                case "AIMode": w.WriteByte(0x05); w.WriteByte(leaf.ByteValue); break;
                case "OData1":
                    w.WriteByte(0x06);
                    w.WriteSingle(leaf.SingleValue);
                    w.WriteSingle(node.GetChildLeaf("OData2").SingleValue);
                    w.WriteSingle(node.GetChildLeaf("OData3").SingleValue);
                    break;
                case "OData2": case "OData3": break;
                case "TeamID": w.WriteByte(0x07); w.WriteInt32(leaf.Int32Value); break;
                case "TriggerType": w.WriteByte(0x08); w.WriteByte(leaf.ByteValue); break;
                case "Scale": w.WriteByte(0x09); w.WriteSingle(leaf.SingleValue); break;
                case "AIDataCount":
                    w.WriteByte(0x0A);
                    w.WriteByte(leaf.ByteValue);
                    foreach (var ai in leaves.Where(l => l.Name == "AIData"))
                        w.WriteSingle(ai.SingleValue);
                    break;
                case "FlickUsed": w.WriteByte(0x14); w.WriteString32(leaf.StringValue); break;
                case "Directions 0":
                    w.WriteByte(0x18);
                    w.WriteSingle(leaf.SingleValue);
                    w.WriteSingle(node.GetChildLeaf("Directions 1").SingleValue);
                    w.WriteSingle(node.GetChildLeaf("Directions 2").SingleValue);
                    break;
                case "Directions 1": case "Directions 2": break;
                case "SplineScale 0":
                    w.WriteByte(0x1A);
                    w.WriteSingle(leaf.SingleValue);
                    w.WriteSingle(node.GetChildLeaf("SplineScale 1").SingleValue);
                    break;
                case "SplineScale 1": break;
                case "SplineTangent 0":
                    w.WriteByte(0x1B);
                    w.WriteSingle(leaf.SingleValue);
                    w.WriteSingle(node.GetChildLeaf("SplineTangent 1").SingleValue);
                    break;
                case "SplineTangent 1": break;
                case "SplinePath3D": w.WriteByte(0x1C); break;
                case "SplineJet": w.WriteByte(0x1D); break;
                case "MinishopRIconCount":
                    w.WriteByte(0x1E);
                    w.WriteInt32(leaf.Int32Value);
                    foreach (var icon in leaves.Where(l => l.Name == "MinishopRIcon"))
                        w.WriteInt32(icon.Int32Value);
                    break;
                case "MinishopMIconCount":
                    w.WriteByte(0x1F);
                    w.WriteInt32(leaf.Int32Value);
                    foreach (var icon in leaves.Where(l => l.Name == "MinishopMIcon"))
                        w.WriteInt32(icon.Int32Value);
                    break;
                case "SplineStartID": w.WriteByte(0x20); w.WriteByte(leaf.ByteValue); break;
                case "KeyTime": w.WriteByte(0x21); w.WriteInt32(leaf.Int32Value); break;
            }
        }
    }

    private void WriteLock(BinaryDataWriter w, TreeNode lockNode)
    {
        w.WriteByte(0x16);
        w.WriteInt32(lockNode.GetChildLeaf("Type").Int32Value);
        w.WriteByte(lockNode.GetChildLeaf("LockRefSrc").ByteValue);
        w.WriteByte(lockNode.GetChildLeaf("LockRefDst").ByteValue);

        // Write attribute leaves inside the lock using the same logic as objects
        var headerNames = new HashSet<string> { "Type", "LockRefSrc", "LockRefDst" };
        WriteAttributeLeaves(w, lockNode, headerNames);

        // Write nested objects/locks
        foreach (var child in lockNode.EnumerateNodes())
        {
            if (child.Name == "Object")
                WriteObject(w, child);
            else if (child.Name == "Lock")
                WriteLock(w, child);
        }

        w.WriteByte(0x17); // Pop lock
    }
}
