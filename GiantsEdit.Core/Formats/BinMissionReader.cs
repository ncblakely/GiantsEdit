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

        if (magic != BinFormatConstants.MsnMagic || version != BinFormatConstants.MsnVersion)
            return null;

        var root = new TreeNode("Mission data");
        var objects = root.AddNode(BinFormatConstants.GroupObjects);

        _currentObject = objects;

        while (_r.Position < data.Length)
        {
            byte chunkId = _r.ReadByte();
            if (chunkId == BinFormatConstants.MsnTerminator)
                break;
            ReadMissionChunk(chunkId, root, objects);
        }

        return root;
    }

    private void ReadMissionChunk(byte chunkId, TreeNode root, TreeNode objects)
    {
        switch (chunkId)
        {
            case BinFormatConstants.MsnObject1Angle: // 1-angled object
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
            case BinFormatConstants.MsnObject3Angle: // 3-angled object
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
            case BinFormatConstants.MsnAIMode:
                _currentObject.AddByte("AIMode", _r.ReadByte());
                break;
            case BinFormatConstants.MsnOData:
                _currentObject.AddSingle("OData1", _r.ReadSingle());
                _currentObject.AddSingle("OData2", _r.ReadSingle());
                _currentObject.AddSingle("OData3", _r.ReadSingle());
                break;
            case BinFormatConstants.MsnTeamID:
                _currentObject.AddInt32("TeamID", _r.ReadInt32());
                break;
            case BinFormatConstants.MsnTriggerType:
                _currentObject.AddByte("TriggerType", _r.ReadByte());
                break;
            case BinFormatConstants.MsnScale:
                _currentObject.AddSingle("Scale", _r.ReadSingle());
                break;
            case BinFormatConstants.MsnAIData:
                {
                    int count = _r.ReadByte();
                    _currentObject.AddByte("AIDataCount", (byte)count);
                    for (int i = 0; i < count; i++)
                        _currentObject.AddSingle("AIData", _r.ReadSingle());
                    break;
                }
            case BinFormatConstants.MsnCharacter:
                {
                    var opts = root.GetOrAddNode(BinFormatConstants.GroupOptions);
                    opts.AddInt32("Character", _r.ReadInt32());
                    opts.AddInt32("TeamMembers", _r.ReadInt32());
                    opts.AddInt32("MarkerID", _r.ReadInt32());
                    opts.AddInt32("KabutoSize", _r.ReadInt32());
                    break;
                }
            case BinFormatConstants.MsnSmartieType:
                root.GetOrAddNode(BinFormatConstants.GroupOptions).AddInt32("SmartieType", _r.ReadInt32());
                break;
            case BinFormatConstants.MsnSpecialText:
                root.GetOrAddNode(BinFormatConstants.GroupOptions).AddInt32("SpecialText", _r.ReadInt32());
                break;
            case BinFormatConstants.MsnIcons:
                {
                    var opts = root.GetOrAddNode(BinFormatConstants.GroupOptions);
                    int count = _r.ReadInt32();
                    opts.AddInt32("IconCount", count);
                    for (int i = 0; i < count; i++)
                        opts.AddInt32("Icons", _r.ReadInt32());
                    break;
                }
            case BinFormatConstants.MsnVimpMeat:
                root.GetOrAddNode(BinFormatConstants.GroupOptions).AddInt32("VimpMeat", _r.ReadInt32());
                break;
            case BinFormatConstants.MsnNoNitro:
                root.GetOrAddNode(BinFormatConstants.GroupOptions).AddInt32("NoNitro", _r.ReadInt32());
                break;
            case BinFormatConstants.MsnNoJetpack:
                root.GetOrAddNode(BinFormatConstants.GroupOptions).AddInt32("NoJetpack", _r.ReadInt32());
                break;
            case BinFormatConstants.MsnFailTime:
                root.GetOrAddNode(BinFormatConstants.GroupOptions).AddInt32("FailTime", _r.ReadInt32());
                break;
            case BinFormatConstants.MsnFailFlick:
                root.GetOrAddNode(BinFormatConstants.GroupOptions).AddString("FailFlick", _r.ReadString32());
                break;
            case BinFormatConstants.MsnFlickUsed:
                _currentObject.AddString("FlickUsed", _r.ReadString32());
                break;
            case BinFormatConstants.MsnStartFlick:
                root.GetOrAddNode(BinFormatConstants.GroupOptions).AddString("StartFlick", _r.ReadString32());
                break;
            case BinFormatConstants.MsnLockStart:
                {
                    var lockNode = _currentObject.AddNode("Lock");
                    _currentObject = lockNode;
                    lockNode.AddInt32("Type", _r.ReadInt32());
                    lockNode.AddByte("LockRefSrc", _r.ReadByte());
                    lockNode.AddByte("LockRefDst", _r.ReadByte());
                    break;
                }
            case BinFormatConstants.MsnLockEnd:
                if (_currentObject.Parent != null)
                    _currentObject = _currentObject.Parent;
                break;
            case BinFormatConstants.MsnDirections:
                _currentObject.AddSingle("Directions 0", _r.ReadSingle());
                _currentObject.AddSingle("Directions 1", _r.ReadSingle());
                _currentObject.AddSingle("Directions 2", _r.ReadSingle());
                break;
            case BinFormatConstants.MsnEndFlick:
                root.GetOrAddNode(BinFormatConstants.GroupOptions).AddString("EndFlick", _r.ReadString32());
                break;
            case BinFormatConstants.MsnSplineScale:
                _currentObject.AddSingle("SplineScale 0", _r.ReadSingle());
                _currentObject.AddSingle("SplineScale 1", _r.ReadSingle());
                break;
            case BinFormatConstants.MsnSplineTangent:
                _currentObject.AddSingle("SplineTangent 0", _r.ReadSingle());
                _currentObject.AddSingle("SplineTangent 1", _r.ReadSingle());
                break;
            case BinFormatConstants.MsnSplinePath3D:
                _currentObject.AddVoid("SplinePath3D");
                break;
            case BinFormatConstants.MsnSplineJet:
                _currentObject.AddVoid("SplineJet");
                break;
            case BinFormatConstants.MsnMinishopRIcons:
                {
                    int count = _r.ReadInt32();
                    _currentObject.AddInt32("MinishopRIconCount", count);
                    for (int i = 0; i < count; i++)
                        _currentObject.AddInt32("MinishopRIcon", _r.ReadInt32());
                    break;
                }
            case BinFormatConstants.MsnMinishopMIcons:
                {
                    int count = _r.ReadInt32();
                    _currentObject.AddInt32("MinishopMIconCount", count);
                    for (int i = 0; i < count; i++)
                        _currentObject.AddInt32("MinishopMIcon", _r.ReadInt32());
                    break;
                }
            case BinFormatConstants.MsnSplineStartID:
                _currentObject.AddByte("SplineStartID", _r.ReadByte());
                break;
            case BinFormatConstants.MsnKeyTime:
                _currentObject.AddInt32("KeyTime", _r.ReadInt32());
                break;
            case BinFormatConstants.MsnJetskiRace:
                root.GetOrAddNode(BinFormatConstants.GroupOptions).AddVoid("JetskiRace");
                break;
            case BinFormatConstants.MsnBumpClampValue:
                root.GetOrAddNode(BinFormatConstants.GroupOptions).AddSingle("BumpClampValue", _r.ReadSingle());
                break;
            case BinFormatConstants.MsnZeroVimpMeat:
                root.GetOrAddNode(BinFormatConstants.GroupOptions).AddVoid("ZeroVimpMeat");
                break;
            case BinFormatConstants.MsnGrabSmartie:
                root.GetOrAddNode(BinFormatConstants.GroupOptions).AddVoid("GrabSmartie");
                break;
            case BinFormatConstants.MsnSuccessDelay:
                {
                    var opts = root.GetOrAddNode(BinFormatConstants.GroupOptions);
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
    public static byte[] Save(TreeNode root)
    {
        var w = new BinaryDataWriter();

        // Header
        w.WriteInt32(BinFormatConstants.MsnMagic);
        w.WriteByte(BinFormatConstants.MsnVersion);

        // Write objects
        var objects = root.FindChildNode(BinFormatConstants.GroupObjects);
        if (objects != null)
        {
            foreach (var obj in objects.EnumerateNodes())
                WriteObject(w, obj);
        }

        // Write options after objects
        var options = root.FindChildNode(BinFormatConstants.GroupOptions);
        if (options != null)
            WriteOptions(w, options);

        // Terminator
        w.WriteByte(BinFormatConstants.MsnTerminator);

        return w.ToArray();
    }

    private static void WriteOptions(BinaryDataWriter w, TreeNode options)
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
                    w.WriteByte(BinFormatConstants.MsnCharacter);
                    w.WriteInt32(leaf.Int32Value);
                    w.WriteInt32(options.GetChildLeaf("TeamMembers").Int32Value);
                    w.WriteInt32(options.GetChildLeaf("MarkerID").Int32Value);
                    w.WriteInt32(options.GetChildLeaf("KabutoSize").Int32Value);
                    break;
                case "SmartieType":
                    w.WriteByte(BinFormatConstants.MsnSmartieType);
                    w.WriteInt32(leaf.Int32Value);
                    break;
                case "SpecialText":
                    w.WriteByte(BinFormatConstants.MsnSpecialText);
                    w.WriteInt32(leaf.Int32Value);
                    break;
                case "IconCount":
                    {
                        w.WriteByte(BinFormatConstants.MsnIcons);
                        w.WriteInt32(leaf.Int32Value);
                        foreach (var icon in leaves.Where(l => l.Name == "Icons"))
                            w.WriteInt32(icon.Int32Value);
                    }
                    break;
                case "Icons":
                    break; // written with IconCount
                case "VimpMeat":
                    w.WriteByte(BinFormatConstants.MsnVimpMeat);
                    w.WriteInt32(leaf.Int32Value);
                    break;
                case "NoNitro":
                    w.WriteByte(BinFormatConstants.MsnNoNitro);
                    w.WriteInt32(leaf.Int32Value);
                    break;
                case "NoJetpack":
                    w.WriteByte(BinFormatConstants.MsnNoJetpack);
                    w.WriteInt32(leaf.Int32Value);
                    break;
                case "FailTime":
                    w.WriteByte(BinFormatConstants.MsnFailTime);
                    w.WriteInt32(leaf.Int32Value);
                    break;
                case "FailFlick":
                    w.WriteByte(BinFormatConstants.MsnFailFlick);
                    w.WriteString32(leaf.StringValue);
                    break;
                case "StartFlick":
                    w.WriteByte(BinFormatConstants.MsnStartFlick);
                    w.WriteString32(leaf.StringValue);
                    break;
                case "EndFlick":
                    w.WriteByte(BinFormatConstants.MsnEndFlick);
                    w.WriteString32(leaf.StringValue);
                    break;
                case "JetskiRace":
                    w.WriteByte(BinFormatConstants.MsnJetskiRace);
                    break;
                case "BumpClampValue":
                    w.WriteByte(BinFormatConstants.MsnBumpClampValue);
                    w.WriteSingle(leaf.SingleValue);
                    break;
                case "ZeroVimpMeat":
                    w.WriteByte(BinFormatConstants.MsnZeroVimpMeat);
                    break;
                case "GrabSmartie":
                    w.WriteByte(BinFormatConstants.MsnGrabSmartie);
                    break;
                case "SuccessDelay 0":
                    w.WriteByte(BinFormatConstants.MsnSuccessDelay);
                    w.WriteSingle(leaf.SingleValue);
                    w.WriteSingle(options.GetChildLeaf("SuccessDelay 1").SingleValue);
                    break;
            }
        }
    }

    private static void WriteObject(BinaryDataWriter w, TreeNode obj)
    {
        bool hasTilt = obj.FindChildLeaf("TiltForward") != null;

        w.WriteByte(hasTilt ? BinFormatConstants.MsnObject3Angle : BinFormatConstants.MsnObject1Angle);
        w.WriteInt32(obj.GetChildLeaf("Type").Int32Value);
        w.WriteSingle(obj.GetChildLeaf("X").SingleValue);
        w.WriteSingle(obj.GetChildLeaf("Y").SingleValue);
        w.WriteSingle(obj.GetChildLeaf("Z").SingleValue);
        w.WriteSingle(obj.GetChildLeaf("DirFacing").SingleValue);

        if (hasTilt)
        {
            w.WriteSingle(obj.GetChildLeaf("TiltForward").SingleValue);
            w.WriteSingle(obj.GetChildLeaf("TiltLeft").SingleValue);
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
    private static void WriteAttributeLeaves(BinaryDataWriter w, TreeNode node, HashSet<string> skipNames)
    {
        var leaves = node.EnumerateLeaves().ToList();

        foreach (var leaf in leaves)
        {
            if (skipNames.Contains(leaf.Name)) continue;

            // Skip individual values, written with their count sentinel
            if (leaf.Name is "AIData" or "MinishopRIcon" or "MinishopMIcon") continue;

            switch (leaf.Name)
            {
                case "AIMode": w.WriteByte(BinFormatConstants.MsnAIMode); w.WriteByte(leaf.ByteValue); break;
                case "OData1":
                    w.WriteByte(BinFormatConstants.MsnOData);
                    w.WriteSingle(leaf.SingleValue);
                    w.WriteSingle(node.GetChildLeaf("OData2").SingleValue);
                    w.WriteSingle(node.GetChildLeaf("OData3").SingleValue);
                    break;
                case "OData2": case "OData3": break;
                case "TeamID": w.WriteByte(BinFormatConstants.MsnTeamID); w.WriteInt32(leaf.Int32Value); break;
                case "TriggerType": w.WriteByte(BinFormatConstants.MsnTriggerType); w.WriteByte(leaf.ByteValue); break;
                case "Scale": w.WriteByte(BinFormatConstants.MsnScale); w.WriteSingle(leaf.SingleValue); break;
                case "AIDataCount":
                    w.WriteByte(BinFormatConstants.MsnAIData);
                    w.WriteByte(leaf.ByteValue);
                    foreach (var ai in leaves.Where(l => l.Name == "AIData"))
                        w.WriteSingle(ai.SingleValue);
                    break;
                case "FlickUsed": w.WriteByte(BinFormatConstants.MsnFlickUsed); w.WriteString32(leaf.StringValue); break;
                case "Directions 0":
                    w.WriteByte(BinFormatConstants.MsnDirections);
                    w.WriteSingle(leaf.SingleValue);
                    w.WriteSingle(node.GetChildLeaf("Directions 1").SingleValue);
                    w.WriteSingle(node.GetChildLeaf("Directions 2").SingleValue);
                    break;
                case "Directions 1": case "Directions 2": break;
                case "SplineScale 0":
                    w.WriteByte(BinFormatConstants.MsnSplineScale);
                    w.WriteSingle(leaf.SingleValue);
                    w.WriteSingle(node.GetChildLeaf("SplineScale 1").SingleValue);
                    break;
                case "SplineScale 1": break;
                case "SplineTangent 0":
                    w.WriteByte(BinFormatConstants.MsnSplineTangent);
                    w.WriteSingle(leaf.SingleValue);
                    w.WriteSingle(node.GetChildLeaf("SplineTangent 1").SingleValue);
                    break;
                case "SplineTangent 1": break;
                case "SplinePath3D": w.WriteByte(BinFormatConstants.MsnSplinePath3D); break;
                case "SplineJet": w.WriteByte(BinFormatConstants.MsnSplineJet); break;
                case "MinishopRIconCount":
                    w.WriteByte(BinFormatConstants.MsnMinishopRIcons);
                    w.WriteInt32(leaf.Int32Value);
                    foreach (var icon in leaves.Where(l => l.Name == "MinishopRIcon"))
                        w.WriteInt32(icon.Int32Value);
                    break;
                case "MinishopMIconCount":
                    w.WriteByte(BinFormatConstants.MsnMinishopMIcons);
                    w.WriteInt32(leaf.Int32Value);
                    foreach (var icon in leaves.Where(l => l.Name == "MinishopMIcon"))
                        w.WriteInt32(icon.Int32Value);
                    break;
                case "SplineStartID": w.WriteByte(BinFormatConstants.MsnSplineStartID); w.WriteByte(leaf.ByteValue); break;
                case "KeyTime": w.WriteByte(BinFormatConstants.MsnKeyTime); w.WriteInt32(leaf.Int32Value); break;
            }
        }
    }

    private static void WriteLock(BinaryDataWriter w, TreeNode lockNode)
    {
        w.WriteByte(BinFormatConstants.MsnLockStart);
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

        w.WriteByte(BinFormatConstants.MsnLockEnd); // Pop lock
    }
}
