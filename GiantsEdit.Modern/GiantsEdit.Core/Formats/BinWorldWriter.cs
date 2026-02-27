using GiantsEdit.Core.DataModel;

namespace GiantsEdit.Core.Formats;

/// <summary>
/// Writes tree data model back to .bin world map format.
/// Ported from Delphi's bin_w_read.pas SaveBinW function.
/// </summary>
public class BinWorldWriter
{
    private const int HeaderSize = 32;
    private const int Magic = 0x1A0002E5;
    private BinaryDataWriter _w = null!;

    /// <summary>
    /// Saves a tree to .bin format bytes.
    /// </summary>
    public byte[] Save(TreeNode root)
    {
        _w = new BinaryDataWriter(1_000_000);

        // Reserve header space: magic (4 bytes) + 7 pointers (28 bytes) = 32 bytes
        _w.Position = HeaderSize;

        var pointers = new int[7]; // [0..6] = section pointers

        // Scan through root nodes in DTD order
        var fileStart = root.GetChildNode("[FileStart]");

        // Pointer 0: main data block
        pointers[0] = _w.Position;
        _w.WriteInt32(0); // placeholder for block length
        _w.WriteString0(fileStart.GetChildLeaf("Box").StringValue);
        _w.WriteString0(fileStart.GetChildLeaf("GtiName").StringValue);

        WriteIfExists(root, "Tiling", SaveTiling);
        WriteIfExists(root, "<Textures>", SaveTextures);
        WriteIfExists(root, "SeaSpeed", SaveSeaSpeed);
        WriteIfExists(root, "<Teleports>", SaveTeleport);
        WriteIfExists(root, "<StartLocs>", SaveStartLoc);
        WriteIfExists(root, "<Sun>", SaveSun);
        WriteIfExists(root, "<Flicks>", SaveFlicks);
        WriteIfExists(root, "<PreObjects>", SaveObjects);
        WriteIfExists(root, "StartWeather", n =>
        {
            _w.WriteByte(0x59);
            _w.WriteString0(n.GetChildLeaf("Name").StringValue);
        });

        _w.WriteByte(0x44); // ObjEditStart

        WriteIfExists(root, "Fog", SaveFog);
        WriteIfExists(root, "WaterFog", SaveWaterFog);
        WriteIfExists(root, "BumpClampValue", n => { _w.WriteByte(0x75); WriteLeaf(n.GetChildLeaf("Value")); });
        WriteIfExists(root, "NoScenerios", n => { _w.WriteByte(0x60); WriteLeaf(n.GetChildLeaf("Value")); });
        WriteIfExists(root, "LandAngles", n =>
        {
            _w.WriteByte(0x66);
            WriteLeaf(n.GetChildLeaf("SlopeAngle"));
            WriteLeaf(n.GetChildLeaf("WallAngle"));
        });
        WriteIfExists(root, "LandTexFade", n =>
        {
            _w.WriteByte(0x65);
            WriteLeaf(n.GetChildLeaf("Falloff0"));
            WriteLeaf(n.GetChildLeaf("Falloff1"));
            WriteLeaf(n.GetChildLeaf("Falloff2"));
        });
        WriteIfExists(root, "WaterColor", SaveWaterColor);
        WriteIfExists(root, "<Missions>", SaveMissions);
        WriteIfExists(root, "<Scenerios>", SaveScenerios);
        WriteIfExists(root, "<Objects>", SaveObjects);

        _w.WriteByte(0x45); // ObjEditEnd
        _w.WriteByte(0xFF); // End marker

        // Backpatch block length
        int blockLen = _w.Position - (pointers[0] + 4);
        _w.PatchInt32(pointers[0], blockLen);

        // Pointer 1: [textures]
        pointers[1] = _w.Position;
        SaveTextureList(root.GetChildNode("[textures]"));

        // Pointer 4: [fx]
        pointers[4] = _w.Position;
        WriteLeaf(root.GetChildNode("[fx]").GetChildLeaf("EnvironmentType"));

        // Pointer 5: [scenerios]
        pointers[5] = _w.Position;
        SaveScenerioList(root.GetChildNode("[scenerios]"));

        // Pointer 6: [includefiles]
        pointers[6] = _w.Position;
        SaveIncludeFiles(root.GetChildNode("[includefiles]"));

        // Pointer 2: [sfx]
        pointers[2] = _w.Position;
        var sfx = root.GetChildNode("[sfx]");
        WriteLeaf(sfx.GetChildLeaf("NumOrVersion"));
        WriteLeaf(sfx.GetChildLeaf("SfxVersion"));
        WriteLeaf(sfx.GetChildLeaf("Count"));
        WriteLeaf(sfx.GetChildLeaf("EntrySize"));
        WriteLeaf(sfx.GetChildLeaf("DataSize"));

        // Pointer 3: [objdefs]
        pointers[3] = _w.Position;
        var objSec = root.GetChildNode("[objdefs]");
        var objSecLeaves = objSec.EnumerateLeaves().ToList();
        _w.WriteInt32(objSecLeaves.Count);
        foreach (var leaf in objSecLeaves)
            WriteLeaf(leaf);

        // Write header: magic first, then 7 pointers (matches Delphi array[-1..6])
        int savedPos = _w.Position;
        _w.Position = 0;
        _w.WriteInt32(Magic);          // pointers[-1] in Delphi
        for (int i = 0; i < 7; i++)
            _w.WriteInt32(pointers[i]); // pointers[0..6] in Delphi
        _w.Position = savedPos;

        return _w.ToArray();
    }

