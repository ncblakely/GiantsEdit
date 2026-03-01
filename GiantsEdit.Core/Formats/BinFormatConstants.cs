using System.Collections.Frozen;

namespace GiantsEdit.Core.Formats;

/// <summary>
/// Shared constants for the .bin world map binary format,
/// used by both <see cref="BinWorldReader"/> and <see cref="BinWorldWriter"/>.
/// </summary>
public static class BinFormatConstants
{
    public const int Magic = 0x1A0002E5;
    public const int HeaderSize = 32;
    public const byte EndMarker = 0xFF;

    // Section names
    public const string SectionFileStart = "[FileStart]";
    public const string SectionTextures = "[textures]";
    public const string SectionSfx = "[sfx]";
    public const string SectionObjDefs = "[objdefs]";
    public const string SectionFx = "[fx]";
    public const string SectionScenerios = "[scenerios]";
    public const string SectionIncludeFiles = "[includefiles]";

    // Group names
    public const string GroupObjects = "<Objects>";
    public const string GroupTextures = "<Textures>";
    public const string GroupTeleports = "<Teleports>";
    public const string GroupStartLocs = "<StartLocs>";
    public const string GroupSun = "<Sun>";
    public const string GroupFlicks = "<Flicks>";
    public const string GroupPreObjects = "<PreObjects>";
    public const string GroupMissions = "<Missions>";
    public const string GroupScenerios = "<Scenerios>";

    // Node names — root-level properties
    public const string NodeTiling = "Tiling";
    public const string NodeSeaSpeed = "SeaSpeed";
    public const string NodeFog = "Fog";
    public const string NodeWaterFog = "WaterFog";
    public const string NodeStartWeather = "StartWeather";
    public const string NodeObjEditStart = "ObjEditStart";
    public const string NodeObjEditEnd = "ObjEditEnd";
    public const string NodeBumpClampValue = "BumpClampValue";
    public const string NodeNoScenerios = "NoScenerios";
    public const string NodeLandAngles = "LandAngles";
    public const string NodeLandTexFade = "LandTexFade";
    public const string NodeWaterColor = "WaterColor";
    public const string NodeScenario = "Scenario";
    public const string NodeMusic = "Music";
    public const string NodeAmbient = "Ambient";
    public const string NodeMultiAmbient = "MultiAmbient";
    public const string NodeArmyBin = "ArmyBin";
    public const string NodeVoPath = "VoPath";
    public const string NodeAmbientColor = "AmbientColor";
    public const string NodeBlendWater = "BlendWater";
    public const string NodeWaterMaterial = "WaterMaterial";
    public const string NodeWorldGrid = "WorldGrid";
    public const string NodeWorldNoLighting = "WorldNoLighting";
    public const string NodeMusicSuspense = "MusicSuspense";
    public const string NodeMusicLight = "MusicLight";
    public const string NodeMusicWin = "MusicWin";
    public const string NodeMusicHeavy = "MusicHeavy";
    public const string NodeMusicFailure = "MusicFailure";
    public const string NodeMusicSuccess = "MusicSuccess";

    // Node names — group children
    public const string NodeTeleport = "Teleport";
    public const string NodeStartLoc = "StartLoc";
    public const string NodeSunColor = "SunColor";
    public const string NodeSunFxName = "SunFxName";
    public const string NodeSunflare1 = "Sunflare1";
    public const string NodeSunflare2 = "Sunflare2";
    public const string NodeObject = "Object";
    public const string NodeSmokeGen = "SmokeGen";
    public const string NodeAreaAlien = "AreaAlien";
    public const string NodeScenerio = "Scenerio";
    public const string NodeFlick = "Flick";

    // Node names — 44-byte texture entries (name32 + wrap + offsetX + offsetY)
    public const string NodeGroundTexture = "GroundTexture";
    public const string NodeSlopeTexture = "SlopeTexture";
    public const string NodeWallTexture = "WallTexture";
    public const string NodeGroundBumpTexture = "GroundBumpTexture";
    public const string NodeSlopeBumpTexture = "SlopeBumpTexture";
    public const string NodeWallBumpTexture = "WallBumpTexture";
    public const string NodeGroundDetailTexture = "GroundDetailTexture";
    public const string NodeSlopeDetailTexture = "SlopeDetailTexture";
    public const string NodeWallDetailTexture = "WallDetailTexture";
    public const string NodeGroundNormalTexture = "GroundNormalTexture";
    public const string NodeSlopeNormalTexture = "SlopeNormalTexture";
    public const string NodeWallNormalTexture = "WallNormalTexture";

