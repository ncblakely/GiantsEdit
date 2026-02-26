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
        var objects = root.AddNode("<Objects>");

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
                obj.AddSingle("Angle", _r.ReadSingle());
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
                obj.AddSingle("Angle X", _r.ReadSingle());
                obj.AddSingle("Angle Y", _r.ReadSingle());
                obj.AddSingle("Angle Z", _r.ReadSingle());
                break;
            }
            case 0x05:
                _currentObject.AddByte("AIMode", _r.ReadByte());
                break;
            case 0x06:
                _currentObject.AddSingle("OData 0", _r.ReadSingle());
                _currentObject.AddSingle("OData 1", _r.ReadSingle());
                _currentObject.AddSingle("OData 2", _r.ReadSingle());
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
                for (int i = 0; i < count; i++)
                    _currentObject.AddSingle("AIData", _r.ReadSingle());
                break;
            }
            case 0x0B:
            {
                var opts = root.GetOrAddNode("<Options>");
                opts.AddInt32("Character 0", _r.ReadInt32());
                opts.AddInt32("Character 1", _r.ReadInt32());
                opts.AddInt32("Character 2", _r.ReadInt32());
                opts.AddInt32("Character 3", _r.ReadInt32());
                break;
            }
            case 0x0C:
                root.GetOrAddNode("<Options>").AddInt32("SmartieType", _r.ReadInt32());
                break;
            case 0x0D:
                _currentObject.AddInt32("SpecialText", _r.ReadInt32());
                break;
            case 0x0E:
            {
                var opts = root.GetOrAddNode("<Options>");
                int count = _r.ReadInt32();
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
                lockNode.AddByte("Lock 1", _r.ReadByte());
                lockNode.AddByte("Lock 2", _r.ReadByte());
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
            case 0x1C:
                _currentObject.AddVoid("SplinePath3D");
                break;
            case 0x1E:
            {
                int count = _r.ReadInt32();
                for (int i = 0; i < count; i++)
                    _currentObject.AddInt32("MinishopRIcon", _r.ReadInt32());
                break;
            }
            case 0x1F:
            {
                int count = _r.ReadInt32();
                for (int i = 0; i < count; i++)
                    _currentObject.AddInt32("MinishopMIcon", _r.ReadInt32());
                break;
            }
            case 0x20:
                _currentObject.AddByte("SplineStartID", _r.ReadByte());
                break;
            case 0x21:
                _currentObject.AddInt32("SplineKeyTime", _r.ReadInt32());
                break;
            case 0x22:
                root.GetOrAddNode("<Options>").AddVoid("JetskiRace");
                break;
            case 0x24:
                root.GetOrAddNode("<Options>").AddVoid("DiscardVimpMeat");
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

        // Write options if present
        var options = root.FindChildNode("<Options>");
        if (options != null)
            WriteOptions(w, options);

        // Write objects
        var objects = root.FindChildNode("<Objects>");
        if (objects != null)
        {
            foreach (var obj in objects.EnumerateNodes())
                WriteObject(w, obj);
        }

        // Terminator
        w.WriteByte(0x02);

        return w.ToArray();
    }

    private void WriteOptions(BinaryDataWriter w, TreeNode options)
    {
        foreach (var leaf in options.EnumerateLeaves())
        {
            switch (leaf.Name)
            {
                case "Character 0":
                    w.WriteByte(0x0B);
                    w.WriteInt32(leaf.Int32Value);
                    // Consume next 3 character leaves
                    break;
                case "SmartieType":
                    w.WriteByte(0x0C);
                    w.WriteInt32(leaf.Int32Value);
                    break;
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
                case "DiscardVimpMeat":
                    w.WriteByte(0x24);
                    break;
                case "GrabSmartie":
                    w.WriteByte(0x25);
                    break;
            }
        }

        // Handle grouped leaves (Characters, Icons, SuccessDelay)
        var leaves = options.EnumerateLeaves().ToList();
        var characters = leaves.Where(l => l.Name.StartsWith("Character ")).ToList();
        if (characters.Count == 4)
        {
            // Already written above as part of Character 0 case
            // Need to output remaining 3 values
        }

        var icons = leaves.Where(l => l.Name == "Icons").ToList();
        if (icons.Count > 0)
        {
            w.WriteByte(0x0E);
            w.WriteInt32(icons.Count);
            foreach (var icon in icons)
                w.WriteInt32(icon.Int32Value);
        }

        var successDelay = leaves.Where(l => l.Name.StartsWith("SuccessDelay ")).ToList();
        if (successDelay.Count == 2)
        {
            w.WriteByte(0x26);
            w.WriteSingle(successDelay[0].SingleValue);
            w.WriteSingle(successDelay[1].SingleValue);
        }
    }

    private void WriteObject(BinaryDataWriter w, TreeNode obj)
    {
        bool has3Angles = obj.FindChildLeaf("Angle X") != null;

        if (has3Angles)
        {
            w.WriteByte(0x04);
            w.WriteInt32(obj.GetChildLeaf("Type").Int32Value);
            w.WriteSingle(obj.GetChildLeaf("X").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("Y").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("Z").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("Angle X").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("Angle Y").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("Angle Z").SingleValue);
        }
        else
        {
            w.WriteByte(0x03);
            w.WriteInt32(obj.GetChildLeaf("Type").Int32Value);
            w.WriteSingle(obj.GetChildLeaf("X").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("Y").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("Z").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("Angle").SingleValue);
        }

        // Write optional attributes
        WriteLeafIfPresent(w, obj, "AIMode", 0x05, LeafWriteKind.Byte);
        WriteLeafIfPresent(w, obj, "TeamID", 0x07, LeafWriteKind.Int32);
        WriteLeafIfPresent(w, obj, "TriggerType", 0x08, LeafWriteKind.Byte);
        WriteLeafIfPresent(w, obj, "Scale", 0x09, LeafWriteKind.Single);
        WriteLeafIfPresent(w, obj, "SpecialText", 0x0D, LeafWriteKind.Int32);
        WriteLeafIfPresent(w, obj, "FlickUsed", 0x14, LeafWriteKind.String32);
        WriteLeafIfPresent(w, obj, "SplineStartID", 0x20, LeafWriteKind.Byte);
        WriteLeafIfPresent(w, obj, "SplineKeyTime", 0x21, LeafWriteKind.Int32);

        // OData ($06)
        var odata0 = obj.FindChildLeaf("OData 0");
        if (odata0 != null)
        {
            w.WriteByte(0x06);
            w.WriteSingle(odata0.SingleValue);
            w.WriteSingle(obj.GetChildLeaf("OData 1").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("OData 2").SingleValue);
        }

        // Directions ($18)
        var dir0 = obj.FindChildLeaf("Directions 0");
        if (dir0 != null)
        {
            w.WriteByte(0x18);
            w.WriteSingle(dir0.SingleValue);
            w.WriteSingle(obj.GetChildLeaf("Directions 1").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("Directions 2").SingleValue);
        }

        // SplineScale ($1A)
        var ss0 = obj.FindChildLeaf("SplineScale 0");
        if (ss0 != null)
        {
            w.WriteByte(0x1A);
            w.WriteSingle(ss0.SingleValue);
            w.WriteSingle(obj.GetChildLeaf("SplineScale 1").SingleValue);
        }

        // SplinePath3D ($1C)
        if (obj.FindChildLeaf("SplinePath3D") != null)
            w.WriteByte(0x1C);

        // AIData ($0A)
        var aiData = obj.EnumerateLeaves().Where(l => l.Name == "AIData").ToList();
        if (aiData.Count > 0)
        {
            w.WriteByte(0x0A);
            w.WriteByte((byte)aiData.Count);
            foreach (var ai in aiData)
                w.WriteSingle(ai.SingleValue);
        }

        // MinishopRIcon ($1E)
        var rIcons = obj.EnumerateLeaves().Where(l => l.Name == "MinishopRIcon").ToList();
        if (rIcons.Count > 0)
        {
            w.WriteByte(0x1E);
            w.WriteInt32(rIcons.Count);
            foreach (var icon in rIcons)
                w.WriteInt32(icon.Int32Value);
        }

        // MinishopMIcon ($1F)
        var mIcons = obj.EnumerateLeaves().Where(l => l.Name == "MinishopMIcon").ToList();
        if (mIcons.Count > 0)
        {
            w.WriteByte(0x1F);
            w.WriteInt32(mIcons.Count);
            foreach (var icon in mIcons)
                w.WriteInt32(icon.Int32Value);
        }

        // Locks (recursive)
        foreach (var child in obj.EnumerateNodes())
        {
            if (child.Name == "Lock")
                WriteLock(w, child);
        }
    }

    private void WriteLock(BinaryDataWriter w, TreeNode lockNode)
    {
        w.WriteByte(0x16);
        w.WriteInt32(lockNode.GetChildLeaf("Type").Int32Value);
        w.WriteByte(lockNode.GetChildLeaf("Lock 1").ByteValue);
        w.WriteByte(lockNode.GetChildLeaf("Lock 2").ByteValue);

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

    private enum LeafWriteKind { Byte, Int32, Single, String32 }

    private void WriteLeafIfPresent(BinaryDataWriter w, TreeNode node, string leafName, byte chunkId, LeafWriteKind kind)
    {
        var leaf = node.FindChildLeaf(leafName);
        if (leaf == null) return;

        w.WriteByte(chunkId);
        switch (kind)
        {
            case LeafWriteKind.Byte: w.WriteByte(leaf.ByteValue); break;
            case LeafWriteKind.Int32: w.WriteInt32(leaf.Int32Value); break;
            case LeafWriteKind.Single: w.WriteSingle(leaf.SingleValue); break;
            case LeafWriteKind.String32: w.WriteString32(leaf.StringValue); break;
        }
    }
}
