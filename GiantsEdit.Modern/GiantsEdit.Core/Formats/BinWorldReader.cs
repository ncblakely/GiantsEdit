using System.Diagnostics;
using GiantsEdit.Core.DataModel;

namespace GiantsEdit.Core.Formats;

/// <summary>
/// Reads .bin world map files into the tree data model.
/// Ported from Delphi's bin_w_read.pas LoadBinW function.
/// </summary>
/// <remarks>
/// File layout:
///   [0..31]  8 x int32 header: pointers[0..6] + magic ($1A0002E5)
///   [ptr0]   Main data block: [FileStart] + chunks until $FF
///   [ptr1]   [textures] section
///   [ptr2]   [sfxlist] section
///   [ptr3]   [unknown] section
///   [ptr4]   [fx] section
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

        // [sfxlist] section
        try
        {
            _r.Position = pointers[2];
            if (_base.FindChildNode("[sfxlist]") == null)
                _entry = _base.AddNode("[sfxlist]");
            else
                _entry = _base.FindChildNode("[sfxlist]")!;
            _entry.AddInt32("p0", _r.ReadInt32());
            _entry.AddInt32("p1", _r.ReadInt32());
            _entry.AddInt32("p2", _r.ReadInt32());
            _entry.AddInt32("p3", _r.ReadInt32());
            _entry.AddInt32("p4", _r.ReadInt32());
        }
        catch (Exception ex) { Debug.WriteLine($"[BinWorldReader] Error in sfxlist section: {ex.Message}"); }

        // [unknown] section
        try
        {
            _r.Position = pointers[3];
            _entry = _base.AddNode("[unknown]");
            int count2 = _r.ReadInt32();
            for (int i = 0; i < count2; i++)
                _entry.AddByte("unknown", _r.ReadByte());
        }
        catch (Exception ex) { Debug.WriteLine($"[BinWorldReader] Error in unknown section: {ex.Message}"); }

        // [fx] section
        try
        {
            _r.Position = pointers[4];
            _entry = _base.AddNode("[fx]");
            _entry.AddInt32("p0", _r.ReadInt32());
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
                Group("<Teleport>", "Teleport");
                _entry.AddByte("Index", _r.ReadByte());
                _entry.AddSingle("X", _r.ReadSingle());
                _entry.AddSingle("Y", _r.ReadSingle());
                _entry.AddSingle("Z", _r.ReadSingle());
                _entry.AddSingle("Angle", _r.ReadSingle());
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
                _entry.AddSingle("Near distance", _r.ReadSingle());
                _entry.AddSingle("Far distance", _r.ReadSingle());
                _entry.AddByte("Red", _r.ReadByte());
                _entry.AddByte("Green", _r.ReadByte());
                _entry.AddByte("Blue", _r.ReadByte());
                break;
            }

            case 0x2A: // Object (1-angle)
                Group("<Objects>", "Object");
                _entry.AddInt32("Type", _r.ReadInt32());
                _entry.AddSingle("X", _r.ReadSingle());
                _entry.AddSingle("Y", _r.ReadSingle());
                _entry.AddSingle("Z", _r.ReadSingle());
                _entry.AddSingle("Angle", _r.ReadSingle());
                _lastObject = _entry;
                break;

            case 0x2B: // AnimTime (no data)
                Hint(";?AnimTime");
                break;

            case 0x2C: // Music
                Hint("Music");
                _entry.AddString("Music", _r.ReadString32());
                break;

            case 0x2D: // Path (no data)
                Hint("Path");
                break;

            case 0x2E: // SeaSpeed
                Hint("SeaSpeed");
                _entry.AddSingle("p0", _r.ReadSingle());
                _entry.AddSingle("p1", _r.ReadSingle());
                _entry.AddSingle("p2", _r.ReadSingle());
                break;

            case 0x2F: // EffectRef (no data)
                Hint("EffectRef");
                break;

            case 0x30: // StartLoc
                Group("<StartLoc>", "StartLoc");
                _entry.AddByte("Index", _r.ReadByte());
                _entry.AddByte("Unknown", _r.ReadByte());
                _entry.AddSingle("X", _r.ReadSingle());
                _entry.AddSingle("Y", _r.ReadSingle());
                _entry.AddSingle("Z", _r.ReadSingle());
                _entry.AddSingle("Angle", _r.ReadSingle());
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

            case 0x39: // Tiling
                Hint("Tiling");
                for (int i = 0; i < 7; i++)
                    _entry.AddSingle($"p{i}", _r.ReadSingle());
                break;

            case 0x3B: // AIMode
                _entry = _lastObject;
                _entry.AddByte("AIMode", _r.ReadByte());
                break;

            case 0x44: // ObjEditStart marker (no data)
                break;
            case 0x45: // ObjEditEnd marker (no data)
                break;

            case 0x46: // Object (6-float: x,y,z + dirfacing,tiltfwd,tiltleft)
                Group("<Objects>", "Object");
                _entry.AddInt32("Type", _r.ReadInt32());
                _entry.AddSingle("X", _r.ReadSingle());
                _entry.AddSingle("Y", _r.ReadSingle());
                _entry.AddSingle("Z", _r.ReadSingle());
                _entry.AddSingle("Angle", _r.ReadSingle());
                _entry.AddSingle("Tilt Forward", _r.ReadSingle());
                _entry.AddSingle("Tilt Left", _r.ReadSingle());
                _lastObject = _entry;
                break;

            case 0x49: // Scenario
                Hint("Scenario");
                _entry.AddSingle("p0", _r.ReadSingle());
                break;

            case 0x4A: // WorldNoLightning
                Hint("WorldNoLightning");
                break;

            case 0x4C: // Lock (push)
                _lastObject = _lastObject.AddNode("Lock");
                _entry = _lastObject;
                _entry.AddInt32("Type", _r.ReadInt32());
                _entry.AddByte("Lock 1", _r.ReadByte());
                _entry.AddByte("Lock 2", _r.ReadByte());
                break;

            case 0x4D: // Lock end (pop)
                _lastObject = _lastObject.Parent!;
                break;

            case 0x4F: // Flick
                Group("<Flicks>");
                _entry.AddString("Name", _r.ReadString64());
                break;

            case 0x52: // TeamID
                _entry = _lastObject;
                _entry.AddInt32("TeamID", _r.ReadInt32());
                break;

            case 0x58: // WaterFog
            {
                var existing = _base.FindChildNode("WaterFog");
                if (existing != null) _base.RemoveNode(existing);
                Hint("WaterFog");
                _entry.AddSingle("Near distance", _r.ReadSingle());
                _entry.AddSingle("Far distance", _r.ReadSingle());
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
                _entry.AddSingle("OData 0", _r.ReadSingle());
                _entry.AddSingle("OData 1", _r.ReadSingle());
                _entry.AddSingle("OData 2", _r.ReadSingle());
                break;

            case 0x5B: // HerdMarkers (stored as int32 bit patterns for single)
                _entry = _lastObject;
                _entry.AddInt32("HerdMarker 0", _r.ReadInt32());
                _entry.AddInt32("HerdMarker 1", _r.ReadInt32());
                _entry.AddInt32("HerdMarker 2", _r.ReadInt32());
                _entry.AddInt32("HerdMarker 3", _r.ReadInt32());
                _entry.AddInt32("HerdMarker 4", _r.ReadInt32());
                break;

            case 0x5C: // Scenerio
                Group("<Scenerios>", "scenerio");
                _entry.AddByte("Type", _r.ReadByte());
                _entry.AddInt32("Index", _r.ReadInt32());
                _entry.AddString("Name", _r.ReadString32());
                break;

            case 0x5D: // Mission
                Group("<Missions>");
                _entry.AddString("Name", _r.ReadString32());
                break;

            case 0x5F: // LightColor
                _entry = _lastObject;
                _entry.AddSingle("LightColor 0", _r.ReadSingle());
                _entry.AddSingle("LightColor 1", _r.ReadSingle());
                _entry.AddSingle("LightColor 2", _r.ReadSingle());
                break;

            case 0x60: // DisableScenerios
                Hint("DisableScenerios");
                _entry.AddInt32("Value", _r.ReadInt32());
                break;

            case 0x65: // LandTexFade
                Hint("LandTexFade");
                _entry.AddSingle("p0", _r.ReadSingle());
                _entry.AddSingle("p1", _r.ReadSingle());
                _entry.AddSingle("p2", _r.ReadSingle());
                break;

            case 0x66: // LandAngles
                Hint("LandAngles");
                _entry.AddSingle("Angle 0", _r.ReadSingle());
                _entry.AddSingle("Angle 1", _r.ReadSingle());
                break;

            case 0x67: // MinishopRIcons
            {
                _entry = _lastObject;
                int n = _r.ReadInt32();
                for (int i = 0; i < n; i++)
                    _entry.AddInt32("MinishopRIcon", _r.ReadInt32());
                break;
            }

            case 0x68: // MinishopMIcons
            {
                _entry = _lastObject;
                int n = _r.ReadInt32();
                for (int i = 0; i < n; i++)
                    _entry.AddInt32("MinishopMIcon", _r.ReadInt32());
                break;
            }

            case 0x6B: // SplineKeyTime
                _entry = _lastObject;
                _entry.AddInt32("SplineKeyTime", _r.ReadInt32());
                break;

            case 0x6C: ReadGenericMusic("MusicSuspense"); break;
            case 0x6D: ReadGenericMusic("MusicLight"); break;
            case 0x6E: ReadGenericMusic("MusicWin"); break;
            case 0x6F: ReadGenericMusic("MusicHeavy"); break;

            case 0x72: Group("<Textures>", "GroundBumpTexture"); ReadTexture(); break;
            case 0x73: Group("<Textures>", "SlopeBumpTexture"); ReadTexture(); break;
            case 0x74: Group("<Textures>", "WallBumpTexture"); ReadTexture(); break;

            case 0x75: // BumpClampValue
                Hint("bumpclampvalue");
                _entry.AddSingle("Value", _r.ReadSingle());
                break;

            case 0x76: // Sunfxname
                Group("<Sun>", "Sunfxname");
                _entry.AddString("Name", _r.ReadPChar());
                for (int i = 0; i < 5; i++)
                    _entry.AddSingle($"p{i}", _r.ReadSingle());
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
                for (int i = 0; i < 23; i++)
                    _entry.AddSingle($"p{i}", _r.ReadSingle());
                break;

            case 0x82: // ArmyBin
                Hint("ArmyBin");
                _entry.AddString("Name", _r.ReadString32());
                break;

            case 0x84: // VoPath
                Hint("VoPath");
                _entry.AddString("Name", _r.ReadPChar());
                break;

            default:
                Debug.WriteLine($"[BinWorldReader] Unknown chunk 0x{chunkType:X2} at pos {_r.Position - 1}, aborting chunk loop");
                // Can't safely skip unknown chunks (unknown length), stop parsing
                _r.Position = _r.Length; // force HasMore to return false
                break;
        }
    }

    private void ReadTexture()
    {
        _entry.AddString("Name", _r.ReadString32());
        _entry.AddSingle("Stretch", _r.ReadSingle());
        _entry.AddSingle("Offset X", _r.ReadSingle());
        _entry.AddSingle("Offset Y", _r.ReadSingle());
    }

    private void ReadSunflare()
    {
        _entry.AddInt32("unknown", _r.ReadInt32());
        for (int i = 0; i < 11; i++)
            _entry.AddSingle($"p{i}", _r.ReadSingle());
    }

    private void ReadGenericMusic(string name)
    {
        Hint(name);
        _entry.AddString("p0", _r.ReadString32());
        _entry.AddString("p1", _r.ReadString32());
    }
}
