using System.Numerics;
using GiantsEdit.Core.DataModel;
using GiantsEdit.Core.Formats;
using GiantsEdit.Core.Rendering;

namespace GiantsEdit.Core.Services;

/// <summary>
/// Editing modes matching the original editor's toolbar buttons.
/// </summary>
public enum EditMode
{
    Camera,
    HeightEdit,
    LightPaint,
    TriangleEdit,
    ObjectEdit
}

public enum TriangleSubMode
{
    SetCorner,       // SpeedButton4: Paint/erase triangles by quadrant
    DiagDirection,   // SpeedButton5: Toggle diagonal direction (type 5↔6)
    DiagOptimize,    // SpeedButton6: Auto-pick diagonal from height
    OptCorner        // SpeedButton9: Auto-determine type from valid corners
}

/// <summary>
/// Manages the current world state: terrain, objects, tree data, and editing state.
/// This is the central service that connects file I/O, tree model, and rendering.
/// </summary>
public class WorldDocument
{
    private TreeNode? _worldRoot;
    private TerrainData? _terrain;
    private readonly List<TreeNode> _missions = [];
    private readonly List<(string Name, byte[] Data)> _otherGckFiles = [];

    public TreeNode? WorldRoot => _worldRoot;
    public TerrainData? Terrain => _terrain;
    public IReadOnlyList<TreeNode> Missions => _missions;

    // Editing state
    public EditMode CurrentMode { get; set; } = EditMode.Camera;
    public TriangleSubMode TriangleMode { get; set; } = TriangleSubMode.SetCorner;
    public float BrushRadius { get; set; } = 50f;
    public float BrushStrength { get; set; } = 0.5f;
    public float TargetHeight { get; set; } = 0f;
    public TreeNode? SelectedObject { get; set; }
    public bool IsModified { get; private set; }
    public string? FilePath { get; private set; }
    public string? TerrainPath { get; private set; }

    // GCK internal entry names (preserved across load/save)
    public string GckBinEntryName { get; set; } = "w_default.bin";
    public string GckGtiEntryName { get; set; } = "default.gti";
    public string GckGmmEntryName { get; set; } = "default.gmm";

    // Map metadata (matches Delphi 'map' record)
    public string MapBinName { get; set; } = string.Empty;
    public string UserMessage { get; set; } = string.Empty;
    public int MapType { get; set; } = -1;
    public bool Shareable { get; set; }

    public event Action? WorldChanged;
    public event Action? SelectionChanged;
    public event Action? TerrainChanged;

    /// <summary>Raises TerrainChanged for external callers.</summary>
    public void NotifyTerrainChanged() => TerrainChanged?.Invoke();

    public void NotifyWorldChanged()
    {
        IsModified = true;
        WorldChanged?.Invoke();
    }

    /// <summary>
    /// Loads a world .bin file and its associated terrain .gti.
    /// </summary>
    public void LoadWorld(string binPath)
    {
        byte[] binData = File.ReadAllBytes(binPath);
        var reader = new BinWorldReader();
        _worldRoot = reader.Load(binData);

        FilePath = binPath;
        MapBinName = Path.GetFileName(binPath);
        IsModified = false;

        // Try to load terrain if referenced in the world data
        var fileStart = _worldRoot?.FindChildNode("[FileStart]");
        var gtiLeaf = fileStart?.FindChildLeaf("GtiName");
        if (gtiLeaf != null)
        {
            string gtiName = gtiLeaf.StringValue;
            string dir = Path.GetDirectoryName(binPath) ?? ".";
            string gtiPath = Path.Combine(dir, gtiName);
            if (File.Exists(gtiPath))
                LoadTerrain(gtiPath);
        }

        // Extract metadata from tree
        ExtractMapMetadata();

        WorldChanged?.Invoke();
    }

    /// <summary>
    /// Loads a terrain .gti file.
    /// </summary>
    public void LoadTerrain(string gtiPath)
    {
        byte[] gtiData = File.ReadAllBytes(gtiPath);
        LoadTerrainFromBytes(gtiData);
        TerrainPath = gtiPath;
    }