    // Leaf names — 16-byte texture entries (name16)
    public const string LeafOutDomeTex = "OutDomeTex";
    public const string LeafDomeTex = "DomeTex";
    public const string LeafDomeEdgeTex = "DomeEdgeTex";
    public const string LeafWFall1Tex = "WFall1Tex";
    public const string LeafWFall2Tex = "WFall2Tex";
    public const string LeafWFall3Tex = "WFall3Tex";
    public const string LeafSpaceLineTex = "SpaceLineTex";
    public const string LeafSpaceTex = "SpaceTex";
    public const string LeafSeaTex = "SeaTex";
    public const string LeafGlowTex = "GlowTex";

    // Opcodes — object attributes
    public const byte OpAnimType = 0x13;
    public const byte OpScale = 0x17;
    public const byte OpAnimTime = 0x2B;
    public const byte OpHerdCount = 0x32;
    public const byte OpAIMode = 0x3B;
    public const byte OpHerdScale = 0x3F;
    public const byte OpAreaAlienScale = 0x41;
    public const byte OpTeamId = 0x52;
    public const byte OpHerdType = 0x53;
    public const byte OpOData = 0x5A;
    public const byte OpHerdMarkers = 0x5B;
    public const byte OpLightColor = 0x5F;
    public const byte OpSplineScale = 0x61;
    public const byte OpSplineTangents = 0x62;
    public const byte OpSplinePath3D = 0x63;
    public const byte OpSplineJet = 0x64;
    public const byte OpMinishopRIcons = 0x67;
    public const byte OpMinishopMIcons = 0x68;
    public const byte OpSplineStartId = 0x6A;
    public const byte OpSplineKeyTime = 0x6B;
    public const byte OpFlickUsed = 0x4E;
    public const byte OpAIData = 0x50;
    public const byte OpHerdPoint = 0x48;
    public const byte OpPath = 0x2D;
    public const byte OpGroundPath = 0x3A;
    public const byte OpWind = 0x54;
    public const byte OpLockStart = 0x4C;
    public const byte OpLockEnd = 0x4D;

    // Opcodes — objects
    public const byte OpObjectRef = 0x2A;
    public const byte OpObjectRef6 = 0x46;
    public const byte OpSmokeGen = 0x42;
    public const byte OpAreaAlien = 0x40;
    public const byte OpObjEditStart = 0x44;
    public const byte OpObjEditEnd = 0x45;
    public const byte OpDirections = 0x51;

    // Opcodes — textures
    public const byte OpOutDomeTex = 0x1D;
    public const byte OpDomeTex = 0x1E;
    public const byte OpDomeEdgeTex = 0x1F;
    public const byte OpWFall1Tex = 0x20;
    public const byte OpWFall2Tex = 0x21;
    public const byte OpWFall3Tex = 0x22;
    public const byte OpSpaceLineTex = 0x23;
    public const byte OpSpaceTex = 0x24;
    public const byte OpSeaTex = 0x25;
    public const byte OpGlowTex = 0x26;
    public const byte OpGroundTexture = 0x34;
    public const byte OpSlopeTexture = 0x35;
    public const byte OpWallTexture = 0x36;
    public const byte OpGroundBumpTexture = 0x72;
    public const byte OpSlopeBumpTexture = 0x73;
    public const byte OpWallBumpTexture = 0x74;
    public const byte OpGroundDetailTexture = 0x85;
    public const byte OpSlopeDetailTexture = 0x86;
    public const byte OpWallDetailTexture = 0x87;
    public const byte OpGroundNormalTexture = 0x8C;
    public const byte OpSlopeNormalTexture = 0x8D;
    public const byte OpWallNormalTexture = 0x8E;

