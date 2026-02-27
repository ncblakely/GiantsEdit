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

        // Write all opcodes by iterating root children in insertion order.
        // This preserves the original opcode stream ordering and handles duplicates.
        foreach (var child in root.EnumerateNodes())
        {
            switch (child.Name)
            {
                // Section-based nodes (handled separately below)
                case "[FileStart]":
                case "[textures]":
                case "[sfx]":
                case "[objdefs]":
                case "[fx]":
                case "[scenerios]":
                case "[includefiles]":
                    break;

                case "Tiling": SaveTiling(child); break;
                case "<Textures>": SaveTextures(child); break;
                case "SeaSpeed": SaveSeaSpeed(child); break;
                case "<Teleports>": SaveTeleport(child); break;
                case "<StartLocs>": SaveStartLoc(child); break;
                case "<Sun>": SaveSun(child); break;
                case "<Flicks>": SaveFlicks(child); break;
                case "<PreObjects>": SaveAllObjects(child); break;

                case "StartWeather":
                    _w.WriteByte(0x59);
                    _w.WriteString0(child.GetChildLeaf("Name").StringValue);
                    break;

                case "ObjEditStart": _w.WriteByte(0x44); break;
                case "ObjEditEnd": _w.WriteByte(0x45); break;

                case "Fog": SaveFog(child); break;
                case "WaterFog": SaveWaterFog(child); break;

                case "BumpClampValue":
                    _w.WriteByte(0x75);
                    WriteLeaf(child.GetChildLeaf("Value"));
                    break;
                case "NoScenerios":
                    _w.WriteByte(0x60);
                    WriteLeaf(child.GetChildLeaf("Value"));
                    break;
                case "LandAngles":
                    _w.WriteByte(0x66);
                    WriteLeaf(child.GetChildLeaf("SlopeAngle"));
                    WriteLeaf(child.GetChildLeaf("WallAngle"));
                    break;
                case "LandTexFade":
                    _w.WriteByte(0x65);
                    WriteLeaf(child.GetChildLeaf("Falloff0"));
                    WriteLeaf(child.GetChildLeaf("Falloff1"));
                    WriteLeaf(child.GetChildLeaf("Falloff2"));
                    break;
                case "WaterColor": SaveWaterColor(child); break;
                case "<Missions>": SaveMissions(child); break;
                case "<Scenerios>": SaveScenerios(child); break;
                case "<Objects>": SaveAllObjects(child); break;

                case "Scenario":
                    _w.WriteByte(0x49);
                    WriteLeaf(child.GetChildLeaf("Value"));
                    break;
                case "Music":
                    _w.WriteByte(0x2C);
                    _w.WriteString32(child.GetChildLeaf("Name").StringValue);
                    break;
                case "Ambient":
                    _w.WriteByte(0x5E);
                    _w.WriteString32(child.GetChildLeaf("Name").StringValue);
                    break;
                case "MultiAmbient":
                    _w.WriteByte(0x81);
                    WriteLeaf(child.GetChildLeaf("Value"));
                    break;
                case "ArmyBin":
                    _w.WriteByte(0x82);
                    _w.WriteString32(child.GetChildLeaf("Name").StringValue);
                    break;
                case "VoPath":
                    _w.WriteByte(0x84);
                    _w.WriteString0(child.GetChildLeaf("Name").StringValue);
                    break;
                case "AmbientColor":
                    _w.WriteByte(0x8B);
                    WriteLeaf(child.GetChildLeaf("R"));
                    WriteLeaf(child.GetChildLeaf("G"));
                    WriteLeaf(child.GetChildLeaf("B"));
                    break;
                case "BlendWater":
                    _w.WriteByte(0x8F);
                    WriteLeaf(child.GetChildLeaf("FogScale"));
                    WriteLeaf(child.GetChildLeaf("RenderFog"));
                    break;
                case "WaterMaterial": SaveWaterMaterial(child); break;
                case "WorldGrid": SaveWorldGrid(child); break;
                case "WorldNoLighting": _w.WriteByte(0x4A); break;

                case "MusicSuspense": SaveGenericMusic(child, 0x6C); break;
                case "MusicLight": SaveGenericMusic(child, 0x6D); break;
                case "MusicWin": SaveGenericMusic(child, 0x6E); break;
                case "MusicHeavy": SaveGenericMusic(child, 0x6F); break;
                case "MusicFailure": SaveSingleMusic(child, 0x70); break;
                case "MusicSuccess": SaveSingleMusic(child, 0x71); break;
            }
        }

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
        // Write node textures (44-byte entries) first, then leaf textures (16-byte entries)
        foreach (var child in n.EnumerateNodes())
        {
            byte code = child.Name switch
            {
                "GroundTexture" => 0x34, "SlopeTexture" => 0x35, "WallTexture" => 0x36,
                "GroundBumpTexture" => 0x72, "SlopeBumpTexture" => 0x73, "WallBumpTexture" => 0x74,
                "GroundDetailTexture" => 0x85, "SlopeDetailTexture" => 0x86, "WallDetailTexture" => 0x87,
                "GroundNormalTexture" => 0x8C, "SlopeNormalTexture" => 0x8D, "WallNormalTexture" => 0x8E,
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

    private void SaveAllObjects(TreeNode n)
    {
        foreach (var obj in n.EnumerateNodes())
        {
            switch (obj.Name)
            {
                case "Object": SaveObjectRef(obj); break;
                case "SmokeGen": SaveSmokeGen(obj); break;
                case "AreaAlien": SaveAreaAlien(obj); break;
            }
        }
    }

    private void SaveObjectRef(TreeNode obj)
    {
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

        SaveObjectAttributes(obj);
    }

    private void SaveObjectAttributes(TreeNode obj)
    {
        var scale = obj.FindChildLeaf("Scale");
        if (scale != null) { _w.WriteByte(0x17); WriteLeaf(scale); }

        var aiMode = obj.FindChildLeaf("AIMode");
        if (aiMode != null) { _w.WriteByte(0x3B); WriteLeaf(aiMode); }

        // HerdMarkers
        var numMarkers = obj.FindChildLeaf("NumMarkers");
        if (numMarkers != null)
        {
            _w.WriteByte(0x5B);
            WriteLeaf(numMarkers);
            WriteLeaf(obj.GetChildLeaf("MarkerType"));
            WriteLeaf(obj.GetChildLeaf("ShowRadius"));
        }

        var teamId = obj.FindChildLeaf("TeamID");
        if (teamId != null) { _w.WriteByte(0x52); WriteLeaf(teamId); }

        // HerdType
        var herdType = obj.FindChildLeaf("HerdType");
        if (herdType != null) { _w.WriteByte(0x53); WriteLeaf(herdType); }

        // HerdCount
        var teamCount = obj.FindChildLeaf("TeamCount");
        if (teamCount != null)
        {
            _w.WriteByte(0x32);
            WriteLeaf(teamCount);
            WriteLeaf(obj.GetChildLeaf("ShowPath"));
        }

        // OData (before KeyTime to match game save order)
        var odata1 = obj.FindChildLeaf("OData1");
        if (odata1 != null)
        {
            _w.WriteByte(0x5A);
            WriteLeaf(odata1);
            WriteLeaf(obj.GetChildLeaf("OData2"));
            WriteLeaf(obj.GetChildLeaf("OData3"));
        }

        var splineKey = obj.FindChildLeaf("KeyTime");
        if (splineKey != null) { _w.WriteByte(0x6B); WriteLeaf(splineKey); }

        var lightR = obj.FindChildLeaf("LightColorR");
        if (lightR != null)
        {
            _w.WriteByte(0x5F);
            WriteLeaf(lightR);
            WriteLeaf(obj.GetChildLeaf("LightColorG"));
            WriteLeaf(obj.GetChildLeaf("LightColorB"));
        }

        // AnimType
        var animType = obj.FindChildLeaf("AnimType");
        if (animType != null) { _w.WriteByte(0x13); WriteLeaf(animType); }

        // AnimTime
        var animTime = obj.FindChildLeaf("AnimTime");
        if (animTime != null) { _w.WriteByte(0x2B); WriteLeaf(animTime); }

        // FlickUsed
        var flickUsed = obj.FindChildLeaf("FlickUsed");
        if (flickUsed != null) { _w.WriteByte(0x4E); _w.WriteString32(flickUsed.StringValue); }

        // AIData
        var aiData0 = obj.FindChildLeaf("AIData0");
        if (aiData0 != null)
        {
            int count = 0;
            while (obj.FindChildLeaf($"AIData{count}") != null) count++;
            _w.WriteByte(0x50);
            _w.WriteByte((byte)count);
            for (int i = 0; i < count; i++)
                WriteLeaf(obj.GetChildLeaf($"AIData{i}"));
        }

        // Directions (only if object was originally 0x2A and got a 0x51 override)
        // 0x51 adds DirFacing/TiltForward/TiltLeft but these overlap with the main ObjectRef fields.
        // Directions is only meaningful when used on an already-placed object to override orientation.

        // HerdScale (only for regular Objects, not AreaAlien which uses 0x41 instead)
        if (obj.Name != "AreaAlien")
        {
            var minScale = obj.FindChildLeaf("MinScale");
            if (minScale != null)
            {
                _w.WriteByte(0x3F);
                WriteLeaf(minScale);
                WriteLeaf(obj.GetChildLeaf("MaxScale"));
            }
        }

        // SplineStartId
        var startId = obj.FindChildLeaf("StartId");
        if (startId != null) { _w.WriteByte(0x6A); WriteLeaf(startId); }

        // SplineScale
        var inScale = obj.FindChildLeaf("InScale");
        if (inScale != null)
        {
            _w.WriteByte(0x61);
            WriteLeaf(inScale);
            WriteLeaf(obj.GetChildLeaf("OutScale"));
        }

        // SplineTangents
        var inTangent = obj.FindChildLeaf("InTangent");
        if (inTangent != null)
        {
            _w.WriteByte(0x62);
            WriteLeaf(inTangent);
            WriteLeaf(obj.GetChildLeaf("OutTangent"));
        }

        // SplinePath3D (marker node, no data)
        if (obj.FindChildNode("SplinePath3D") != null) _w.WriteByte(0x63);

        // SplineJet (marker node, no data)
        if (obj.FindChildNode("SplineJet") != null) _w.WriteByte(0x64);

        // MinishopRIcons
        var ricon0 = obj.FindChildLeaf("RIcon0");
        if (ricon0 != null)
        {
            int count = 0;
            while (obj.FindChildLeaf($"RIcon{count}") != null) count++;
            _w.WriteByte(0x67);
            _w.WriteInt32(count);
            for (int i = 0; i < count; i++)
                WriteLeaf(obj.GetChildLeaf($"RIcon{i}"));
        }

        // MinishopMIcons
        var micon0 = obj.FindChildLeaf("MIcon0");
        if (micon0 != null)
        {
            int count = 0;
            while (obj.FindChildLeaf($"MIcon{count}") != null) count++;
            _w.WriteByte(0x68);
            _w.WriteInt32(count);
            for (int i = 0; i < count; i++)
                WriteLeaf(obj.GetChildLeaf($"MIcon{i}"));
        }

        // HerdPoints
        foreach (var herdPt in obj.EnumerateNodes().Where(c => c.Name == "HerdPoint"))
        {
            _w.WriteByte(0x48);
            WriteLeaf(herdPt.GetChildLeaf("X"));
            WriteLeaf(herdPt.GetChildLeaf("Y"));
            WriteLeaf(herdPt.GetChildLeaf("Z"));
        }

        // Paths
        foreach (var path in obj.EnumerateNodes().Where(c => c.Name == "Path"))
        {
            _w.WriteByte(0x2D);
            _w.WriteString32(path.GetChildLeaf("AnimName").StringValue);
            WriteLeaf(path.GetChildLeaf("PathSpeed"));
        }

        // GroundPaths
        foreach (var gpath in obj.EnumerateNodes().Where(c => c.Name == "GroundPath"))
        {
            _w.WriteByte(0x3A);
            _w.WriteString32(gpath.GetChildLeaf("AnimName").StringValue);
            WriteLeaf(gpath.GetChildLeaf("PathSpeed"));
        }

        // Wind
        foreach (var wind in obj.EnumerateNodes().Where(c => c.Name == "Wind"))
        {
            _w.WriteByte(0x54);
            _w.WriteString32(wind.GetChildLeaf("AnimName").StringValue);
            WriteLeaf(wind.GetChildLeaf("PathSpeed"));
            WriteLeaf(wind.GetChildLeaf("Distance"));
            WriteLeaf(wind.GetChildLeaf("Magnitude"));
        }

        // Locks (recursive)
        foreach (var lockNode in obj.EnumerateNodes().Where(c => c.Name == "Lock"))
        {
            _w.WriteByte(0x4C);
            WriteLeaf(lockNode.GetChildLeaf("Type"));
            WriteLeaf(lockNode.GetChildLeaf("LockRefSrc"));
            WriteLeaf(lockNode.GetChildLeaf("LockRefDst"));
            SaveAllObjects(lockNode);
            _w.WriteByte(0x4D);
        }
    }

    private void SaveSmokeGen(TreeNode obj)
    {
        _w.WriteByte(0x42);
        WriteLeaf(obj.GetChildLeaf("Type"));
        WriteLeaf(obj.GetChildLeaf("X"));
        WriteLeaf(obj.GetChildLeaf("Y"));
        WriteLeaf(obj.GetChildLeaf("Z"));
        WriteLeaf(obj.GetChildLeaf("StopTimeMin"));
        WriteLeaf(obj.GetChildLeaf("StopTimeMax"));
        WriteLeaf(obj.GetChildLeaf("GoTimeMin"));
        WriteLeaf(obj.GetChildLeaf("GoTimeMax"));
        WriteLeaf(obj.GetChildLeaf("GenRateMin"));
        WriteLeaf(obj.GetChildLeaf("GenRateMax"));
        WriteLeaf(obj.GetChildLeaf("ScaleStart"));
        WriteLeaf(obj.GetChildLeaf("ScaleEnd"));
        WriteLeaf(obj.GetChildLeaf("SpeedMin"));
        WriteLeaf(obj.GetChildLeaf("SpeedMax"));
        WriteLeaf(obj.GetChildLeaf("FadeTimeMin"));
        WriteLeaf(obj.GetChildLeaf("FadeTimeMax"));
        WriteLeaf(obj.GetChildLeaf("WindAngMin"));
        WriteLeaf(obj.GetChildLeaf("WindAngMax"));
        WriteLeaf(obj.GetChildLeaf("WindAngRate"));
        WriteLeaf(obj.GetChildLeaf("WindSpeedMin"));
        WriteLeaf(obj.GetChildLeaf("WindSpeedMax"));
        WriteLeaf(obj.GetChildLeaf("WindSpeedRate"));
        WriteLeaf(obj.GetChildLeaf("White"));
        SaveObjectAttributes(obj);
    }

    private void SaveAreaAlien(TreeNode obj)
    {
        _w.WriteByte(0x40);
        WriteLeaf(obj.GetChildLeaf("Type"));
        WriteLeaf(obj.GetChildLeaf("Count"));
        WriteLeaf(obj.GetChildLeaf("X"));
        WriteLeaf(obj.GetChildLeaf("Y"));
        WriteLeaf(obj.GetChildLeaf("Z"));
        WriteLeaf(obj.GetChildLeaf("MinRadius"));
        WriteLeaf(obj.GetChildLeaf("MaxRadius"));

        // AreaAlienScale uses same leaf names as HerdScale
        var minScale = obj.FindChildLeaf("MinScale");
        if (minScale != null)
        {
            _w.WriteByte(0x41);
            WriteLeaf(minScale);
            WriteLeaf(obj.GetChildLeaf("MaxScale"));
        }

        SaveObjectAttributes(obj);
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
        foreach (var child in n.EnumerateNodes())
        {
            if (child.Name == "Flick")
            {
                _w.WriteByte(0x4F);
                _w.WriteString32(child.GetChildLeaf("Name").StringValue);
                _w.WriteString32(child.GetChildLeaf("Trigger").StringValue);
            }
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

    private void SaveWaterMaterial(TreeNode n)
    {
        _w.WriteByte(0x90);
        WriteLeaf(n.GetChildLeaf("DiffuseR"));
        WriteLeaf(n.GetChildLeaf("DiffuseG"));
        WriteLeaf(n.GetChildLeaf("DiffuseB"));
        WriteLeaf(n.GetChildLeaf("DiffuseA"));
        WriteLeaf(n.GetChildLeaf("AmbientR"));
        WriteLeaf(n.GetChildLeaf("AmbientG"));
        WriteLeaf(n.GetChildLeaf("AmbientB"));
        WriteLeaf(n.GetChildLeaf("AmbientA"));
        WriteLeaf(n.GetChildLeaf("SpecularR"));
        WriteLeaf(n.GetChildLeaf("SpecularG"));
        WriteLeaf(n.GetChildLeaf("SpecularB"));
        WriteLeaf(n.GetChildLeaf("SpecularA"));
        WriteLeaf(n.GetChildLeaf("Power"));
    }

    private void SaveWorldGrid(TreeNode n)
    {
        _w.WriteByte(0x47);
        WriteLeaf(n.GetChildLeaf("GridStep"));
        WriteLeaf(n.GetChildLeaf("GridMinX"));
        WriteLeaf(n.GetChildLeaf("GridMaxX"));
        WriteLeaf(n.GetChildLeaf("GridMinY"));
        WriteLeaf(n.GetChildLeaf("GridMaxY"));
    }

    private void SaveGenericMusic(TreeNode n, byte opcode)
    {
        _w.WriteByte(opcode);
        _w.WriteString32(n.GetChildLeaf("Track1").StringValue);
        _w.WriteString32(n.GetChildLeaf("Track2").StringValue);
    }

    private void SaveSingleMusic(TreeNode n, byte opcode)
    {
        _w.WriteByte(opcode);
        _w.WriteString32(n.GetChildLeaf("Track").StringValue);
    }
}