    /// <summary>
    /// Loads terrain from raw GTI bytes (used by both file and GCK paths).
    /// </summary>
    private void LoadTerrainFromBytes(byte[] gtiData)
    {
        _terrain = GtiFormat.Load(gtiData);

        TerrainChanged?.Invoke();
    }

    /// <summary>
    /// Opens a .gck archive (ZIP containing w_*.bin + *.gti + optional *.gmm).
    /// </summary>
    public void LoadGck(string gckPath)
    {
        var entries = GzpArchive.ListEntries(gckPath);

        // Find the w_*.bin entry
        string? binEntry = entries.FirstOrDefault(e =>
            Path.GetFileName(e).StartsWith("w_", StringComparison.OrdinalIgnoreCase) &&
            e.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));

        // Find the *.gti entry
        string? gtiEntry = entries.FirstOrDefault(e =>
            e.EndsWith(".gti", StringComparison.OrdinalIgnoreCase));

        // Find the *.gmm entry
        string? gmmEntry = entries.FirstOrDefault(e =>
            e.EndsWith(".gmm", StringComparison.OrdinalIgnoreCase));

        if (binEntry != null)
        {
            byte[]? binData = GzpArchive.ExtractFile(gckPath, binEntry);
            if (binData != null)
            {
                var reader = new BinWorldReader();
                _worldRoot = reader.Load(binData);
            }
            GckBinEntryName = binEntry;
        }

        if (gtiEntry != null)
        {
            byte[]? gtiData = GzpArchive.ExtractFile(gckPath, gtiEntry);
            if (gtiData != null)
            {
                LoadTerrainFromBytes(gtiData);
            }
            GckGtiEntryName = gtiEntry;
        }

        if (gmmEntry != null)
            GckGmmEntryName = gmmEntry;

        // Preserve any extra files (e.g. .abx) for round-trip saving
        _otherGckFiles.Clear();
        foreach (var entry in entries)
        {
            if (entry == binEntry || entry == gtiEntry || entry == gmmEntry)
                continue;
            byte[]? data = GzpArchive.ExtractFile(gckPath, entry);
            if (data != null)
                _otherGckFiles.Add((entry, data));
        }