    private void WriteIfExists(TreeNode root, string name, Action<TreeNode> writer)
    {
        var node = root.FindChildNode(name);
        if (node != null) writer(node);
    }

    private void WriteLeaf(TreeLeaf leaf)
    {
        switch (leaf.PropertyType)
        {
            case PropertyType.Byte: _w.WriteByte(leaf.ByteValue); break;
            case PropertyType.Int32: _w.WriteInt32(leaf.Int32Value); break;
            case PropertyType.Single: _w.WriteInt32(leaf.RawInt32); break; // preserve bit pattern
            default: break;
        }
    }

    private void SaveTiling(TreeNode n)
    {
        _w.WriteByte(0x39);
        WriteLeaf(n.GetChildLeaf("Obsolete0"));
        WriteLeaf(n.GetChildLeaf("Obsolete1"));
        WriteLeaf(n.GetChildLeaf("Obsolete2"));
        WriteLeaf(n.GetChildLeaf("MixNear"));
        WriteLeaf(n.GetChildLeaf("MixFar"));
        WriteLeaf(n.GetChildLeaf("MixNearBlend"));
        WriteLeaf(n.GetChildLeaf("MixFarBlend"));
    }

    private void SaveTextures(TreeNode n)
    {
        foreach (var leaf in n.EnumerateLeaves())
        {
            byte code = leaf.Name switch
            {
                "DomeTex" => 0x1E, "SeaTex" => 0x25, "GlowTex" => 0x26,
                "OutDomeTex" => 0x1D, "DomeEdgeTex" => 0x1F,
                "WFall1Tex" => 0x20, "WFall2Tex" => 0x21, "WFall3Tex" => 0x22,
                "SpaceLineTex" => 0x23, "SpaceTex" => 0x24,
                _ => 0
            };
            if (code != 0)
            {
                _w.WriteByte(code);
                _w.WriteString16(leaf.StringValue);
            }
        }

        foreach (var child in n.EnumerateNodes())
        {
            byte code = child.Name switch
            {
                "GroundTexture" => 0x34, "SlopeTexture" => 0x35, "WallTexture" => 0x36,
                "GroundBumpTexture" => 0x72, "SlopeBumpTexture" => 0x73, "WallBumpTexture" => 0x74,
                _ => 0
            };
            if (code != 0)
            {
                _w.WriteByte(code);
                _w.WriteString32(child.GetChildLeaf("Name").StringValue);
                WriteLeaf(child.GetChildLeaf("Wrap"));
                WriteLeaf(child.GetChildLeaf("OffsetX"));
                WriteLeaf(child.GetChildLeaf("OffsetY"));
            }
        }
    }

    private void SaveSeaSpeed(TreeNode n)
    {
        _w.WriteByte(0x2E);
        WriteLeaf(n.GetChildLeaf("Cycle"));
        WriteLeaf(n.GetChildLeaf("Speed"));
        WriteLeaf(n.GetChildLeaf("Trans"));
    }

    private void SaveTeleport(TreeNode n)
    {
        foreach (var child in n.EnumerateNodes())
        {
            _w.WriteByte(0x27);
            WriteLeaf(child.GetChildLeaf("Index"));
            WriteLeaf(child.GetChildLeaf("X"));
            WriteLeaf(child.GetChildLeaf("Y"));
            WriteLeaf(child.GetChildLeaf("Z"));
            WriteLeaf(child.GetChildLeaf("DirFacing"));
        }
    }

