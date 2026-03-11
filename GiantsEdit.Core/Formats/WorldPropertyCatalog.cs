using GiantsEdit.Core.DataModel;

namespace GiantsEdit.Core.Formats;

/// <summary>
/// Describes a world property that can be inserted into a map via the editor.
/// </summary>
/// <param name="Name">Property name (matches tree node or leaf name)</param>
/// <param name="GroupName">Parent group node name, or null for root-level</param>
/// <param name="IsRepeatable">True if multiple instances are allowed</param>
/// <param name="IsLeafOnGroup">True if this is a leaf directly on the group (not a child node)</param>
/// <param name="CreateDefaults">Populates default leaves on the target node</param>
public record InsertableProperty(
    string Name,
    string? GroupName,
    bool IsRepeatable,
    bool IsLeafOnGroup,
    Action<TreeNode> CreateDefaults);

/// <summary>
/// Static catalog of all world properties that can be inserted into a map.
/// Each entry knows how to create default leaf values on a target node.
/// </summary>
public static class WorldPropertyCatalog
{
    public static IReadOnlyList<InsertableProperty> Properties { get; } = Build();

    /// <summary>
    /// Inserts a property into the world tree with default values.
    /// Returns the created node, or null if a singleton already exists.
    /// </summary>
    public static TreeNode? Insert(InsertableProperty prop, TreeNode root)
    {
        TreeNode parent;
        if (prop.GroupName != null)
        {
            parent = root.FindChildNode(prop.GroupName) ?? root.AddNode(prop.GroupName);
        }
        else
        {
            parent = root;
        }

        if (prop.IsLeafOnGroup)
        {
            // Singleton check: leaf with same name already on parent
            if (!prop.IsRepeatable && parent.FindChildLeaf(prop.Name) != null)
                return null;
            prop.CreateDefaults(parent);
            return parent;
        }

        // Singleton check: child node with same name already on parent
        if (!prop.IsRepeatable && parent.FindChildNode(prop.Name) != null)
            return null;

        var node = parent.AddNode(prop.Name);
        prop.CreateDefaults(node);
        return node;
    }

    /// <summary>
    /// Returns names of properties that already exist in the tree (for filtering singletons).
    /// </summary>
    public static HashSet<string> GetExistingPropertyNames(TreeNode root)
    {
        var existing = new HashSet<string>();
        foreach (var prop in Properties)
        {
            if (prop.IsRepeatable) continue;

            TreeNode parent;
            if (prop.GroupName != null)
            {
                parent = root.FindChildNode(prop.GroupName)!;
                if (parent == null) continue;
            }
            else
            {
                parent = root;
            }

            if (prop.IsLeafOnGroup)
            {
                if (parent.FindChildLeaf(prop.Name) != null)
                    existing.Add(prop.Name);
            }
            else
            {
                if (parent.FindChildNode(prop.Name) != null)
                    existing.Add(prop.Name);
            }
        }
        return existing;
    }