        FilePath = gckPath;
        MapBinName = binEntry != null ? Path.GetFileName(binEntry) : string.Empty;
        TerrainPath = null;
        IsModified = false;
        ExtractMapMetadata();
        WorldChanged?.Invoke();
    }

    /// <summary>
    /// Saves the current world. If the path is .gck, saves as a GCK archive
    /// (ZIP containing w_*.bin + *.gti + *.gmm). Otherwise saves raw .bin.
    /// </summary>
    public void SaveWorld(string? path = null)
    {
        if (_worldRoot == null) return;
        path ??= FilePath;
        if (path == null) return;

        if (path.EndsWith(".gck", StringComparison.OrdinalIgnoreCase))
        {
            SaveGck(path);
        }
        else
        {
            var writer = new BinWorldWriter();
            byte[] data = writer.Save(_worldRoot);
            File.WriteAllBytes(path, data);
        }

        FilePath = path;
        IsModified = false;
    }

    /// <summary>
    /// Saves the world as a .gck archive matching the original Delphi SaveMap behavior.
    /// </summary>
    private void SaveGck(string gckPath)
    {
        var files = new List<(string EntryName, byte[] Data)>();

        // Add GTI terrain data
        if (_terrain != null)
        {
            byte[] gtiData = GtiFormat.Save(_terrain);
            files.Add((GckGtiEntryName, gtiData));
        }

        // Add BIN world data
        if (_worldRoot != null)
        {
            var writer = new BinWorldWriter();
            byte[] binData = writer.Save(_worldRoot);
            files.Add((GckBinEntryName, binData));
        }

        // Build GMM metadata
        string gmmText = BuildGmmText();
        files.Add((GckGmmEntryName, System.Text.Encoding.ASCII.GetBytes(gmmText)));

        // Preserve any extra files from the original archive
        foreach (var (name, data) in _otherGckFiles)
            files.Add((name, data));

        GzpArchive.Create(gckPath, files);
    }

    /// <summary>
    /// Builds the GMM metadata text matching the original Delphi format.
    /// </summary>
    private string BuildGmmText()
    {
        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrEmpty(GckBinEntryName))
        {
            sb.AppendLine($"Modinfo_BinName={GckBinEntryName}");
            sb.AppendLine($"Modinfo_BinType={MapType}");
        }

        if (!string.IsNullOrEmpty(UserMessage))
            sb.AppendLine($"Modinfo_UserMessage={UserMessage}");

        if (Shareable)
            sb.AppendLine("Modinfo_Shareable=1");

        return sb.ToString();
    }

    /// <summary>
    /// Saves the terrain to a .gti file.
    /// </summary>
    public void SaveTerrain(string? path = null)
    {
        if (_terrain == null) return;
        path ??= TerrainPath;
        if (path == null) return;

        byte[] data = GtiFormat.Save(_terrain);
        File.WriteAllBytes(path, data);
        TerrainPath = path;
    }

    /// <summary>
    /// Loads a mission .bin file and adds it to the mission list.
    /// </summary>
    public void LoadMission(string missionPath)
    {
        byte[] data = File.ReadAllBytes(missionPath);
        var reader = new BinMissionReader();
        var mission = reader.Load(data);
        if (mission != null)
            _missions.Add(mission);
    }

    /// <summary>
    /// Creates a new empty world with the given terrain dimensions.
    /// </summary>
    public void NewWorld(int terrainWidth, int terrainHeight, string textureName = "")
    {
        _worldRoot = new TreeNode("Map data");
        var fs = _worldRoot.AddNode("[FileStart]");
        fs.AddString("Box", "newmap");
        fs.AddString("GtiName", "newmap.gti");

        _worldRoot.AddNode("Tiling");
        _worldRoot.AddNode("Fog");
        _worldRoot.AddNode("WaterFog");
        _worldRoot.AddNode("[textures]");
        _worldRoot.AddNode("[sfxlist]");
        _worldRoot.AddNode("[unknown]");
        _worldRoot.AddNode("[fx]");
        _worldRoot.AddNode("[scenerios]");
        _worldRoot.AddNode("[includefiles]");
        _worldRoot.AddNode("<Objects>");

        _terrain = GtiFormat.CreateNew(terrainWidth, terrainHeight, textureName);

        FilePath = null;
        TerrainPath = null;
        IsModified = true;
        WorldChanged?.Invoke();
        TerrainChanged?.Invoke();
    }

    /// <summary>
    /// Gets all world objects as render instances.
    /// </summary>
    public List<ObjectInstance> GetObjectInstances()
    {
        var result = new List<ObjectInstance>();
        var objNode = _worldRoot?.FindChildNode("<Objects>");
        if (objNode == null) return result;

        foreach (var obj in objNode.EnumerateNodes())
        {
            if (obj.Name != "Object") continue;

            var typeLeaf = obj.FindChildLeaf("Type");
            if (typeLeaf == null) continue;

            float x = obj.FindChildLeaf("X")?.SingleValue ?? 0;
            float y = obj.FindChildLeaf("Y")?.SingleValue ?? 0;
            float z = obj.FindChildLeaf("Z")?.SingleValue ?? 0;
            float scale = obj.FindChildLeaf("Scale")?.SingleValue ?? 1f;

            float dirFacing = obj.FindChildLeaf("Angle")?.SingleValue ?? 0;
            float tiltFwd = obj.FindChildLeaf("Tilt Forward")?.SingleValue ?? 0;
            float tiltLeft = obj.FindChildLeaf("Tilt Left")?.SingleValue ?? 0;

            int modelId = typeLeaf.Int32Value;

            // Remap type 679 markers by AIMode to special shapes (matches Delphi Draww)
            if (modelId == 679)
            {
                int aiMode = obj.FindChildLeaf("AIMode")?.ByteValue ?? 0;
                modelId = aiMode switch
                {
                    1 => -1, 2 => -2, 3 => -3, 4 => -4,
                    27 => -5, 22 => -6, 23 => -7,
                    _ => 679
                };
            }

            result.Add(new ObjectInstance
            {
                ModelId = modelId,
                Position = new Vector3(x, y, z),
                DirFacing = dirFacing,
                TiltForward = tiltFwd,
                TiltLeft = tiltLeft,
                Scale = scale,
                SourceNode = obj
            });
        }

        return result;
    }

    /// <summary>
    /// Builds spline line segments from waypoint objects (types 1052 and 1162).
    /// Matches the Delphi Draww spline logic: objects are grouped by (type base + AIMode),
    /// ordered by TeamID, and consecutive valid points are connected with lines.
    /// </summary>
    public List<SplineLine> GetSplineLines()
    {
        // splineids[0..511]: each slot is a list of points indexed by TeamID
        var splinePoints = new Dictionary<int, SortedDictionary<int, Vector3>>();

        void CollectFrom(TreeNode? root)
        {
            var objContainer = root?.FindChildNode("<Objects>");
            if (objContainer == null) return;

            foreach (var obj in objContainer.EnumerateNodes())
            {
                if (obj.Name != "Object") continue;
                int typeId = obj.FindChildLeaf("Type")?.Int32Value ?? 0;
                if (typeId != 1052 && typeId != 1162) continue;

                int groupBase = typeId == 1052 ? 0 : 256;
                int aiMode = obj.FindChildLeaf("AIMode")?.ByteValue ?? 0;
                int groupId = groupBase + aiMode;
                int teamId = obj.FindChildLeaf("TeamID")?.Int32Value ?? 0;

                float x = obj.FindChildLeaf("X")?.SingleValue ?? 0;
                float y = obj.FindChildLeaf("Y")?.SingleValue ?? 0;
                float z = obj.FindChildLeaf("Z")?.SingleValue ?? 0;

                if (!splinePoints.TryGetValue(groupId, out var points))
                {
                    points = new SortedDictionary<int, Vector3>();
                    splinePoints[groupId] = points;
                }
                points[teamId] = new Vector3(x, y, z);
            }
        }

        CollectFrom(_worldRoot);

        var result = new List<SplineLine>();

        // Build line segments for each color group
        BuildSplineGroup(splinePoints, 0, 255, new Vector3(1f, 1f, 1f), result);       // Type 1052: white
        BuildSplineGroup(splinePoints, 256, 511, new Vector3(1f, 0.75f, 0.5f), result); // Type 1162: orange

        return result;
    }

    private static void BuildSplineGroup(
        Dictionary<int, SortedDictionary<int, Vector3>> splinePoints,
        int startGroup, int endGroup, Vector3 color, List<SplineLine> result)
    {
        var verts = new List<float>();

        for (int g = startGroup; g <= endGroup; g++)
        {
            if (!splinePoints.TryGetValue(g, out var points)) continue;

            Vector3? prev = null;
            foreach (var (_, pos) in points)
            {
                if (prev.HasValue)
                {
                    verts.Add(prev.Value.X); verts.Add(prev.Value.Y); verts.Add(prev.Value.Z);
                    verts.Add(pos.X); verts.Add(pos.Y); verts.Add(pos.Z);
                }
                prev = pos;
            }
        }

        if (verts.Count > 0)
        {
            result.Add(new SplineLine
            {
                Vertices = verts.ToArray(),
                PointCount = verts.Count / 3,
                Color = color
            });
        }
    }
    public TerrainRenderData? BuildTerrainRenderData()
    {
        if (_terrain == null)
            return null;
        var data = TerrainMeshBuilder.Build(_terrain);
        return data;
    }

    /// <summary>
    /// Modifies terrain height at the given world coordinate using the current brush.
    /// </summary>
    public void PaintHeight(float worldX, float worldY, float heightDelta)
    {
        if (_terrain == null) return;

        int cx = (int)((worldX - _terrain.Header.XOffset) / _terrain.Header.Stretch);
        int cy = (int)((worldY - _terrain.Header.YOffset) / _terrain.Header.Stretch);
        float radiusCells = BrushRadius / _terrain.Header.Stretch;

        int r = (int)MathF.Ceiling(radiusCells);
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                int x = cx + dx;
                int y = cy + dy;
                if (x < 0 || x >= _terrain.Width || y < 0 || y >= _terrain.Height)
                    continue;

                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > radiusCells) continue;

                // Gaussian falloff matching original: exp(-d²/(r²/4))
                float factor = MathF.Exp(-(dist * dist) / (radiusCells * radiusCells / 4));
                _terrain.Heights[y * _terrain.Width + x] += heightDelta * factor * BrushStrength;
            }
        }

        IsModified = true;
        TerrainChanged?.Invoke();
    }

    /// <summary>
    /// Paints terrain lightmap at the given world coordinate.
    /// </summary>
    public void PaintLight(float worldX, float worldY, byte r, byte g, byte b)
    {
        if (_terrain == null) return;

        int cx = (int)((worldX - _terrain.Header.XOffset) / _terrain.Header.Stretch);
        int cy = (int)((worldY - _terrain.Header.YOffset) / _terrain.Header.Stretch);
        float radiusCells = BrushRadius / _terrain.Header.Stretch;

        int rad = (int)MathF.Ceiling(radiusCells);
        for (int dy = -rad; dy <= rad; dy++)
        {
            for (int dx = -rad; dx <= rad; dx++)
            {
                int x = cx + dx;
                int y = cy + dy;
                if (x < 0 || x >= _terrain.Width || y < 0 || y >= _terrain.Height)
                    continue;

                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > radiusCells) continue;

                float factor = MathF.Exp(-(dist * dist) / (radiusCells * radiusCells / 4));
                int idx = (y * _terrain.Width + x) * 3;
                _terrain.LightMap[idx + 0] = (byte)(_terrain.LightMap[idx + 0] + (r - _terrain.LightMap[idx + 0]) * factor * BrushStrength);
                _terrain.LightMap[idx + 1] = (byte)(_terrain.LightMap[idx + 1] + (g - _terrain.LightMap[idx + 1]) * factor * BrushStrength);
                _terrain.LightMap[idx + 2] = (byte)(_terrain.LightMap[idx + 2] + (b - _terrain.LightMap[idx + 2]) * factor * BrushStrength);
            }
        }

        IsModified = true;
        TerrainChanged?.Invoke();
    }

    /// <summary>
    /// Sets the triangle type at the given terrain cell.
    /// </summary>
    public void SetTriangleType(float worldX, float worldY, byte triType)
    {
        if (_terrain == null) return;

        int cx = (int)((worldX - _terrain.Header.XOffset) / _terrain.Header.Stretch);
        int cy = (int)((worldY - _terrain.Header.YOffset) / _terrain.Header.Stretch);

        if (cx >= 0 && cx < _terrain.Width && cy >= 0 && cy < _terrain.Height)
        {
            _terrain.Triangles[cy * _terrain.Width + cx] = triType;
            IsModified = true;
            TerrainChanged?.Invoke();
        }
    }

    /// <summary>
    /// Adds a new object to the world.
    /// </summary>
    public TreeNode? AddObject(int typeId, float x, float y, float z, float angle = 0f)
    {
        var objContainer = _worldRoot?.FindChildNode("<Objects>");
        if (objContainer == null) return null;

        var obj = objContainer.AddNode("Object");
        obj.AddInt32("Type", typeId);
        obj.AddSingle("X", x);
        obj.AddSingle("Y", y);
        obj.AddSingle("Z", z);
        obj.AddSingle("Angle", angle);

        IsModified = true;
        WorldChanged?.Invoke();
        return obj;
    }

    /// <summary>
    /// Removes the selected object from the world.
    /// </summary>
    public void RemoveSelectedObject()
    {
        if (SelectedObject == null || _worldRoot == null) return;

        var objContainer = _worldRoot.FindChildNode("<Objects>");
        objContainer?.RemoveNode(SelectedObject);
        SelectedObject = null;
        IsModified = true;
        WorldChanged?.Invoke();
        SelectionChanged?.Invoke();
    }

    public void SelectObject(TreeNode? obj)
    {
        SelectedObject = obj;
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Extracts map metadata (user message, map type, shareable) from the world tree.
    /// The Delphi code reads these from special leaf nodes during BIN loading.
    /// </summary>
    private void ExtractMapMetadata()
    {
        if (_worldRoot == null) return;

        // Look for GmmData node which contains user message, map type, shareable
        var gmmData = _worldRoot.FindChildNode("GmmData");
        if (gmmData != null)
        {
            UserMessage = gmmData.FindChildLeaf("UserMessage")?.StringValue ?? string.Empty;
            MapType = gmmData.FindChildLeaf("MapType")?.Int32Value ?? -1;
            Shareable = (gmmData.FindChildLeaf("Shareable")?.Int32Value ?? 0) != 0;
        }
    }

    /// <summary>
    /// Gets marker objects (Type=679) with their AIMode and TeamID for the marker report.
    /// </summary>
    public List<(int AIMode, int TeamID)> GetMarkers()
    {
        var markers = new List<(int, int)>();
        if (_worldRoot == null) return markers;

        foreach (var node in _worldRoot.EnumerateNodes())
        {
            if (node.Name != "Object") continue;
            int type = node.FindChildLeaf("Type")?.Int32Value ?? 0;
            if (type != 679) continue;

            int aiMode = node.FindChildLeaf("AIMode")?.ByteValue ?? -1;
            int teamId = node.FindChildLeaf("TeamID")?.Int32Value ?? -1;
            markers.Add((aiMode, teamId));
        }

        return markers;
    }

    /// <summary>
    /// Adds a new empty mission.
    /// </summary>
    public TreeNode AddMission(string? name = null)
    {
        name ??= $"wm_defaultmission_{RandomChars(4)}";
        var mission = new TreeNode(name);
        mission.AddNode("<Objects>");
        var options = mission.AddNode("<Options>");
        options.AddInt32("NoJetpack", 0);
        options.AddInt32("NoNitro", 0);
        options.AddInt32("Character 0", 0);
        options.AddInt32("Character 1", 0);
        options.AddInt32("Character 2", 0);
        options.AddInt32("Character 3", 0);
        options.AddInt32("Icons", 0);
        _missions.Add(mission);
        IsModified = true;
        return mission;
    }

    /// <summary>
    /// Removes a mission by index.
    /// </summary>
    public void RemoveMission(int index)
    {
        if (index >= 0 && index < _missions.Count)
        {
            _missions.RemoveAt(index);
            IsModified = true;
        }
    }

    private static string RandomChars(int count)
    {
        var rng = Random.Shared;
        var chars = new char[count];
        for (int i = 0; i < count; i++)
            chars[i] = (char)('a' + rng.Next(26));
        return new string(chars);
    }

    /// <summary>
    /// Generates an export report listing which tree sections exist and which were
    /// not written by the BIN writer (matching Delphi ScanDone behavior).
    /// </summary>
    public string GetExportReport()
    {
        if (_worldRoot == null) return string.Empty;

        // Known sections that the writer handles
        var handledSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "[FileStart]", "<Objects>", "<PreObjects>", "[textures]",
            "[fx]", "[scenerios]", "[includefiles]", "[sfxlist]", "[unknown]"
        };

        var unhandled = new List<string>();
        foreach (var node in _worldRoot.EnumerateNodes())
        {
            if (!handledSections.Contains(node.Name))
                unhandled.Add(node.Name);
        }

        if (unhandled.Count == 0) return string.Empty;

        return "ScanNode debug report\nThe following nodes were not scanned:\n" +
               string.Join("\n", unhandled.Select(n => $"Untouched: {n}"));
    }
}