    private void SaveStartLoc(TreeNode n)
    {
        foreach (var child in n.EnumerateNodes())
        {
            _w.WriteByte(0x30);
            WriteLeaf(child.GetChildLeaf("Type"));
            WriteLeaf(child.GetChildLeaf("StartNumber"));
            WriteLeaf(child.GetChildLeaf("X"));
            WriteLeaf(child.GetChildLeaf("Y"));
            WriteLeaf(child.GetChildLeaf("Z"));
            WriteLeaf(child.GetChildLeaf("DirFacing"));
        }
    }

    private void SaveSun(TreeNode n)
    {
        foreach (var child in n.EnumerateNodes())
        {
            if (child.Name == "SunColor")
            {
                _w.WriteByte(0x28);
                WriteLeaf(child.GetChildLeaf("Red"));
                WriteLeaf(child.GetChildLeaf("Green"));
                WriteLeaf(child.GetChildLeaf("Blue"));
            }
            else if (child.Name == "SunFxName")
            {
                _w.WriteByte(0x76);
                _w.WriteString0(child.GetChildLeaf("Name").StringValue);
                WriteLeaf(child.GetChildLeaf("ColorR"));
                WriteLeaf(child.GetChildLeaf("ColorG"));
                WriteLeaf(child.GetChildLeaf("ColorB"));
                WriteLeaf(child.GetChildLeaf("Exponent"));
                WriteLeaf(child.GetChildLeaf("Factor"));
            }
            else if (child.Name is "Sunflare1" or "Sunflare2")
            {
                _w.WriteByte((byte)(child.Name == "Sunflare1" ? 0x77 : 0x78));
                WriteLeaf(child.GetChildLeaf("Type"));
                WriteLeaf(child.GetChildLeaf("Base0"));
                WriteLeaf(child.GetChildLeaf("Exponent0"));
                WriteLeaf(child.GetChildLeaf("Factor0"));
                WriteLeaf(child.GetChildLeaf("Damping0"));
                WriteLeaf(child.GetChildLeaf("Base1"));
                WriteLeaf(child.GetChildLeaf("Exponent1"));
                WriteLeaf(child.GetChildLeaf("Factor1"));
                WriteLeaf(child.GetChildLeaf("Damping1"));
                WriteLeaf(child.GetChildLeaf("OscillationAmplitude"));
                WriteLeaf(child.GetChildLeaf("OscillationFrequency"));
                WriteLeaf(child.GetChildLeaf("SpinFrequency"));
            }
        }
    }

    private void SaveObjects(TreeNode n)
    {
        foreach (var obj in n.EnumerateNodes())
        {
            // Determine: has tilt → opcode 0x46 (Angle + TiltFwd + TiltLeft), else 0x2A (Angle only)
            var tiltFwd = obj.FindChildLeaf("TiltForward");
            if (tiltFwd != null)
            {
                _w.WriteByte(0x46);
                WriteLeaf(obj.GetChildLeaf("Type"));
                WriteLeaf(obj.GetChildLeaf("X"));
                WriteLeaf(obj.GetChildLeaf("Y"));
                WriteLeaf(obj.GetChildLeaf("Z"));
                WriteLeaf(obj.GetChildLeaf("DirFacing"));
                WriteLeaf(tiltFwd);
                WriteLeaf(obj.GetChildLeaf("TiltLeft"));
            }
            else
            {
                _w.WriteByte(0x2A);
                WriteLeaf(obj.GetChildLeaf("Type"));
                WriteLeaf(obj.GetChildLeaf("X"));
                WriteLeaf(obj.GetChildLeaf("Y"));
                WriteLeaf(obj.GetChildLeaf("Z"));
                WriteLeaf(obj.GetChildLeaf("DirFacing"));
            }

            var scale = obj.FindChildLeaf("Scale");
            if (scale != null) { _w.WriteByte(0x17); WriteLeaf(scale); }

            var aiMode = obj.FindChildLeaf("AIMode");
            if (aiMode != null) { _w.WriteByte(0x3B); WriteLeaf(aiMode); }

            var teamId = obj.FindChildLeaf("TeamID");
            if (teamId != null) { _w.WriteByte(0x52); WriteLeaf(teamId); }

            var splineKey = obj.FindChildLeaf("KeyTime");
            if (splineKey != null) { _w.WriteByte(0x6B); WriteLeaf(splineKey); }

            var odata1 = obj.FindChildLeaf("OData1");
            if (odata1 != null)
            {
                _w.WriteByte(0x5A);
                WriteLeaf(odata1);
                WriteLeaf(obj.GetChildLeaf("OData2"));
                WriteLeaf(obj.GetChildLeaf("OData3"));
            }

            var lightR = obj.FindChildLeaf("LightColorR");
            if (lightR != null)
            {
                _w.WriteByte(0x5F);
                WriteLeaf(lightR);
                WriteLeaf(obj.GetChildLeaf("LightColorG"));
                WriteLeaf(obj.GetChildLeaf("LightColorB"));
            }

            // Locks (recursive)
            foreach (var lockNode in obj.EnumerateNodes().Where(c => c.Name == "Lock"))
            {
                _w.WriteByte(0x4C);
                WriteLeaf(lockNode.GetChildLeaf("Type"));
                WriteLeaf(lockNode.GetChildLeaf("LockRefSrc"));
                WriteLeaf(lockNode.GetChildLeaf("LockRefDst"));
                SaveObjects(lockNode);
                _w.WriteByte(0x4D);
            }
        }
    }