    private static List<InsertableProperty> Build()
    {
        return
        [
            // --- Textures (string16 leaves directly on <Textures> group) ---
            Tex("DomeTex"),
            Tex("OutDomeTex"),
            Tex("DomeEdgeTex"),
            Tex("WFall1Tex"),
            Tex("WFall2Tex"),
            Tex("WFall3Tex"),
            Tex("SpaceLineTex"),
            Tex("SpaceTex"),
            Tex("SeaTex"),
            Tex("GlowTex"),

            // --- Terrain textures (child nodes under <Textures>) ---
            TexNode("GroundTexture"),
            TexNode("SlopeTexture"),
            TexNode("WallTexture"),
            TexNode("GroundBumpTexture"),
            TexNode("SlopeBumpTexture"),
            TexNode("WallBumpTexture"),
            TexNode("GroundDetailTexture"),
            TexNode("SlopeDetailTexture"),
            TexNode("WallDetailTexture"),
            TexNode("GroundNormalTexture"),
            TexNode("SlopeNormalTexture"),
            TexNode("WallNormalTexture"),

            // --- Sun ---
            Prop("SunColor", BinFormatConstants.GroupSun, n =>
            {
                n.AddByte("Red", 255);
                n.AddByte("Green", 255);
                n.AddByte("Blue", 255);
            }),
            Prop("SunFxName", BinFormatConstants.GroupSun, n =>
            {
                n.AddString("Name", "");
                n.AddSingle("ColorR", 1f);
                n.AddSingle("ColorG", 1f);
                n.AddSingle("ColorB", 1f);
                n.AddSingle("Exponent", 1f);
                n.AddSingle("Factor", 1f);
            }),
            Prop("Sunflare1", BinFormatConstants.GroupSun, Sunflare),
            Prop("Sunflare2", BinFormatConstants.GroupSun, Sunflare),

            // --- Fog ---
            Prop("Fog", null, n =>
            {
                n.AddSingle("FogMin", 1000f);
                n.AddSingle("FogMax", 10000f);
                n.AddByte("Red", 128);
                n.AddByte("Green", 128);
                n.AddByte("Blue", 128);
            }),
            Prop("WaterFog", null, n =>
            {
                n.AddSingle("FogMin", 100f);
                n.AddSingle("FogMax", 2000f);
                n.AddByte("Red", 0);
                n.AddByte("Green", 64);
                n.AddByte("Blue", 128);
            }),

            // --- Colors ---
            Prop("AmbientColor", null, n =>
            {
                n.AddSingle("R", 0.3f);
                n.AddSingle("G", 0.3f);
                n.AddSingle("B", 0.3f);
            }),
            Prop("WaterColor", null, n =>
            {
                n.AddSingle("R", 0f); n.AddSingle("G", 0.2f); n.AddSingle("B", 0.4f);
                n.AddSingle("R1", 0f); n.AddSingle("G1", 0.2f); n.AddSingle("B1", 0.4f);
                n.AddSingle("R2", 0f); n.AddSingle("G2", 0.2f); n.AddSingle("B2", 0.4f);
                n.AddSingle("R3", 0f); n.AddSingle("G3", 0.2f); n.AddSingle("B3", 0.4f);
                n.AddSingle("R4", 0f); n.AddSingle("G4", 0.2f); n.AddSingle("B4", 0.4f);
                n.AddSingle("ReflectionR", 0.5f);
                n.AddSingle("ReflectionG", 0.5f);
                n.AddSingle("ReflectionB", 0.5f);
            }),
            Prop("MultiAmbient", null, n => n.AddInt32("Value", 0)),

            // --- World settings ---
            Prop("SeaSpeed", null, n =>
            {
                n.AddSingle("Cycle", 1f);
                n.AddSingle("Speed", 1f);
                n.AddSingle("Trans", 0.5f);
            }),
            Prop("Tiling", null, n =>
            {
                n.AddSingle("Obsolete0", 0f);
                n.AddSingle("Obsolete1", 0f);
                n.AddSingle("Obsolete2", 0f);
                n.AddSingle("MixNear", 0f);
                n.AddSingle("MixFar", 5000f);
                n.AddSingle("MixNearBlend", 0f);
                n.AddSingle("MixFarBlend", 1000f);
            }),
            Prop("WorldGrid", null, n =>
            {
                n.AddSingle("GridStep", 500f);
                n.AddSingle("GridMinX", -12500f);
                n.AddSingle("GridMaxX", 12500f);
                n.AddSingle("GridMinY", -12500f);
                n.AddSingle("GridMaxY", 12500f);
            }),
            Prop("LandTexFade", null, n =>
            {
                n.AddSingle("Falloff0", 1000f);
                n.AddSingle("Falloff1", 2000f);
                n.AddSingle("Falloff2", 3000f);
            }),
            Prop("LandAngles", null, n =>
            {
                n.AddSingle("SlopeAngle", 45f);
                n.AddSingle("WallAngle", 70f);
            }),
            Prop("LightClampValue", null, n => n.AddSingle("Value", 1f)),
            Prop("NormalMapInfluence", null, n => n.AddSingle("Value", 0.5f)),
            Prop("NormalMapStrength", null, n => n.AddSingle("Value", 3f)),
            Prop("AOStrength", null, n => n.AddSingle("Value", 0f)),
            Prop("Scenario", null, n => n.AddInt32("Value", 0)),
            Prop("NoScenerios", null, n => n.AddInt32("Value", 1)),
            Prop("WorldNoLighting", null, _ => { }),
            Prop("BlendWater", null, n =>
            {
                n.AddSingle("FogScale", 1f);
                n.AddInt32("RenderFog", 1);
            }),
            Prop("WaterMaterial", null, n =>
            {
                n.AddSingle("DiffuseR", 1f);
                n.AddSingle("DiffuseG", 1f);
                n.AddSingle("DiffuseB", 1f);
                n.AddSingle("DiffuseA", 1f);
                n.AddSingle("Power", 1f);
            }),

            // --- Teleport / StartLoc (repeatable, under groups) ---
            new("Teleport", BinFormatConstants.GroupTeleports, true, false, n =>
            {
                n.AddByte("Index", 0);
                n.AddSingle("X", 0f);
                n.AddSingle("Y", 0f);
                n.AddSingle("Z", 0f);
                n.AddSingle("DirFacing", 0f);
            }),
            new("StartLoc", BinFormatConstants.GroupStartLocs, true, false, n =>
            {
                n.AddByte("Type", 0);
                n.AddByte("StartNumber", 0);
                n.AddSingle("X", 0f);
                n.AddSingle("Y", 0f);
                n.AddSingle("Z", 0f);
                n.AddSingle("DirFacing", 0f);
            }),

            // --- Music (root-level nodes) ---
            Prop("Music", null, n => n.AddString("Name", "")),
            Prop("MusicSuspense", null, n =>
            {
                n.AddString("Track1", "");
                n.AddString("Track2", "");
            }),
            Prop("MusicLight", null, n =>
            {
                n.AddString("Track1", "");
                n.AddString("Track2", "");
            }),
            Prop("MusicWin", null, n =>
            {
                n.AddString("Track1", "");
                n.AddString("Track2", "");
            }),
            Prop("MusicHeavy", null, n =>
            {
                n.AddString("Track1", "");
                n.AddString("Track2", "");
            }),
            Prop("MusicFailure", null, n => n.AddString("Track", "")),
            Prop("MusicSuccess", null, n => n.AddString("Track", "")),

            // --- Audio ---
            Prop("Ambient", null, n => n.AddString("Name", "")),
            Prop("VoPath", null, n => n.AddString("Name", "")),

            // --- Misc ---
            Prop("StartWeather", null, n => n.AddString("Name", "")),
            Prop("ArmyBin", null, n => n.AddString("Name", "")),
            new("Flick", BinFormatConstants.GroupFlicks, true, false, n =>
            {
                n.AddString("Name", "");
                n.AddString("Trigger", "");
            }),
            new("Scenerio", BinFormatConstants.GroupScenerios, true, false, n =>
            {
                n.AddByte("Type", 0);
                n.AddInt32("TriggersNeeded", 0);
                n.AddString("Name", "");
            }),
        ];
    }