    // Opcodes — world/map properties
    public const byte OpTeleport = 0x27;
    public const byte OpSunColor = 0x28;
    public const byte OpFog = 0x29;
    public const byte OpMusic = 0x2C;
    public const byte OpSeaSpeed = 0x2E;
    public const byte OpStartLoc = 0x30;
    public const byte OpTiling = 0x39;
    public const byte OpWorldGrid = 0x47;
    public const byte OpScenario = 0x49;
    public const byte OpWorldNoLighting = 0x4A;
    public const byte OpFlick = 0x4F;
    public const byte OpWaterFog = 0x58;
    public const byte OpStartWeather = 0x59;
    public const byte OpScenerio = 0x5C;
    public const byte OpMission = 0x5D;
    public const byte OpAmbient = 0x5E;
    public const byte OpNoScenerios = 0x60;
    public const byte OpLandTexFade = 0x65;
    public const byte OpLandAngles = 0x66;
    public const byte OpBumpClampValue = 0x75;
    public const byte OpSunFxName = 0x76;
    public const byte OpSunflare1 = 0x77;
    public const byte OpSunflare2 = 0x78;
    public const byte OpWaterColor = 0x79;
    public const byte OpMultiAmbient = 0x81;
    public const byte OpArmyBin = 0x82;
    public const byte OpVoPath = 0x84;
    public const byte OpAmbientColor = 0x8B;
    public const byte OpBlendWater = 0x8F;
    public const byte OpWaterMaterial = 0x90;

    // Opcodes — music
    public const byte OpMusicSuspense = 0x6C;
    public const byte OpMusicLight = 0x6D;
    public const byte OpMusicWin = 0x6E;
    public const byte OpMusicHeavy = 0x6F;
    public const byte OpMusicFailure = 0x70;
    public const byte OpMusicSuccess = 0x71;

    /// <summary>
    /// Maps opcode → node name for 44-byte texture entries (name32 + wrap + offsetX + offsetY).
    /// </summary>
    public static readonly FrozenDictionary<byte, string> NodeTextureOpcodeToName = new Dictionary<byte, string>
    {
        [OpGroundTexture] = NodeGroundTexture,
        [OpSlopeTexture] = NodeSlopeTexture,
        [OpWallTexture] = NodeWallTexture,
        [OpGroundBumpTexture] = NodeGroundBumpTexture,
        [OpSlopeBumpTexture] = NodeSlopeBumpTexture,
        [OpWallBumpTexture] = NodeWallBumpTexture,
        [OpGroundDetailTexture] = NodeGroundDetailTexture,
        [OpSlopeDetailTexture] = NodeSlopeDetailTexture,
        [OpWallDetailTexture] = NodeWallDetailTexture,
        [OpGroundNormalTexture] = NodeGroundNormalTexture,
        [OpSlopeNormalTexture] = NodeSlopeNormalTexture,
        [OpWallNormalTexture] = NodeWallNormalTexture,
    }.ToFrozenDictionary();

    /// <summary>
    /// Maps node name → opcode for 44-byte texture entries.
    /// </summary>
    public static readonly FrozenDictionary<string, byte> NodeTextureNameToOpcode =
        NodeTextureOpcodeToName.ToFrozenDictionary(kv => kv.Value, kv => kv.Key);

    /// <summary>
    /// Maps opcode → leaf name for 16-byte texture entries (name16).
    /// </summary>
    public static readonly FrozenDictionary<byte, string> LeafTextureOpcodeToName = new Dictionary<byte, string>
    {
        [OpOutDomeTex] = LeafOutDomeTex,
        [OpDomeTex] = LeafDomeTex,
        [OpDomeEdgeTex] = LeafDomeEdgeTex,
        [OpWFall1Tex] = LeafWFall1Tex,
        [OpWFall2Tex] = LeafWFall2Tex,
        [OpWFall3Tex] = LeafWFall3Tex,
        [OpSpaceLineTex] = LeafSpaceLineTex,
        [OpSpaceTex] = LeafSpaceTex,
        [OpSeaTex] = LeafSeaTex,
        [OpGlowTex] = LeafGlowTex,
    }.ToFrozenDictionary();

    /// <summary>
    /// Maps leaf name → opcode for 16-byte texture entries.
    /// </summary>
    public static readonly FrozenDictionary<string, byte> LeafTextureNameToOpcode =
        LeafTextureOpcodeToName.ToFrozenDictionary(kv => kv.Value, kv => kv.Key);
}
