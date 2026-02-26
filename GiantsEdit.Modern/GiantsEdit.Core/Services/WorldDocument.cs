using System.Diagnostics;
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

/// <summary>
/// Manages the current world state: terrain, objects, tree data, and editing state.
/// This is the central service that connects file I/O, tree model, and rendering.
/// </summary>
public class WorldDocument
{
    private TreeNode? _worldRoot;
    private TerrainData? _terrain;
    private readonly List<TreeNode> _missions = [];

    public TreeNode? WorldRoot => _worldRoot;
    public TerrainData? Terrain => _terrain;
    public IReadOnlyList<TreeNode> Missions => _missions;

    // Editing state
    public EditMode CurrentMode { get; set; } = EditMode.Camera;
    public float BrushRadius { get; set; } = 50f;
    public float BrushStrength { get; set; } = 0.5f;
    public TreeNode? SelectedObject { get; set; }
    public bool IsModified { get; private set; }
    public string? FilePath { get; private set; }
    public string? TerrainPath { get; private set; }

    public event Action? WorldChanged;
    public event Action? SelectionChanged;
    public event Action? TerrainChanged;

    /// <summary>
    /// Loads a world .bin file and its associated terrain .gti.
    /// </summary>
    public void LoadWorld(string binPath)
    {
        Debug.WriteLine($"[LoadWorld] Loading: {binPath}");
        byte[] binData = File.ReadAllBytes(binPath);
        var reader = new BinWorldReader();
        _worldRoot = reader.Load(binData);
        Debug.WriteLine($"[LoadWorld] Root node: {_worldRoot?.Name}, children: {_worldRoot?.NodeCount ?? 0}");

        FilePath = binPath;
        IsModified = false;

        // Try to load terrain if referenced in the world data
        var fileStart = _worldRoot?.FindChildNode("[FileStart]");
        var gtiLeaf = fileStart?.FindChildLeaf("GtiName");
        Debug.WriteLine($"[LoadWorld] GtiName leaf: {gtiLeaf?.StringValue ?? "(null)"}");
        if (gtiLeaf != null)
        {
            string gtiName = gtiLeaf.StringValue;
            string dir = Path.GetDirectoryName(binPath) ?? ".";
            string gtiPath = Path.Combine(dir, gtiName);
            Debug.WriteLine($"[LoadWorld] Looking for GTI at: {gtiPath}, exists={File.Exists(gtiPath)}");
            if (File.Exists(gtiPath))
                LoadTerrain(gtiPath);
        }

        WorldChanged?.Invoke();
    }

    /// <summary>
    /// Loads a terrain .gti file.
    /// </summary>
    public void LoadTerrain(string gtiPath)
    {
        Debug.WriteLine($"[LoadTerrain] Loading: {gtiPath} ({new FileInfo(gtiPath).Length} bytes)");
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
        Debug.WriteLine($"[LoadTerrain] Terrain {_terrain.Width}x{_terrain.Height}, stretch={_terrain.Header.Stretch}, offset=({_terrain.Header.XOffset},{_terrain.Header.YOffset})");

        // Log triangle type distribution for diagnostics
        var triCounts = new int[8];
        foreach (var t in _terrain.Triangles) triCounts[t & 7]++;
        Debug.WriteLine($"[LoadTerrain] Triangle types: 0(empty)={triCounts[0]} 1={triCounts[1]} 2={triCounts[2]} 3={triCounts[3]} 4={triCounts[4]} 5={triCounts[5]} 6={triCounts[6]} 7={triCounts[7]}");

        TerrainChanged?.Invoke();
    }

    /// <summary>
    /// Opens a .gck archive (ZIP containing w_*.bin + *.gti + optional *.gmm).
    /// </summary>
    public void LoadGck(string gckPath)
    {
        Debug.WriteLine($"[LoadGck] Opening: {gckPath}");

        var entries = GzpArchive.ListEntries(gckPath);
        Debug.WriteLine($"[LoadGck] Archive contains {entries.Count} entries: {string.Join(", ", entries)}");

        // Find the w_*.bin entry
        string? binEntry = entries.FirstOrDefault(e =>
            Path.GetFileName(e).StartsWith("w_", StringComparison.OrdinalIgnoreCase) &&
            e.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));

        // Find the *.gti entry
        string? gtiEntry = entries.FirstOrDefault(e =>
            e.EndsWith(".gti", StringComparison.OrdinalIgnoreCase));

        Debug.WriteLine($"[LoadGck] BIN entry: {binEntry ?? "(none)"}, GTI entry: {gtiEntry ?? "(none)"}");

        if (binEntry != null)
        {
            byte[]? binData = GzpArchive.ExtractFile(gckPath, binEntry);
            if (binData != null)
            {
                Debug.WriteLine($"[LoadGck] Extracted BIN: {binData.Length} bytes");
                var reader = new BinWorldReader();
                _worldRoot = reader.Load(binData);
                Debug.WriteLine($"[LoadGck] Root node: {_worldRoot?.Name}, children: {_worldRoot?.NodeCount ?? 0}");
            }
        }

        if (gtiEntry != null)
        {
            byte[]? gtiData = GzpArchive.ExtractFile(gckPath, gtiEntry);
            if (gtiData != null)
            {
                Debug.WriteLine($"[LoadGck] Extracted GTI: {gtiData.Length} bytes");
                LoadTerrainFromBytes(gtiData);
            }
        }

        FilePath = gckPath;
        TerrainPath = null;
        IsModified = false;
        WorldChanged?.Invoke();
    }

    /// <summary>
    /// Saves the current world to a .bin file.
    /// </summary>
    public void SaveWorld(string? path = null)
    {
        if (_worldRoot == null) return;
        path ??= FilePath;
        if (path == null) return;

        var writer = new BinWorldWriter();
        byte[] data = writer.Save(_worldRoot);
        File.WriteAllBytes(path, data);
        FilePath = path;
        IsModified = false;
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

            float ax = obj.FindChildLeaf("Angle X")?.SingleValue ?? 0;
            float ay = obj.FindChildLeaf("Angle Y")?.SingleValue ?? 0;
            float az = obj.FindChildLeaf("Angle")?.SingleValue ??
                        obj.FindChildLeaf("Angle Z")?.SingleValue ?? 0;

            result.Add(new ObjectInstance
            {
                ModelId = typeLeaf.Int32Value,
                Position = new Vector3(x, y, z),
                Rotation = new Vector3(ax, ay, az),
                Scale = scale
            });
        }

        return result;
    }

    /// <summary>
    /// Builds terrain render data for the current terrain.
    /// </summary>
    public TerrainRenderData? BuildTerrainRenderData()
    {
        if (_terrain == null)
        {
            Debug.WriteLine("[BuildTerrainRenderData] No terrain loaded");
            return null;
        }
        var data = TerrainMeshBuilder.Build(_terrain);
        Debug.WriteLine($"[BuildTerrainRenderData] Built mesh: {data.VertexCount} vertices, {data.IndexCount} indices");
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
}