    /// <summary>Simple texture leaf directly on group node.</summary>
    private static InsertableProperty Tex(string name) =>
        new(name, BinFormatConstants.GroupTextures, false, true, n => n.AddStringL(name, "", 16));

    /// <summary>Complex texture child node under group.</summary>
    private static InsertableProperty TexNode(string name) =>
        new(name, BinFormatConstants.GroupTextures, false, false, n =>
        {
            n.AddString("Name", "");
            n.AddSingle("Wrap", 1f);
            n.AddSingle("OffsetX", 0f);
            n.AddSingle("OffsetY", 0f);
        });

    /// <summary>Singleton property that creates a child node (on group or root).</summary>
    private static InsertableProperty Prop(string name, string? group, Action<TreeNode> defaults) =>
        new(name, group, false, false, defaults);

    private static void Sunflare(TreeNode n)
    {
        n.AddInt32("Type", 0);
        n.AddSingle("Base0", 1f);
        n.AddSingle("Exponent0", 1f);
        n.AddSingle("Factor0", 1f);
        n.AddSingle("Damping0", 0f);
        n.AddSingle("Base1", 1f);
        n.AddSingle("Exponent1", 1f);
        n.AddSingle("Factor1", 1f);
        n.AddSingle("Damping1", 0f);
        n.AddSingle("OscillationAmplitude", 0f);
        n.AddSingle("OscillationFrequency", 0f);
        n.AddSingle("SpinFrequency", 0f);
    }
}