    private void SaveFog(TreeNode n)
    {
        _w.WriteByte(0x29);
        WriteLeaf(n.GetChildLeaf("FogMin"));
        WriteLeaf(n.GetChildLeaf("FogMax"));
        WriteLeaf(n.GetChildLeaf("Red"));
        WriteLeaf(n.GetChildLeaf("Green"));
        WriteLeaf(n.GetChildLeaf("Blue"));
    }

    private void SaveWaterFog(TreeNode n)
    {
        _w.WriteByte(0x58);
        WriteLeaf(n.GetChildLeaf("FogMin"));
        WriteLeaf(n.GetChildLeaf("FogMax"));
        WriteLeaf(n.GetChildLeaf("Red"));
        WriteLeaf(n.GetChildLeaf("Green"));
        WriteLeaf(n.GetChildLeaf("Blue"));
    }

    private void SaveWaterColor(TreeNode n)
    {
        _w.WriteByte(0x79);
        WriteLeaf(n.GetChildLeaf("ColorR"));
        WriteLeaf(n.GetChildLeaf("ColorG"));
        WriteLeaf(n.GetChildLeaf("ColorB"));
        for (int i = 0; i < 5; i++)
        {
            WriteLeaf(n.GetChildLeaf($"Color{i}R"));
            WriteLeaf(n.GetChildLeaf($"Color{i}G"));
            WriteLeaf(n.GetChildLeaf($"Color{i}B"));
        }
        WriteLeaf(n.GetChildLeaf("ReflectionFalloff"));
        WriteLeaf(n.GetChildLeaf("ReflectionWidth"));
        WriteLeaf(n.GetChildLeaf("ReflectionScale"));
        WriteLeaf(n.GetChildLeaf("ReflectionAmplitude"));
        WriteLeaf(n.GetChildLeaf("WaveSpeed"));
    }

    private void SaveFlicks(TreeNode n)
    {
        foreach (var leaf in n.EnumerateLeaves())
        {
            _w.WriteByte(0x4F);
            _w.WriteString64(leaf.StringValue);
        }
    }

    private void SaveMissions(TreeNode n)
    {
        foreach (var leaf in n.EnumerateLeaves())
        {
            _w.WriteByte(0x5D);
            _w.WriteString32(leaf.StringValue);
        }
    }

    private void SaveScenerios(TreeNode n)
    {
        foreach (var child in n.EnumerateNodes())
        {
            _w.WriteByte(0x5C);
            WriteLeaf(child.GetChildLeaf("Type"));
            WriteLeaf(child.GetChildLeaf("TriggersNeeded"));
            _w.WriteString32(child.GetChildLeaf("Name").StringValue);
        }
    }

    private void SaveTextureList(TreeNode n)
    {
        var children = n.EnumerateNodes().ToList();
        _w.WriteInt32(children.Count);
        foreach (var tex in children)
        {
            WriteLeaf(tex.GetChildLeaf("Unknown"));
            WriteLeaf(tex.GetChildLeaf("IsSkyDome"));
            _w.WriteLString0(tex.GetChildLeaf("Name").StringValue);
        }
    }

    private void SaveScenerioList(TreeNode n)
    {
        var leaves = n.EnumerateLeaves().ToList();
        // The scenerio list stores sub-nodes with Index+Name
        var children = n.EnumerateNodes().ToList();
        _w.WriteInt32(children.Count);
        foreach (var sc in children)
        {
            WriteLeaf(sc.GetChildLeaf("Index"));
            _w.WriteString32(sc.GetChildLeaf("Name").StringValue);
        }
    }

    private void SaveIncludeFiles(TreeNode n)
    {
        var leaves = n.EnumerateLeaves().ToList();
        _w.WriteInt32(leaves.Count);
        foreach (var leaf in leaves)
            _w.WriteString32(leaf.StringValue);
    }
}
