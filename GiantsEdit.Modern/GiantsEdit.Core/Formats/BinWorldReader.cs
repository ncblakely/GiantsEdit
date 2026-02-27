using System.Diagnostics;
using GiantsEdit.Core.DataModel;

namespace GiantsEdit.Core.Formats;

/// <summary>
/// Reads .bin world map files into the tree data model.
/// </summary>
/// <remarks>
/// File layout:
///   [0..31]  8 x int32 header: pointers[0..6] + magic ($1A0002E5)
///   [ptr0]   Main data block: [FileStart] + chunks until $FF
///   [ptr1]   [textures] section
///   [ptr2]   [sfx] section (sound effects)
///   [ptr3]   [objdefs] section (object definitions / GBS models)
///   [ptr4]   [fx] section (environment FX type)
///   [ptr5]   [scenerios] section
///   [ptr6]   [includefiles] section
/// </remarks>
public class BinWorldReader
{
    private const int Magic = 0x1A0002E5;
    private const int HeaderSize = 32;
    private BinaryDataReader _r = null!;
    private TreeNode _base = null!;
    private TreeNode _entry = null!;
    private TreeNode _lastObject = null!;

    /// <summary>
    /// Loads a world .bin file into a tree structure.
    /// </summary>
    /// <param name="data">Raw file bytes.</param>
    /// <param name="mapDataRule">DTD rule for the root MapData node (from w.dtd).</param>
    /// <returns>The root TreeNode, or null if the file is invalid.</returns>
    public TreeNode? Load(byte[] data, DtdNode? mapDataRule = null)
    {
        _r = new BinaryDataReader(data);
        _base = new TreeNode("Map data", mapDataRule) { State = TreeState.Visible };

        // Read header: 8 x int32 = 32 bytes
        // Delphi declares pointers[-1..6], so the first int32 is index -1 (unused/ignored)
        int _unused = _r.ReadInt32(); // pointers[-1] — not a section pointer
        var pointers = new int[7];
        for (int i = 0; i < 7; i++)
            pointers[i] = _r.ReadInt32();

        // Validate pointers
        for (int i = 0; i < 7; i++)
            if (pointers[i] < HeaderSize || pointers[i] > data.Length)
                return null;

        // Main data block
        _r.Position = pointers[0];
        int blockLen = _r.ReadInt32();

        _entry = _base.AddNode("[FileStart]");
        _entry.AddString("Box", _r.ReadPChar());
        _entry.AddString("GtiName", _r.ReadPChar());

        // Read chunks until $FF
        while (_r.HasMore)
        {
            byte b = _r.ReadByte();
            if (b == 0xFF) break;
            try
            {
                ReadChunk(b);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BinWorldReader] Error in chunk 0x{b:X2} at pos {_r.Position}: {ex.Message}");
                break;
            }
        }

