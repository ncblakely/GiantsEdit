using GiantsEdit.Core.DataModel;
using GiantsEdit.Core.Formats;
using GiantsEdit.Core.Rendering;

namespace GiantsEdit.Core.Services;

/// <summary>
/// Manages loading GBS models from the game's GZP archives and uploading them to the renderer.
/// Matches the Delphi objectmanager.pas GetModel/DrawModel flow.
/// </summary>
public class ModelManager
{
    private readonly ObjectCatalog _catalog;
    private Dictionary<string, GzpEntry> _gzpIndex = new(StringComparer.OrdinalIgnoreCase);
    private string _overridePath = string.Empty;

    // Cache: object type ID → (parsed model, uploaded renderer ID)
    private readonly Dictionary<int, GbsModel?> _modelCache = [];
    private readonly Dictionary<int, int> _rendererIds = []; // type ID → renderer model ID
    private readonly HashSet<int> _failedIds = []; // IDs that failed to load

    public ModelManager(ObjectCatalog catalog)
    {
        _catalog = catalog;
    }

    /// <summary>
    /// Scans a Giants installation directory to build the GZP file index.
    /// </summary>
    /// <param name="giantsFolder">Path to the Giants installation (parent of bin/).</param>
    public void SetGamePath(string giantsFolder)
    {
        string binPath = Path.Combine(giantsFolder, "bin");
        _overridePath = Path.Combine(binPath, "override");
        _gzpIndex = GzpNativeReader.BuildIndexFromDirectory(binPath);
    }

    /// <summary>
    /// Whether the game path has been set and GZP files indexed.
    /// </summary>
    public bool HasGameData => _gzpIndex.Count > 0;

    /// <summary>
    /// Loads a file by name, checking bin\override\ first, then GZP archives.
    /// Matches Delphi's LoadGZPFile behavior.
    /// </summary>
    public byte[]? LoadGameFile(string name)
    {
        // Check override folder first
        if (!string.IsNullOrEmpty(_overridePath))
        {
            string overridFile = Path.Combine(_overridePath, name);
            if (File.Exists(overridFile))
                return File.ReadAllBytes(overridFile);
        }

        // Then check GZP archives
        if (_gzpIndex.TryGetValue(name, out var entry))
        {
            try
            {
                return GzpNativeReader.ExtractEntry(entry);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the model filename for an object type ID using the catalog.
    /// Matches Delphi's GetModel model name resolution logic.
    /// </summary>
    public static string? ResolveModelFilename(ObjectCatalog catalog, int typeId)
    {
        var entries = catalog.GetById(typeId);
        if (entries.Count == 0) return null;

        string s = entries[0].ModelPath;
        if (string.IsNullOrEmpty(s)) return null;

        int parenIdx = s.IndexOf('(');
        if (parenIdx > 0)
        {
            // e.g. "kb (0)" or "mc (0..7)" or "rp_bow (_..5)"
            string s2 = s[(parenIdx + 1)..].TrimEnd(')').Trim();
            s = s[..(parenIdx - 1)].Trim(); // base name before " ("

            // Take first value: before comma or dot range
            int commaIdx = s2.IndexOf(',');
            if (commaIdx > 0) s2 = s2[..commaIdx];
            int dotIdx = s2.IndexOf('.');
            if (dotIdx > 0) s2 = s2[..dotIdx];

            if (s2 == "_")
            {
                // Use base name as-is
            }
            else if (s2.Length == 1 && char.IsAsciiDigit(s2[0]))
            {
                s = s + "_L" + s2; // e.g. "kb" + "_L0" = "kb_L0"
            }
            else
            {
                s = s2; // Use the inner part as the model name
            }
        }

        return s;
    }

    /// <summary>
    /// Tries to load and upload a model for the given type ID.
    /// Returns the renderer model ID, or -1 if unavailable.
    /// </summary>
    public int GetOrLoadModel(int typeId, IRenderer renderer)
    {
        // Already uploaded?
        if (_rendererIds.TryGetValue(typeId, out int rendId))
            return rendId;

        // Already failed?
        if (_failedIds.Contains(typeId))
            return -1;

        // Resolve model filename
        string? modelName = ResolveModelFilename(_catalog, typeId);
        if (modelName == null)
        {
            _failedIds.Add(typeId);
            return -1;
        }

        // Load .gbs data
        byte[]? gbsData = LoadGameFile(modelName + ".gbs");
        if (gbsData == null || gbsData.Length == 0)
        {
            _failedIds.Add(typeId);
            return -1;
        }

        try
        {
            var model = GbsModelLoader.Load(gbsData);
            var renderData = GbsModelConverter.ToRenderData(model);

            // Load textures for each part
            foreach (var part in renderData.Parts)
            {
                if (string.IsNullOrEmpty(part.TextureName)) continue;
                byte[]? texData = LoadGameFile(part.TextureName + ".tga");
                if (texData != null && texData.Length > 18)
                {
                    try
                    {
                        part.TextureImage = TgaLoader.Load(texData);
                    }
                    catch { /* texture load failure is non-fatal */ }
                }
            }

            int id = renderer.UploadModel(renderData, typeId);
            _rendererIds[typeId] = id;
            _modelCache[typeId] = model;
            return id;
        }
        catch
        {
            _failedIds.Add(typeId);
            return -1;
        }
    }

    /// <summary>
    /// Preloads all models referenced by the current object list.
    /// Call on the GL thread.
    /// </summary>
    public void PreloadModels(IEnumerable<ObjectInstance> objects, IRenderer renderer)
    {
        var seen = new HashSet<int>();
        foreach (var obj in objects)
        {
            if (obj.ModelId < 0) continue; // Negative IDs are special marker shapes
            if (seen.Add(obj.ModelId))
                GetOrLoadModel(obj.ModelId, renderer);
        }
    }

    /// <summary>
    /// Clears the model cache (e.g. when changing game path).
    /// </summary>
    public void ClearCache()
    {
        _modelCache.Clear();
        _rendererIds.Clear();
        _failedIds.Clear();
    }
}