        // [textures] section
        try
        {
            _r.Position = pointers[1];
            _entry = _base.AddNode("[textures]");
            int count = _r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var tex = _entry.AddNode("texture");
                tex.AddByte("Unknown", _r.ReadByte());
                tex.AddByte("IsSkyDome", _r.ReadByte());
                tex.AddString("Name", _r.ReadBLString());
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[BinWorldReader] Error in textures section: {ex.Message}"); }

        // [sfx] section (sound effects)
        try
        {
            _r.Position = pointers[2];
            if (_base.FindChildNode("[sfx]") == null)
                _entry = _base.AddNode("[sfx]");
            else
                _entry = _base.FindChildNode("[sfx]")!;
            _entry.AddInt32("NumOrVersion", _r.ReadInt32());
            _entry.AddInt32("SfxVersion", _r.ReadInt32());
            _entry.AddInt32("Count", _r.ReadInt32());
            _entry.AddInt32("EntrySize", _r.ReadInt32());
            _entry.AddInt32("DataSize", _r.ReadInt32());
        }
        catch (Exception ex) { Debug.WriteLine($"[BinWorldReader] Error in sfx section: {ex.Message}"); }

        // [objdefs] section (object definitions / GBS models)
        try
        {
            _r.Position = pointers[3];
            _entry = _base.AddNode("[objdefs]");
            int count2 = _r.ReadInt32();
            for (int i = 0; i < count2; i++)
                _entry.AddByte("data", _r.ReadByte());
        }
        catch (Exception ex) { Debug.WriteLine($"[BinWorldReader] Error in objdefs section: {ex.Message}"); }

        // [fx] section
        try
        {
            _r.Position = pointers[4];
            _entry = _base.AddNode("[fx]");
            _entry.AddInt32("EnvironmentType", _r.ReadInt32());
        }
        catch (Exception ex) { Debug.WriteLine($"[BinWorldReader] Error in fx section: {ex.Message}"); }

        // [scenerios] section
        try
        {
            _r.Position = pointers[5];
            _entry = _base.AddNode("[scenerios]");
            int count3 = _r.ReadInt32();
            for (int i = 0; i < count3; i++)
            {
                var sc = _entry.AddNode("scenerio");
                sc.AddByte("Index", _r.ReadByte());
                sc.AddString("Name", _r.ReadString32());
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[BinWorldReader] Error in scenerios section: {ex.Message}"); }

        // [includefiles] section
        try
        {
            _r.Position = pointers[6];
            _entry = _base.AddNode("[includefiles]");
            int count4 = _r.ReadInt32();
            for (int i = 0; i < count4; i++)
                _entry.AddString("Name", _r.ReadString32());
        }
        catch (Exception ex) { Debug.WriteLine($"[BinWorldReader] Error in includefiles section: {ex.Message}"); }

        return _base;
    }

    private void Group(string groupName, string? childName = null)
    {
        var group = _base.FindChildNode(groupName) ?? _base.AddNode(groupName);
        _entry = childName != null ? group.AddNode(childName) : group;
    }

    private void Hint(string name)
    {
        _entry = _base.AddNode(name);
    }

    private void ReadChunk(byte chunkType)
    {
        switch (chunkType)
        {
            case 0x13: // AnimStart
                _entry = _lastObject;
                _entry.AddByte("AnimType", _r.ReadByte());
                break;

            case 0x17: // Scale
                _entry = _lastObject;
                _entry.AddSingle("Scale", _r.ReadSingle());
                break;

            case 0x1D: Group("<Textures>"); _entry.AddStringL("OutDomeTex", _r.ReadString16(), 16); break;
            case 0x1E: Group("<Textures>"); _entry.AddStringL("DomeTex", _r.ReadString16(), 16); break;
            case 0x1F: Group("<Textures>"); _entry.AddStringL("DomeEdgeTex", _r.ReadString16(), 16); break;
            case 0x20: Group("<Textures>"); _entry.AddStringL("WFall1Tex", _r.ReadString16(), 16); break;
            case 0x21: Group("<Textures>"); _entry.AddStringL("WFall2Tex", _r.ReadString16(), 16); break;
            case 0x22: Group("<Textures>"); _entry.AddStringL("WFall3Tex", _r.ReadString16(), 16); break;
            case 0x23: Group("<Textures>"); _entry.AddStringL("SpaceLineTex", _r.ReadString16(), 16); break;
            case 0x24: Group("<Textures>"); _entry.AddStringL("SpaceTex", _r.ReadString16(), 16); break;
            case 0x25: Group("<Textures>"); _entry.AddStringL("SeaTex", _r.ReadString16(), 16); break;
            case 0x26: Group("<Textures>"); _entry.AddStringL("GlowTex", _r.ReadString16(), 16); break;

            case 0x27: // Teleport
                Group("<Teleports>", "Teleport");
                _entry.AddByte("Index", _r.ReadByte());
                _entry.AddSingle("X", _r.ReadSingle());
                _entry.AddSingle("Y", _r.ReadSingle());
                _entry.AddSingle("Z", _r.ReadSingle());
                _entry.AddSingle("DirFacing", _r.ReadSingle());
                break;

            case 0x28: // SunColor
                Group("<Sun>", "SunColor");
                _entry.AddByte("Red", _r.ReadByte());
                _entry.AddByte("Green", _r.ReadByte());
                _entry.AddByte("Blue", _r.ReadByte());
                break;

            case 0x29: // Fog
            {
                var existing = _base.FindChildNode("Fog");
                if (existing != null) _base.RemoveNode(existing);
                Hint("Fog");
                _entry.AddSingle("FogMin", _r.ReadSingle());
                _entry.AddSingle("FogMax", _r.ReadSingle());
                _entry.AddByte("Red", _r.ReadByte());
                _entry.AddByte("Green", _r.ReadByte());
                _entry.AddByte("Blue", _r.ReadByte());
                break;
            }

            case 0x2A: // ObjectRef
                Group("<Objects>", "Object");
                _entry.AddInt32("Type", _r.ReadInt32());
                _entry.AddSingle("X", _r.ReadSingle());
                _entry.AddSingle("Y", _r.ReadSingle());
                _entry.AddSingle("Z", _r.ReadSingle());
                _entry.AddSingle("DirFacing", _r.ReadSingle());
                _lastObject = _entry;
                break;

            case 0x2B: // AnimTime
                _entry = _lastObject;
                _entry.AddSingle("AnimTime", _r.ReadSingle());
                break;

            case 0x2C: // Music (obsolete)
                Hint("Music");
                _entry.AddString("Name", _r.ReadString32());
                break;

            case 0x2D: // Path
            {
                _entry = _lastObject;
                var pathNode = _entry.AddNode("Path");
                pathNode.AddString("AnimName", _r.ReadString32());
                pathNode.AddSingle("PathSpeed", _r.ReadSingle());
                break;
            }

            case 0x2E: // SeaSpeed
                Hint("SeaSpeed");
                _entry.AddSingle("Cycle", _r.ReadSingle());
                _entry.AddSingle("Speed", _r.ReadSingle());
                _entry.AddSingle("Trans", _r.ReadSingle());
                break;

            case 0x2F: // EffectRef (obsolete)
                _r.Skip(4 + 6 * 4); // int + 6 floats
                break;

            case 0x30: // StartLoc
                Group("<StartLocs>", "StartLoc");
                _entry.AddByte("Type", _r.ReadByte());
                _entry.AddByte("StartNumber", _r.ReadByte());
                _entry.AddSingle("X", _r.ReadSingle());
                _entry.AddSingle("Y", _r.ReadSingle());
                _entry.AddSingle("Z", _r.ReadSingle());
                _entry.AddSingle("DirFacing", _r.ReadSingle());
                break;

            case 0x31: // HerdPath (obsolete)
                _entry = _lastObject;
                _r.Skip(32 + 4 + 4); // 32-byte name + 2 floats
                break;

            case 0x32: // HerdCount
                _entry = _lastObject;
                _entry.AddByte("TeamCount", _r.ReadByte());
                _entry.AddByte("ShowPath", _r.ReadByte());
                break;

            case 0x33: // Garden (obsolete)
                _r.Skip(32 + 4);
                break;

            case 0x34: // GroundTexture
                Group("<Textures>", "GroundTexture");
                ReadTexture();
                break;
            case 0x35: // SlopeTexture
                Group("<Textures>", "SlopeTexture");
                ReadTexture();
                break;
            case 0x36: // WallTexture
                Group("<Textures>", "WallTexture");
                ReadTexture();
                break;

            case 0x37: // Single (obsolete)
                _r.Skip(32 + 4);
                break;
            case 0x38: // Group (obsolete)
                _r.Skip(32 + 4);
                break;

            case 0x39: // Tiling
                Hint("Tiling");
                _entry.AddSingle("Obsolete0", _r.ReadSingle());
                _entry.AddSingle("Obsolete1", _r.ReadSingle());
                _entry.AddSingle("Obsolete2", _r.ReadSingle());
                _entry.AddSingle("MixNear", _r.ReadSingle());
                _entry.AddSingle("MixFar", _r.ReadSingle());
                _entry.AddSingle("MixNearBlend", _r.ReadSingle());
                _entry.AddSingle("MixFarBlend", _r.ReadSingle());
                break;

            case 0x3A: // GroundPath
            {
                _entry = _lastObject;
                var pathNode = _entry.AddNode("GroundPath");
                pathNode.AddString("AnimName", _r.ReadString32());
                pathNode.AddSingle("PathSpeed", _r.ReadSingle());
                break;
            }

            case 0x3B: // AIMode
                _entry = _lastObject;
                _entry.AddByte("AIMode", _r.ReadByte());
                break;

            case 0x3C: // BaseDefinition (obsolete)
                _r.Skip(4 * 4);
                break;
            case 0x3D: // EndBaseDefinition (obsolete)
                break;
            case 0x3E: // BaseObject (obsolete)
                _r.Skip(5 * 4);
                break;

            case 0x3F: // HerdScale
                _entry = _lastObject;
                _entry.AddSingle("MinScale", _r.ReadSingle());
                _entry.AddSingle("MaxScale", _r.ReadSingle());
                break;

            case 0x40: // AreaAlien
                Group("<Objects>", "AreaAlien");
                _entry.AddInt32("Type", _r.ReadInt32());
                _entry.AddByte("Count", _r.ReadByte());
                _entry.AddSingle("X", _r.ReadSingle());
                _entry.AddSingle("Y", _r.ReadSingle());
                _entry.AddSingle("Z", _r.ReadSingle());
                _entry.AddSingle("MinRadius", _r.ReadSingle());
                _entry.AddSingle("MaxRadius", _r.ReadSingle());
                _lastObject = _entry;
                break;

            case 0x41: // AreaAlienScale
                _entry = _lastObject;
                _entry.AddSingle("MinScale", _r.ReadSingle());
                _entry.AddSingle("MaxScale", _r.ReadSingle());
                break;

            case 0x42: // SmokeGen
                Group("<Objects>", "SmokeGen");
                _entry.AddInt32("Type", _r.ReadInt32());
                _entry.AddSingle("X", _r.ReadSingle());
                _entry.AddSingle("Y", _r.ReadSingle());
                _entry.AddSingle("Z", _r.ReadSingle());
                _entry.AddSingle("StopTimeMin", _r.ReadSingle());
                _entry.AddSingle("StopTimeMax", _r.ReadSingle());
                _entry.AddSingle("GoTimeMin", _r.ReadSingle());
                _entry.AddSingle("GoTimeMax", _r.ReadSingle());
                _entry.AddSingle("GenRateMin", _r.ReadSingle());
                _entry.AddSingle("GenRateMax", _r.ReadSingle());
                _entry.AddSingle("ScaleStart", _r.ReadSingle());
                _entry.AddSingle("ScaleEnd", _r.ReadSingle());
                _entry.AddSingle("SpeedMin", _r.ReadSingle());
                _entry.AddSingle("SpeedMax", _r.ReadSingle());
                _entry.AddSingle("FadeTimeMin", _r.ReadSingle());
                _entry.AddSingle("FadeTimeMax", _r.ReadSingle());
                _entry.AddSingle("WindAngMin", _r.ReadSingle());
                _entry.AddSingle("WindAngMax", _r.ReadSingle());
                _entry.AddSingle("WindAngRate", _r.ReadSingle());
                _entry.AddSingle("WindSpeedMin", _r.ReadSingle());
                _entry.AddSingle("WindSpeedMax", _r.ReadSingle());
                _entry.AddSingle("WindSpeedRate", _r.ReadSingle());
                _entry.AddSingle("White", _r.ReadSingle());
                _lastObject = _entry;
                break;

            case 0x43: // BaseCreate (obsolete)
                break;

            case 0x44: // ObjEditStart
                Hint("ObjEditStart");
                break;
            case 0x45: // ObjEditEnd
                Hint("ObjEditEnd");
                break;

            case 0x46: // ObjectRef6
                Group("<Objects>", "Object");
                _entry.AddInt32("Type", _r.ReadInt32());
                _entry.AddSingle("X", _r.ReadSingle());
                _entry.AddSingle("Y", _r.ReadSingle());
                _entry.AddSingle("Z", _r.ReadSingle());
                _entry.AddSingle("DirFacing", _r.ReadSingle());
                _entry.AddSingle("TiltForward", _r.ReadSingle());
                _entry.AddSingle("TiltLeft", _r.ReadSingle());
                _lastObject = _entry;
                break;

            case 0x47: // WorldGrid
                Hint("WorldGrid");
                _entry.AddSingle("GridStep", _r.ReadSingle());
                _entry.AddSingle("GridMinX", _r.ReadSingle());
                _entry.AddSingle("GridMaxX", _r.ReadSingle());
                _entry.AddSingle("GridMinY", _r.ReadSingle());
                _entry.AddSingle("GridMaxY", _r.ReadSingle());
                break;

            case 0x48: // HerdPoint
                _entry = _lastObject;
            {
                var pt = _entry.AddNode("HerdPoint");
                pt.AddSingle("X", _r.ReadSingle());
                pt.AddSingle("Y", _r.ReadSingle());
                pt.AddSingle("Z", _r.ReadSingle());
            }
                break;

            case 0x49: // Scenario
                Hint("Scenario");
                _entry.AddInt32("Value", _r.ReadInt32());
                break;

            case 0x4A: // WorldNoLighting (obsolete)
                Hint("WorldNoLighting");
                break;

            case 0x4C: // LockStart
                _lastObject = _lastObject.AddNode("Lock");
                _entry = _lastObject;
                _entry.AddInt32("Type", _r.ReadInt32());
                _entry.AddByte("LockRefSrc", _r.ReadByte());
                _entry.AddByte("LockRefDst", _r.ReadByte());
                break;

            case 0x4D: // LockEnd
                _lastObject = _lastObject.Parent!;
                break;

            case 0x4E: // FlickUsed
                _entry = _lastObject;
                _entry.AddString("FlickUsed", _r.ReadString32());
                break;

            case 0x4F: // Flick
                Group("<Flicks>");
            {
                var flick = _entry.AddNode("Flick");
                flick.AddString("Name", _r.ReadString32());
                flick.AddString("Trigger", _r.ReadString32());
            }
                break;

            case 0x50: // AIData
            {
                _entry = _lastObject;
                int numAi = _r.ReadByte();
                for (int i = 0; i < numAi; i++)
                    _entry.AddSingle($"AIData{i}", _r.ReadSingle());
                break;
            }

            case 0x51: // Directions
                _entry = _lastObject;
                _entry.AddSingle("DirFacing", _r.ReadSingle());
                _entry.AddSingle("TiltForward", _r.ReadSingle());
                _entry.AddSingle("TiltLeft", _r.ReadSingle());
                break;

            case 0x52: // TeamID
                _entry = _lastObject;
                _entry.AddInt32("TeamID", _r.ReadInt32());
                break;

            case 0x53: // HerdType
                _entry = _lastObject;
                _entry.AddInt32("HerdType", _r.ReadInt32());
                break;

            case 0x54: // Wind
            {
                _entry = _lastObject;
                var windNode = _entry.AddNode("Wind");
                windNode.AddString("AnimName", _r.ReadString32());
                windNode.AddSingle("PathSpeed", _r.ReadSingle());
                windNode.AddSingle("Distance", _r.ReadSingle());
                windNode.AddSingle("Magnitude", _r.ReadSingle());
                break;
            }

            case 0x58: // WaterFog
            {
                var existing = _base.FindChildNode("WaterFog");
                if (existing != null) _base.RemoveNode(existing);
                Hint("WaterFog");
                _entry.AddSingle("FogMin", _r.ReadSingle());
                _entry.AddSingle("FogMax", _r.ReadSingle());
                _entry.AddByte("Red", _r.ReadByte());
                _entry.AddByte("Green", _r.ReadByte());
                _entry.AddByte("Blue", _r.ReadByte());
                break;
            }

            case 0x59: // StartWeather
                Hint("StartWeather");
                _entry.AddString("Name", _r.ReadPChar());
                break;

            case 0x5A: // OData
                _entry = _lastObject;
                _entry.AddSingle("OData1", _r.ReadSingle());
                _entry.AddSingle("OData2", _r.ReadSingle());
                _entry.AddSingle("OData3", _r.ReadSingle());
                break;

            case 0x5B: // HerdMarkers
                _entry = _lastObject;
                _entry.AddInt32("NumMarkers", _r.ReadInt32());
                _entry.AddInt32("MarkerType", _r.ReadInt32());
                _entry.AddInt32("ShowRadius", _r.ReadInt32());
                break;

            case 0x5C: // Scenerio
                Group("<Scenerios>", "Scenerio");
                _entry.AddByte("Type", _r.ReadByte());
                _entry.AddInt32("TriggersNeeded", _r.ReadInt32());
                _entry.AddString("Name", _r.ReadString32());
                break;

            case 0x5D: // Mission
                Group("<Missions>");
                _entry.AddString("Name", _r.ReadString32());
                break;

            case 0x5E: // Ambient
                Hint("Ambient");
                _entry.AddString("Name", _r.ReadString32());
                break;

            case 0x5F: // LightColor
                _entry = _lastObject;
                _entry.AddSingle("LightColorR", _r.ReadSingle());
                _entry.AddSingle("LightColorG", _r.ReadSingle());
                _entry.AddSingle("LightColorB", _r.ReadSingle());
                break;

            case 0x60: // NoScenerios
                Hint("NoScenerios");
                _entry.AddInt32("Value", _r.ReadInt32());
                break;

            case 0x61: // SplineScale
                _entry = _lastObject;
                _entry.AddSingle("InScale", _r.ReadSingle());
                _entry.AddSingle("OutScale", _r.ReadSingle());
                break;

            case 0x62: // SplineTangents
                _entry = _lastObject;
                _entry.AddSingle("InTangent", _r.ReadSingle());
                _entry.AddSingle("OutTangent", _r.ReadSingle());
                break;

            case 0x63: // SplinePath3D
                _entry = _lastObject;
                _entry.AddNode("SplinePath3D");
                break;

            case 0x64: // SplineJet
                _entry = _lastObject;
                _entry.AddNode("SplineJet");
                break;

            case 0x65: // LandTexFade
                Hint("LandTexFade");
                _entry.AddSingle("Falloff0", _r.ReadSingle());
                _entry.AddSingle("Falloff1", _r.ReadSingle());
                _entry.AddSingle("Falloff2", _r.ReadSingle());
                break;

            case 0x66: // LandAngles
                Hint("LandAngles");
                _entry.AddSingle("SlopeAngle", _r.ReadSingle());
                _entry.AddSingle("WallAngle", _r.ReadSingle());
                break;

            case 0x67: // MinishopRIcons
            {
                _entry = _lastObject;
                int n = _r.ReadInt32();
                for (int i = 0; i < n; i++)
                    _entry.AddInt32($"RIcon{i}", _r.ReadInt32());
                break;
            }

            case 0x68: // MinishopMIcons
            {
                _entry = _lastObject;
                int n = _r.ReadInt32();
                for (int i = 0; i < n; i++)
                    _entry.AddInt32($"MIcon{i}", _r.ReadInt32());
                break;
            }

            case 0x6A: // SplineStartId
                _entry = _lastObject;
                _entry.AddByte("StartId", _r.ReadByte());
                break;

            case 0x6B: // SplineKeyTime
                _entry = _lastObject;
                _entry.AddInt32("KeyTime", _r.ReadInt32());
                break;

            case 0x6C: ReadGenericMusic("MusicSuspense"); break;
            case 0x6D: ReadGenericMusic("MusicLight"); break;
            case 0x6E: ReadGenericMusic("MusicWin"); break;
            case 0x6F: ReadGenericMusic("MusicHeavy"); break;
            case 0x70: ReadSingleMusic("MusicFailure"); break;
            case 0x71: ReadSingleMusic("MusicSuccess"); break;

            case 0x72: Group("<Textures>", "GroundBumpTexture"); ReadTexture(); break;
            case 0x73: Group("<Textures>", "SlopeBumpTexture"); ReadTexture(); break;
            case 0x74: Group("<Textures>", "WallBumpTexture"); ReadTexture(); break;

            case 0x75: // BumpClampValue
                Hint("BumpClampValue");
                _entry.AddSingle("Value", _r.ReadSingle());
                break;

            case 0x76: // Sunfxname
                Group("<Sun>", "SunFxName");
                _entry.AddString("Name", _r.ReadPChar());
                _entry.AddSingle("ColorR", _r.ReadSingle());
                _entry.AddSingle("ColorG", _r.ReadSingle());
                _entry.AddSingle("ColorB", _r.ReadSingle());
                _entry.AddSingle("Exponent", _r.ReadSingle());
                _entry.AddSingle("Factor", _r.ReadSingle());
                break;

            case 0x77: // Sunflare1
                Group("<Sun>", "Sunflare1");
                ReadSunflare();
                break;
            case 0x78: // Sunflare2
                Group("<Sun>", "Sunflare2");
                ReadSunflare();
                break;

            case 0x79: // WaterColor
                Hint("WaterColor");
                ReadWaterColor();
                break;

            case 0x81: // MultiAmbient
                Hint("MultiAmbient");
                _entry.AddInt32("Value", _r.ReadInt32());
                break;

            case 0x82: // ArmyBin
                Hint("ArmyBin");
                _entry.AddString("Name", _r.ReadString32());
                break;

            case 0x84: // VoPath
                Hint("VoPath");
                _entry.AddString("Name", _r.ReadPChar());
                break;

            case 0x85: Group("<Textures>", "GroundDetailTexture"); ReadTexture(); break;
            case 0x86: Group("<Textures>", "SlopeDetailTexture"); ReadTexture(); break;
            case 0x87: Group("<Textures>", "WallDetailTexture"); ReadTexture(); break;

            case 0x8B: // AmbientColor
                Hint("AmbientColor");
                _entry.AddSingle("R", _r.ReadSingle());
                _entry.AddSingle("G", _r.ReadSingle());
                _entry.AddSingle("B", _r.ReadSingle());
                break;

            case 0x8C: Group("<Textures>", "GroundNormalTexture"); ReadTexture(); break;
            case 0x8D: Group("<Textures>", "SlopeNormalTexture"); ReadTexture(); break;
            case 0x8E: Group("<Textures>", "WallNormalTexture"); ReadTexture(); break;

            case 0x8F: // BlendWater
                Hint("BlendWater");
                _entry.AddSingle("FogScale", _r.ReadSingle());
                _entry.AddInt32("RenderFog", _r.ReadInt32());
                break;

            case 0x90: // WaterMaterial
                Hint("WaterMaterial");
                _entry.AddSingle("DiffuseR", _r.ReadSingle());
                _entry.AddSingle("DiffuseG", _r.ReadSingle());
                _entry.AddSingle("DiffuseB", _r.ReadSingle());
                _entry.AddSingle("DiffuseA", _r.ReadSingle());
                _entry.AddSingle("AmbientR", _r.ReadSingle());
                _entry.AddSingle("AmbientG", _r.ReadSingle());
                _entry.AddSingle("AmbientB", _r.ReadSingle());
                _entry.AddSingle("AmbientA", _r.ReadSingle());
                _entry.AddSingle("SpecularR", _r.ReadSingle());
                _entry.AddSingle("SpecularG", _r.ReadSingle());
                _entry.AddSingle("SpecularB", _r.ReadSingle());
                _entry.AddSingle("SpecularA", _r.ReadSingle());
                _entry.AddSingle("Power", _r.ReadSingle());
                break;

            default:
                Debug.WriteLine($"[BinWorldReader] Unknown chunk 0x{chunkType:X2} at pos {_r.Position - 1}, aborting chunk loop");
                _r.Position = _r.Length;
                break;
        }
    }

    private void ReadTexture()
    {
        _entry.AddString("Name", _r.ReadString32());
        _entry.AddSingle("Wrap", _r.ReadSingle());
        _entry.AddSingle("OffsetX", _r.ReadSingle());
        _entry.AddSingle("OffsetY", _r.ReadSingle());
    }

    private void ReadSunflare()
    {
        _entry.AddInt32("Type", _r.ReadInt32());
        _entry.AddSingle("Base0", _r.ReadSingle());
        _entry.AddSingle("Exponent0", _r.ReadSingle());
        _entry.AddSingle("Factor0", _r.ReadSingle());
        _entry.AddSingle("Damping0", _r.ReadSingle());
        _entry.AddSingle("Base1", _r.ReadSingle());
        _entry.AddSingle("Exponent1", _r.ReadSingle());
        _entry.AddSingle("Factor1", _r.ReadSingle());
        _entry.AddSingle("Damping1", _r.ReadSingle());
        _entry.AddSingle("OscillationAmplitude", _r.ReadSingle());
        _entry.AddSingle("OscillationFrequency", _r.ReadSingle());
        _entry.AddSingle("SpinFrequency", _r.ReadSingle());
    }

    private void ReadWaterColor()
    {
        _entry.AddSingle("ColorR", _r.ReadSingle());
        _entry.AddSingle("ColorG", _r.ReadSingle());
        _entry.AddSingle("ColorB", _r.ReadSingle());
        for (int i = 0; i < 5; i++)
        {
            _entry.AddSingle($"Color{i}R", _r.ReadSingle());
            _entry.AddSingle($"Color{i}G", _r.ReadSingle());
            _entry.AddSingle($"Color{i}B", _r.ReadSingle());
        }
        _entry.AddSingle("ReflectionFalloff", _r.ReadSingle());
        _entry.AddSingle("ReflectionWidth", _r.ReadSingle());
        _entry.AddSingle("ReflectionScale", _r.ReadSingle());
        _entry.AddSingle("ReflectionAmplitude", _r.ReadSingle());
        _entry.AddSingle("WaveSpeed", _r.ReadSingle());
    }

    private void ReadGenericMusic(string name)
    {
        Hint(name);
        _entry.AddString("Track1", _r.ReadString32());
        _entry.AddString("Track2", _r.ReadString32());
    }

    private void ReadSingleMusic(string name)
    {
        Hint(name);
        _entry.AddString("Track", _r.ReadString32());
    }
}
